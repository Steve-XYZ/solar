using SolarBmsMonitor.Core.Calculations;
using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Tests;

public sealed class BatteryHealthTests
{
    [Fact]
    public void UsesStateOfHealthWhenPlausible()
    {
        var summary = BatteryHealth.Summarize(98, 279, 300, 5);

        Assert.Equal(BatteryHealthMode.StateOfHealth, summary.Mode);
        Assert.Equal(98, summary.StateOfHealthPercent);
        Assert.Equal(279, summary.RemainingCapacityAh);
        Assert.Equal(300, summary.ReferenceCapacityAh);
        Assert.Equal(5, summary.CycleCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(101d)]
    [InlineData(double.NaN)]
    public void FallsBackToCapacityWhenStateOfHealthIsNotUsable(double? stateOfHealthPercent)
    {
        var summary = BatteryHealth.Summarize(stateOfHealthPercent, 279, 300, 5);

        Assert.Equal(BatteryHealthMode.Capacity, summary.Mode);
        Assert.Null(summary.StateOfHealthPercent);
        Assert.Equal(279, summary.RemainingCapacityAh);
    }

    [Fact]
    public void DropsImplausibleCapacityAndCycles()
    {
        var summary = BatteryHealth.Summarize(100, double.NaN, 0, -1);

        Assert.Equal(BatteryHealthMode.StateOfHealth, summary.Mode);
        Assert.Null(summary.RemainingCapacityAh);
        Assert.Null(summary.ReferenceCapacityAh);
        Assert.Null(summary.CycleCount);
    }
}
