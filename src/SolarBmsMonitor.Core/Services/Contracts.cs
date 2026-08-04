using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Core.Services;

public interface IBleMonitorService : IAsyncDisposable
{
    BleAvailability Availability { get; }
    BleConnectionState ConnectionState { get; }
    bool CaptureDiagnosticFrames { get; set; }
    GattProfile? CurrentProfile { get; }
    event EventHandler<BleDevice>? DeviceDiscovered;
    event EventHandler<BleConnectionState>? ConnectionStateChanged;
    event EventHandler<DiagnosticEntry>? DiagnosticEntryReceived;
    event EventHandler<BatterySnapshot>? SnapshotReceived;
    Task<bool> EnsurePermissionsAsync(CancellationToken cancellationToken);
    Task ScanAsync(TimeSpan duration, CancellationToken cancellationToken);
    Task ConnectAsync(BleDevice device, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task QueryTelemetryAsync(CancellationToken cancellationToken);
    DiagnosticReport CreateDiagnosticReport();
}

public interface IBatteryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task SaveDeviceAsync(DeviceInfo device, CancellationToken cancellationToken);
    Task BeginSessionAsync(string deviceId, DateTimeOffset startedAt, CancellationToken cancellationToken);
    Task EndSessionAsync(string deviceId, DateTimeOffset endedAt, string reason, CancellationToken cancellationToken);
    Task<bool> SaveSnapshotIfSignificantAsync(BatterySnapshot snapshot, CancellationToken cancellationToken);
    Task SaveDiagnosticAsync(DiagnosticEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<BatterySnapshot>> GetRecentSnapshotsAsync(string deviceId, int limit, CancellationToken cancellationToken);
}

public interface IExportService
{
    Task<string> ExportDiagnosticsJsonAsync(DiagnosticReport report, CancellationToken cancellationToken);
    Task<string> ExportHistoryCsvAsync(IReadOnlyList<BatterySnapshot> snapshots, CancellationToken cancellationToken);
}
