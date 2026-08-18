using SolarBmsMonitor.Core.Calculations;

namespace SolarBmsMonitor.App.ViewModels;

public enum TimeRange
{
    LastHour,
    LastDay,
    LastWeek,
    All
}

/// <param name="IsTruncated">
/// The query hit the sample cap, so the charts show only the most recent part
/// of the requested range. The page says so rather than letting the range
/// button imply a completeness the data does not have.
/// </param>
public sealed record ChartDataBundle(
    ChartSeries? VoltageSeries,
    ChartSeries? TemperatureSeries,
    ChartSeries? PowerSeries,
    ChartSeries? SocSeries,
    IReadOnlyList<CellBalanceData> CellBalance,
    TimeRange SelectedTimeRange,
    bool IsTruncated);
