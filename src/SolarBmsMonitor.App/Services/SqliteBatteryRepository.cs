using System.Collections.Concurrent;
using System.Text.Json;
using SolarBmsMonitor.Core.Models;
using SolarBmsMonitor.Core.Services;
using SQLite;
using BatteryDeviceInfo = SolarBmsMonitor.Core.Models.DeviceInfo;

namespace SolarBmsMonitor.App.Services;

public sealed class SqliteBatteryRepository : IBatteryRepository, IDisposable
{
    private static readonly SQLiteOpenFlags OpenFlags =
        SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex;

    private readonly SQLiteAsyncConnection _database = new(
        Path.Combine(FileSystem.AppDataDirectory, "solar-bms-monitor.db3"), OpenFlags);
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly ConcurrentDictionary<string, BatterySnapshot> _lastSaved = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _activeSessionIds = new(StringComparer.Ordinal);
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _database.CreateTableAsync<DeviceRow>().WaitAsync(cancellationToken);
            await _database.CreateTableAsync<SnapshotRow>().WaitAsync(cancellationToken);
            await _database.CreateTableAsync<DiagnosticRow>().WaitAsync(cancellationToken);
            await _database.CreateTableAsync<SessionRow>().WaitAsync(cancellationToken);
            await _database.CreateTableAsync<EventRow>().WaitAsync(cancellationToken);
            await _database.ExecuteAsync(
                    "UPDATE connection_sessions SET EndedUnixMilliseconds = ?, EndReason = ? WHERE EndedUnixMilliseconds IS NULL",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    "Sesión interrumpida (cierre inesperado o apagón)")
                .WaitAsync(cancellationToken);
            await _database.EnableWriteAheadLoggingAsync().WaitAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task SaveDeviceAsync(BatteryDeviceInfo device, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var row = DeviceRow.FromModel(device);
        var existing = await _database.FindAsync<DeviceRow>(device.DeviceId).WaitAsync(cancellationToken);
        if (existing is not null)
        {
            row.FirstConnectedUnixMilliseconds = existing.FirstConnectedUnixMilliseconds;
        }

        await _database.InsertOrReplaceAsync(row).WaitAsync(cancellationToken);
    }

    public async Task BeginSessionAsync(
        string deviceId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (_activeSessionIds.ContainsKey(deviceId))
        {
            return;
        }

        var row = new SessionRow
        {
            DeviceId = deviceId,
            StartedUnixMilliseconds = startedAt.ToUnixTimeMilliseconds(),
        };
        await _database.InsertAsync(row).WaitAsync(cancellationToken);
        _activeSessionIds[deviceId] = row.Id;
    }

    public async Task EndSessionAsync(
        string deviceId,
        DateTimeOffset endedAt,
        string reason,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!_activeSessionIds.TryRemove(deviceId, out var sessionId))
        {
            return;
        }

        var row = await _database.FindAsync<SessionRow>(sessionId).WaitAsync(cancellationToken);
        if (row is null)
        {
            return;
        }

        row.EndedUnixMilliseconds = endedAt.ToUnixTimeMilliseconds();
        row.EndReason = reason;
        await _database.UpdateAsync(row).WaitAsync(cancellationToken);
    }

    public async Task<bool> SaveSnapshotIfSignificantAsync(
        BatterySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (_lastSaved.TryGetValue(snapshot.DeviceId, out var previous) && !IsSignificant(previous, snapshot))
        {
            return false;
        }

        await _database.InsertAsync(SnapshotRow.FromModel(snapshot)).WaitAsync(cancellationToken);
        await AccumulateSessionEnergyAsync(snapshot, cancellationToken);
        foreach (var alarm in snapshot.Alarms.Where(alarm => alarm.IsActive))
        {
            await _database.InsertAsync(EventRow.FromAlarm(snapshot, alarm)).WaitAsync(cancellationToken);
        }
        _lastSaved[snapshot.DeviceId] = snapshot;
        return true;
    }

    public async Task SaveDiagnosticAsync(DiagnosticEntry entry, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertAsync(DiagnosticRow.FromModel(entry)).WaitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BatterySnapshot>> GetRecentSnapshotsAsync(
        string deviceId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await InitializeAsync(cancellationToken);
        var rows = await _database.Table<SnapshotRow>()
            .Where(row => row.DeviceId == deviceId)
            .OrderByDescending(row => row.TimestampUnixMilliseconds)
            .Take(limit)
            .ToListAsync()
            .WaitAsync(cancellationToken);
        return rows.Select(row => row.ToModel()).ToArray();
    }

    private static bool IsSignificant(BatterySnapshot previous, BatterySnapshot current)
    {
        if (current.Timestamp - previous.Timestamp >= TimeSpan.FromSeconds(30))
        {
            return true;
        }

        return Changed(previous.StateOfChargePercent, current.StateOfChargePercent, 0.5)
            || Changed(previous.PackVoltageVolts, current.PackVoltageVolts, 0.1)
            || Changed(previous.CurrentAmps, current.CurrentAmps, 1)
            || Changed(previous.CellDeltaMillivolts, current.CellDeltaMillivolts, 5)
            || previous.Alarms.Count != current.Alarms.Count
            || previous.ChargeState != current.ChargeState;
    }

    private static bool Changed(double? previous, double? current, double threshold) =>
        previous != current && (previous is null || current is null || Math.Abs(previous.Value - current.Value) >= threshold);

    private async Task AccumulateSessionEnergyAsync(BatterySnapshot current, CancellationToken cancellationToken)
    {
        if (!_activeSessionIds.TryGetValue(current.DeviceId, out var sessionId)
            || !_lastSaved.TryGetValue(current.DeviceId, out var previous)
            || previous.PowerWatts is null
            || current.PowerWatts is null)
        {
            return;
        }

        var elapsed = current.Timestamp - previous.Timestamp;
        if (elapsed <= TimeSpan.Zero || elapsed > TimeSpan.FromMinutes(10))
        {
            return;
        }

        var session = await _database.FindAsync<SessionRow>(sessionId).WaitAsync(cancellationToken);
        if (session is null)
        {
            return;
        }

        var averagePowerWatts = (previous.PowerWatts.Value + current.PowerWatts.Value) / 2;
        var wattHours = averagePowerWatts * elapsed.TotalHours;
        if (wattHours >= 0)
        {
            session.EstimatedChargedWattHours += wattHours;
        }
        else
        {
            session.EstimatedDischargedWattHours += -wattHours;
        }

        await _database.UpdateAsync(session).WaitAsync(cancellationToken);
    }

    [Table("devices")]
    private sealed class DeviceRow
    {
        [PrimaryKey]
        public string DeviceId { get; set; } = string.Empty;
        public string BleName { get; set; } = string.Empty;
        public int Rssi { get; set; }
        public string? FirmwareVersion { get; set; }
        public string? HardwareVersion { get; set; }
        public int? CellCount { get; set; }
        public double? NominalCapacityAh { get; set; }
        public long FirstConnectedUnixMilliseconds { get; set; }
        public long LastConnectedUnixMilliseconds { get; set; }

        public static DeviceRow FromModel(BatteryDeviceInfo model) => new()
        {
            DeviceId = model.DeviceId,
            BleName = model.BleName,
            Rssi = model.Rssi,
            FirmwareVersion = model.FirmwareVersion,
            HardwareVersion = model.HardwareVersion,
            CellCount = model.CellCount,
            NominalCapacityAh = model.NominalCapacityAh,
            FirstConnectedUnixMilliseconds = model.FirstConnectedAt.ToUnixTimeMilliseconds(),
            LastConnectedUnixMilliseconds = model.LastConnectedAt.ToUnixTimeMilliseconds(),
        };
    }

    [Table("snapshots")]
    private sealed class SnapshotRow
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }
        [Indexed]
        public string DeviceId { get; set; } = string.Empty;
        [Indexed]
        public long TimestampUnixMilliseconds { get; set; }
        public double? StateOfChargePercent { get; set; }
        public double? StateOfHealthPercent { get; set; }
        public double? PackVoltageVolts { get; set; }
        public double? CurrentAmps { get; set; }
        public double? PowerWatts { get; set; }
        public double? RemainingCapacityAh { get; set; }
        public double? FullCapacityAh { get; set; }
        public double? DesignedCapacityAh { get; set; }
        public int? CycleCount { get; set; }
        public string CellVoltagesJson { get; set; } = "[]";
        public string TemperaturesJson { get; set; } = "[]";
        public double? CellDeltaMillivolts { get; set; }
        public int ChargeState { get; set; }
        public string AlarmsJson { get; set; } = "[]";
        public int DataQuality { get; set; }
        public bool IsStale { get; set; }

        public static SnapshotRow FromModel(BatterySnapshot model) => new()
        {
            DeviceId = model.DeviceId,
            TimestampUnixMilliseconds = model.Timestamp.ToUnixTimeMilliseconds(),
            StateOfChargePercent = model.StateOfChargePercent,
            StateOfHealthPercent = model.StateOfHealthPercent,
            PackVoltageVolts = model.PackVoltageVolts,
            CurrentAmps = model.CurrentAmps,
            PowerWatts = model.PowerWatts,
            RemainingCapacityAh = model.RemainingCapacityAh,
            FullCapacityAh = model.FullCapacityAh,
            DesignedCapacityAh = model.DesignedCapacityAh,
            CycleCount = model.CycleCount,
            CellVoltagesJson = JsonSerializer.Serialize(model.CellVoltages),
            TemperaturesJson = JsonSerializer.Serialize(model.TemperaturesCelsius),
            CellDeltaMillivolts = model.CellDeltaMillivolts,
            ChargeState = (int)model.ChargeState,
            AlarmsJson = JsonSerializer.Serialize(model.Alarms),
            DataQuality = (int)model.DataQuality,
            IsStale = model.IsStale,
        };

        public BatterySnapshot ToModel() => new(
            DateTimeOffset.FromUnixTimeMilliseconds(TimestampUnixMilliseconds),
            DeviceId,
            StateOfChargePercent,
            StateOfHealthPercent,
            PackVoltageVolts,
            CurrentAmps,
            PowerWatts,
            RemainingCapacityAh,
            FullCapacityAh,
            DesignedCapacityAh,
            CycleCount,
            JsonSerializer.Deserialize<double[]>(CellVoltagesJson) ?? [],
            JsonSerializer.Deserialize<double[]>(TemperaturesJson) ?? [],
            CellDeltaMillivolts,
            (SolarBmsMonitor.Core.Models.ChargeState)ChargeState,
            JsonSerializer.Deserialize<BatteryAlarm[]>(AlarmsJson) ?? [],
            (SolarBmsMonitor.Core.Models.DataQuality)DataQuality,
            IsStale);
    }

    [Table("diagnostics")]
    private sealed class DiagnosticRow
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }
        [Indexed]
        public long TimestampUnixMilliseconds { get; set; }
        public int Direction { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Hex { get; set; }

        public static DiagnosticRow FromModel(DiagnosticEntry model) => new()
        {
            TimestampUnixMilliseconds = model.Timestamp.ToUnixTimeMilliseconds(),
            Direction = (int)model.Direction,
            Message = model.Message,
            Hex = model.Hex,
        };
    }

    [Table("connection_sessions")]
    private sealed class SessionRow
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }
        [Indexed]
        public string DeviceId { get; set; } = string.Empty;
        public long StartedUnixMilliseconds { get; set; }
        public long? EndedUnixMilliseconds { get; set; }
        public string? EndReason { get; set; }
        public double EstimatedChargedWattHours { get; set; }
        public double EstimatedDischargedWattHours { get; set; }
    }

    [Table("battery_events")]
    private sealed class EventRow
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }
        [Indexed]
        public string DeviceId { get; set; } = string.Empty;
        [Indexed]
        public long TimestampUnixMilliseconds { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public static EventRow FromAlarm(BatterySnapshot snapshot, BatteryAlarm alarm) => new()
        {
            DeviceId = snapshot.DeviceId,
            TimestampUnixMilliseconds = snapshot.Timestamp.ToUnixTimeMilliseconds(),
            Code = alarm.Code,
            Description = alarm.Description,
        };
    }

    public void Dispose() => _initializationGate.Dispose();
}
