using SolarBmsMonitor.App.ViewModels;
using SolarBmsMonitor.Core.Calculations;
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
            return new ChartDataBundle(null, null, null, null, new List<CellBalanceData>(), timeRange);
        }

        var voltageSeries = ChartDataProcessing.CreateSeries(snapshots, "Voltaje (V)", "#FF6B5E", s => s.PackVoltageVolts);
        var temperatureSeries = ChartDataProcessing.CreateSeries(snapshots, "Temperatura (°C)", "#1268E8", s => s.TemperaturesCelsius.Count > 0 ? s.TemperaturesCelsius.Average() : null);
        var powerSeries = ChartDataProcessing.CreateSeries(snapshots, "Potencia (W)", "#45CFA4", s => s.PowerWatts);
        var socSeries = ChartDataProcessing.CreateSeries(snapshots, "SOC (%)", "#2CB78A", s => s.StateOfChargePercent);

        var cellBalance = ChartDataProcessing.CreateCellBalanceData(snapshots[^1]);

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
        var cutoff = timeRange switch
        {
            TimeRange.LastHour => DateTimeOffset.UtcNow.AddHours(-1),
            TimeRange.LastDay => DateTimeOffset.UtcNow.AddDays(-1),
            TimeRange.LastWeek => DateTimeOffset.UtcNow.AddDays(-7),
            TimeRange.All => (DateTimeOffset?)null,
            _ => DateTimeOffset.UtcNow.AddDays(-1)
        };

        var allSnapshots = await _repository.GetRecentSnapshotsAsync(deviceId, 1000, cutoff, cancellationToken);

        var filtered = new List<BatterySnapshot>();
        foreach (var snapshot in allSnapshots)
        {
            filtered.Add(snapshot);
        }
        filtered.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return filtered;
    }
}
