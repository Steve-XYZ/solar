namespace SolarBmsMonitor.App.ViewModels;

public enum TimeRange
{
    LastHour,
    LastDay,
    LastWeek,
    All
}

public sealed record ChartDataPoint(
    DateTimeOffset Timestamp,
    double Value,
    string Label);

public sealed record ChartSeries(
    string Title,
    string Color,
    IReadOnlyList<ChartDataPoint> Points);

public sealed record CellBalanceData(
    int CellNumber,
    double Voltage,
    bool IsMinimum,
    bool IsMaximum);

public sealed record ChartDataBundle(
    ChartSeries? VoltageSeries,
    ChartSeries? TemperatureSeries,
    ChartSeries? PowerSeries,
    ChartSeries? SocSeries,
    IReadOnlyList<CellBalanceData> CellBalance,
    TimeRange SelectedTimeRange);