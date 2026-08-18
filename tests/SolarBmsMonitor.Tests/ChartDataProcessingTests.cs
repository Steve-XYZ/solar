using System.Globalization;
using SolarBmsMonitor.Core.Calculations;
using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Tests;

public class ChartDataProcessingTests
{
    [Fact]
    public void CreateSeries_WithEmptySnapshots_ReturnsNull()
    {
        var snapshots = new List<BatterySnapshot>();
        var result = ChartDataProcessing.CreateSeries(
            snapshots,
            "Test",
            "#FF0000",
            s => s.PackVoltageVolts);

        Assert.Null(result);
    }

    [Fact]
    public void CreateSeries_WithValidSnapshots_ReturnsSeries()
    {
        var snapshots = new List<BatterySnapshot>
        {
            CreateSnapshot(50.0, DateTimeOffset.UtcNow.AddMinutes(-10)),
            CreateSnapshot(51.0, DateTimeOffset.UtcNow.AddMinutes(-5)),
            CreateSnapshot(52.0, DateTimeOffset.UtcNow)
        };

        var result = ChartDataProcessing.CreateSeries(
            snapshots,
            "Test",
            "#FF0000",
            s => s.PackVoltageVolts);

        Assert.NotNull(result);
        Assert.Equal("Test", result.Title);
        Assert.Equal("#FF0000", result.Color);
        Assert.Equal(3, result.Points.Count);
    }

    [Fact]
    public void CreateSeries_WithNullValues_FiltersThemOut()
    {
        var snapshots = new List<BatterySnapshot>
        {
            CreateSnapshot(50.0, DateTimeOffset.UtcNow.AddMinutes(-10)),
            CreateSnapshot(null, DateTimeOffset.UtcNow.AddMinutes(-5)),
            CreateSnapshot(52.0, DateTimeOffset.UtcNow)
        };

        var result = ChartDataProcessing.CreateSeries(
            snapshots,
            "Test",
            "#FF0000",
            s => s.PackVoltageVolts);

        Assert.NotNull(result);
        Assert.Equal(2, result.Points.Count);
    }

    [Fact]
    public void CalculateLabelInterval_WithFewPoints_ReturnsOne()
    {
        Assert.Equal(1, ChartDataProcessing.CalculateLabelInterval(5));
        Assert.Equal(1, ChartDataProcessing.CalculateLabelInterval(10));
    }

    [Fact]
    public void CalculateLabelInterval_WithManyPoints_ReturnsLargerInterval()
    {
        Assert.Equal(2, ChartDataProcessing.CalculateLabelInterval(15));
        Assert.Equal(5, ChartDataProcessing.CalculateLabelInterval(30));
        Assert.Equal(10, ChartDataProcessing.CalculateLabelInterval(80));
        Assert.Equal(20, ChartDataProcessing.CalculateLabelInterval(150));
        Assert.Equal(50, ChartDataProcessing.CalculateLabelInterval(300));
        Assert.Equal(100, ChartDataProcessing.CalculateLabelInterval(1000));
    }

    [Fact]
    public void CreateCellBalanceData_WithEmptySnapshot_ReturnsEmpty()
    {
        var result = ChartDataProcessing.CreateCellBalanceData(null);
        Assert.Empty(result);
    }

    [Fact]
    public void CreateCellBalanceData_WithNoCellVoltages_ReturnsEmpty()
    {
        var snapshot = CreateSnapshot(50.0, DateTimeOffset.UtcNow);
        var result = ChartDataProcessing.CreateCellBalanceData(snapshot);
        Assert.Empty(result);
    }

    [Fact]
    public void CreateCellBalanceData_WithBalancedCells_NoExtremesMarked()
    {
        var voltages = new[] { 3.2, 3.2, 3.2, 3.2 };
        var snapshot = CreateSnapshot(50.0, DateTimeOffset.UtcNow, voltages, 10.0);

        var result = ChartDataProcessing.CreateCellBalanceData(snapshot);

        Assert.Equal(4, result.Count);
        Assert.All(result, data => Assert.False(data.IsMinimum));
        Assert.All(result, data => Assert.False(data.IsMaximum));
    }

    [Fact]
    public void CreateCellBalanceData_WithUnbalancedCells_MarksExtremes()
    {
        var voltages = new[] { 3.1, 3.2, 3.3, 3.4 };
        var snapshot = CreateSnapshot(50.0, DateTimeOffset.UtcNow, voltages, 100.0);

        var result = ChartDataProcessing.CreateCellBalanceData(snapshot);

        Assert.Equal(4, result.Count);
        Assert.True(result[0].IsMinimum);
        Assert.True(result[3].IsMaximum);
        Assert.False(result[1].IsMinimum);
        Assert.False(result[2].IsMaximum);
    }

    [Fact]
    public void CreateCellBalanceData_WithBalancedButMeasurableSpread_StillMarksExtremes()
    {
        // 5 mV of spread: CellBalance.Evaluate calls this pack Balanced, but
        // the card still has to say which cell is the low one. Naming the
        // extremes and judging the balance are separate questions.
        var voltages = new[] { 3.200, 3.202, 3.203, 3.205 };
        var snapshot = CreateSnapshot(50.0, DateTimeOffset.UtcNow, voltages, 5.0);

        Assert.Equal(CellBalanceLevel.Balanced, CellBalance.Evaluate(snapshot.CellDeltaMillivolts));

        var result = ChartDataProcessing.CreateCellBalanceData(snapshot);

        Assert.True(result[0].IsMinimum);
        Assert.True(result[3].IsMaximum);
        Assert.True(result[0].IsExtreme);
        Assert.False(result[1].IsExtreme);
    }

    [Fact]
    public void CreateCellBalanceData_WithUnknownDelta_StillMarksExtremes()
    {
        // A missing delta is not a reason to stop pointing at the ends of the
        // spread; the voltages themselves answer the question.
        var voltages = new[] { 3.1, 3.2, 3.3 };
        var snapshot = CreateSnapshot(50.0, DateTimeOffset.UtcNow, voltages, cellDelta: null);

        var result = ChartDataProcessing.CreateCellBalanceData(snapshot);

        Assert.True(result[0].IsMinimum);
        Assert.True(result[2].IsMaximum);
    }

    [Fact]
    public void CreateSeries_WithManyPoints_LabelsOnlyEveryInterval()
    {
        var snapshots = Enumerable.Range(0, 30)
            .Select(i => CreateSnapshot(50.0 + i, DateTimeOffset.UtcNow.AddMinutes(-30 + i)))
            .ToList();

        var result = ChartDataProcessing.CreateSeries(
            snapshots,
            "Test",
            "#FF0000",
            s => s.PackVoltageVolts);

        Assert.NotNull(result);
        Assert.Equal(30, result.Points.Count);

        // Interval of 5 for 30 points, plus the last point always labelled.
        var labelled = result.Points.Count(p => !string.IsNullOrEmpty(p.Label));
        Assert.Equal(7, labelled);
        Assert.NotEmpty(result.Points[0].Label);
        Assert.Empty(result.Points[1].Label);
        Assert.NotEmpty(result.Points[^1].Label);
    }

    [Fact]
    public void FormatTimestamp_ConvertsToLocalTime()
    {
        var timestamp = new DateTimeOffset(2026, 8, 17, 12, 34, 56, TimeSpan.Zero);
        var expected = timestamp.ToLocalTime();

        var formatted = ChartDataProcessing.FormatTimestamp(timestamp, 5);

        Assert.Equal(expected.ToString("HH:mm:ss", CultureInfo.InvariantCulture), formatted);
    }

    [Fact]
    public void FormatTimestamp_WithLongSeries_DropsTheSeconds()
    {
        var timestamp = new DateTimeOffset(2026, 8, 17, 12, 34, 56, TimeSpan.Zero);
        var expected = timestamp.ToLocalTime();

        var formatted = ChartDataProcessing.FormatTimestamp(timestamp, 500);

        Assert.Equal(expected.ToString("HH:mm", CultureInfo.InvariantCulture), formatted);
    }

    private static BatterySnapshot CreateSnapshot(
        double? voltage,
        DateTimeOffset timestamp,
        IReadOnlyList<double>? cellVoltages = null,
        double? cellDelta = null)
    {
        return new BatterySnapshot(
            timestamp,
            "test-device",
            50.0,
            null,
            voltage,
            null,
            null,
            null,
            null,
            null,
            null,
            cellVoltages ?? [],
            [],
            cellDelta,
            ChargeState.Idle,
            [],
            DataQuality.Valid,
            false);
    }
}
