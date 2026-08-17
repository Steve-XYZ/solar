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
    bool IsMaximum);

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
        var balanceLevel = CellBalance.Evaluate(snapshot.CellDeltaMillivolts);
        var hasSpread = balanceLevel != CellBalanceLevel.Balanced;

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

    public static string FormatTimestamp(DateTimeOffset timestamp, int totalPoints)
    {
        var localTimestamp = timestamp.ToLocalTime();
        if (totalPoints <= 10)
        {
            return localTimestamp.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        return localTimestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}