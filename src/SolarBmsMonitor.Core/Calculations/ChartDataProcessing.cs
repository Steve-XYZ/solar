using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Core.Calculations;

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
    bool IsMaximum)
{
    public bool IsExtreme => IsMinimum || IsMaximum;
}

public static class ChartDataProcessing
{
    public static ChartSeries? CreateSeries(
        IReadOnlyList<BatterySnapshot> snapshots,
        string title,
        string color,
        Func<BatterySnapshot, double?> valueSelector)
    {
        var points = new List<ChartDataPoint>();
        var totalPoints = snapshots.Count;
        var labelInterval = CalculateLabelInterval(totalPoints);

        for (int i = 0; i < totalPoints; i++)
        {
            var snapshot = snapshots[i];
            var value = valueSelector(snapshot);
            if (value.HasValue)
            {
                var shouldLabel = i % labelInterval == 0 || i == totalPoints - 1;
                var label = shouldLabel ? FormatTimestamp(snapshot.Timestamp, totalPoints) : string.Empty;
                points.Add(new ChartDataPoint(snapshot.Timestamp, value.Value, label));
            }
        }

        return points.Count > 0 ? new ChartSeries(title, color, points) : null;
    }

    public static List<CellBalanceData> CreateCellBalanceData(BatterySnapshot? snapshot)
    {
        if (snapshot?.CellVoltages is not { Count: > 0 } voltages)
        {
            return [];
        }

        var minimum = voltages.Min();
        var maximum = voltages.Max();

        // Same rule the cells page uses, so both screens agree on which card
        // is the low one. Naming the extremes is not a balance verdict: the
        // colour stays neutral and CellBalance.Evaluate keeps that judgement.
        var hasSpread = CellBalance.HasMeaningfulSpread(voltages);

        return voltages
            .Select((voltage, index) => new CellBalanceData(
                index + 1,
                voltage,
                hasSpread && voltage <= minimum,
                hasSpread && voltage >= maximum))
            .ToList();
    }

    public static int CalculateLabelInterval(int totalPoints)
    {
        if (totalPoints <= 10) return 1;
        if (totalPoints <= 20) return 2;
        if (totalPoints <= 50) return 5;
        if (totalPoints <= 100) return 10;
        if (totalPoints <= 200) return 20;
        if (totalPoints <= 500) return 50;
        return 100;
    }

    /// <summary>
    /// A handful of points covers a short window, where seconds distinguish
    /// one sample from the next. Once the series is long the labels are thinned
    /// and span hours, so the seconds are noise that only makes each label
    /// wider and more likely to collide with its neighbour.
    /// </summary>
    public static string FormatTimestamp(DateTimeOffset timestamp, int totalPoints)
    {
        var localTimestamp = timestamp.ToLocalTime();
        var format = totalPoints <= 10 ? "HH:mm:ss" : "HH:mm";
        return localTimestamp.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }
}
