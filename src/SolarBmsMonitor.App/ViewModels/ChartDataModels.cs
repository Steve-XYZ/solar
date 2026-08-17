using SolarBmsMonitor.Core.Calculations;

namespace SolarBmsMonitor.App.ViewModels;

public enum TimeRange
{
    LastHour,
    LastDay,
    LastWeek,
    All
}

public sealed record ChartDataBundle(
    ChartSeries? VoltageSeries,
    ChartSeries? TemperatureSeries,
    ChartSeries? PowerSeries,
    ChartSeries? SocSeries,
    System.Collections.Generic.List<CellBalanceData> CellBalance,
    TimeRange SelectedTimeRange);
