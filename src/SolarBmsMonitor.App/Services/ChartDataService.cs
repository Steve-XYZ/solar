using SolarBmsMonitor.App.ViewModels;
using SolarBmsMonitor.Core.Models;
using SolarBmsMonitor.Core.Services;

namespace SolarBmsMonitor.App.Services;

public interface IChartDataService
{
    Task<ChartDataBundle> GenerateChartDataAsync(
        string deviceId,
        TimeRange timeRange,
        CancellationToken cancellationToken = default);
}

public sealed class ChartDataService : IChartDataService
{
    private readonly IBatteryRepository _repository;

    public ChartDataService(IBatteryRepository repository)
    {
        _repository = repository;
    }

    public async Task<ChartDataBundle> GenerateChartDataAsync(
        string deviceId,
        TimeRange timeRange,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await GetFilteredSnapshotsAsync(deviceId, timeRange, cancellationToken);
        
        if (snapshots.Count == 0)
        {
            return new ChartDataBundle(null, null, null, null, [], timeRange);
        }

        var voltageSeries = CreateSeries(snapshots, "Voltaje (V)", "#FF6B6B", s => s.PackVoltageVolts);
        var temperatureSeries = CreateSeries(snapshots, "Temperatura (°C)", "#4ECDC4", s => s.TemperaturesCelsius.Count > 0 ? s.TemperaturesCelsius.Average() : null);
        var powerSeries = CreateSeries(snapshots, "Potencia (W)", "#45B7D1", s => s.PowerWatts);
        var socSeries = CreateSeries(snapshots, "SOC (%)", "#96CEB4", s => s.StateOfChargePercent);
        
        var cellBalance = snapshots.Count > 0 ? CreateCellBalanceData(snapshots[^1]) : [];

        return new ChartDataBundle(
            voltageSeries,
            temperatureSeries,
            powerSeries,
            socSeries,
            cellBalance,
            timeRange);
    }

    private async Task<IReadOnlyList<BatterySnapshot>> GetFilteredSnapshotsAsync(
        string deviceId,
        TimeRange timeRange,
        CancellationToken cancellationToken)
    {
        var allSnapshots = await _repository.GetRecentSnapshotsAsync(deviceId, 1000, cancellationToken);
        
        var cutoff = timeRange switch
        {
            TimeRange.LastHour => DateTimeOffset.UtcNow.AddHours(-1),
            TimeRange.LastDay => DateTimeOffset.UtcNow.AddDays(-1),
            TimeRange.LastWeek => DateTimeOffset.UtcNow.AddDays(-7),
            TimeRange.All => DateTimeOffset.MinValue,
            _ => DateTimeOffset.UtcNow.AddDays(-1)
        };

        var filtered = new List<BatterySnapshot>();
        foreach (var snapshot in allSnapshots)
        {
            if (snapshot.Timestamp >= cutoff)
            {
                filtered.Add(snapshot);
            }
        }
        filtered.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return filtered;
    }

    private static ChartSeries? CreateSeries(
        IReadOnlyList<BatterySnapshot> snapshots,
        string title,
        string color,
        Func<BatterySnapshot, double?> valueSelector)
    {
        var points = new List<ChartDataPoint>();
        
        foreach (var snapshot in snapshots)
        {
            var value = valueSelector(snapshot);
            if (value.HasValue)
            {
                var label = FormatTimestamp(snapshot.Timestamp, snapshots.Count);
                points.Add(new ChartDataPoint(snapshot.Timestamp, value.Value, label));
            }
        }

        return points.Count > 0 ? new ChartSeries(title, color, points) : null;
    }

    private static string FormatTimestamp(DateTimeOffset timestamp, int totalPoints)
    {
        if (totalPoints <= 10)
        {
            return timestamp.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }
        
        return timestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<CellBalanceData> CreateCellBalanceData(BatterySnapshot? snapshot)
    {
        if (snapshot?.CellVoltages is not { Count: > 0 } voltages)
        {
            return [];
        }

        var minimum = voltages.Min();
        var maximum = voltages.Max();
        var hasSpread = maximum - minimum > 0.0005;

        return voltages
            .Select((voltage, index) => new CellBalanceData(
                index + 1,
                voltage,
                hasSpread && voltage <= minimum,
                hasSpread && voltage >= maximum))
            .ToList();
    }
}