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
    // Enough samples to draw a readable trend without pulling a whole season
    // of rows into memory. When a range needs more than this the page says the
    // view is partial instead of quietly dropping the older half.
    private const int SampleLimit = 1000;

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
        var isTruncated = snapshots.Count >= SampleLimit;

        if (snapshots.Count == 0)
        {
            return new ChartDataBundle(null, null, null, null, [], timeRange, false);
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
            timeRange,
            isTruncated);
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

        var recent = await _repository.GetRecentSnapshotsAsync(deviceId, SampleLimit, cutoff, cancellationToken);

        // The repository hands back the newest rows first; the charts read
        // left to right.
        var ordered = recent.ToList();
        ordered.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return ordered;
    }
}
