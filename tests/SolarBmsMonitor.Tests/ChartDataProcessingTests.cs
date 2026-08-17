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
    public void FormatTimestamp_ConvertsToLocalTime()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var formatted = ChartDataProcessing.FormatTimestamp(timestamp, 5);

        Assert.NotNull(formatted);
        Assert.Contains(":", formatted);
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