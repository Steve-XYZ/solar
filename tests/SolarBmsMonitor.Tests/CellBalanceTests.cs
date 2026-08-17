using SolarBmsMonitor.Core.Calculations;
using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Tests;

public sealed class CellBalanceTests
{
    [Theory]
    [InlineData(0, CellBalanceLevel.Balanced)]
    [InlineData(20, CellBalanceLevel.Balanced)]
    [InlineData(21, CellBalanceLevel.Acceptable)]
    [InlineData(50, CellBalanceLevel.Acceptable)]
    [InlineData(51, CellBalanceLevel.Review)]
    public void ClassifiesSpreadAtThresholds(double deltaMillivolts, CellBalanceLevel expected)
    {
        Assert.Equal(expected, CellBalance.Evaluate(deltaMillivolts));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void ReportsUnknownWithoutUsableCellData(double? deltaMillivolts)
    {
        Assert.Equal(CellBalanceLevel.Unknown, CellBalance.Evaluate(deltaMillivolts));
    }
}
