using SolarBmsMonitor.Core.Calculations;
using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Tests;

public sealed class BatteryCalculationsTests
{
    [Fact]
    public void CalculatesPowerEnergyAndCellDelta()
    {
        Assert.Equal(-1_280, BatteryCalculations.PowerWatts(25.6, -50), 3);
        Assert.Equal(7.68, BatteryCalculations.NominalEnergyKilowattHours, 3);
        Assert.Equal(12, BatteryCalculations.CellDeltaMillivolts([3.310, 3.322, 3.315]), 3);
    }

    [Fact]
    public void IntegratesIncomingEnergyAndRejectsInvalidGaps()
    {
        var incoming = BatteryCalculations.IncomingEnergyKilowattHours(
            1_000,
            1_000,
            TimeSpan.FromSeconds(30));

        Assert.Equal(0.008333, incoming, 6);
        Assert.Equal(
            0,
            BatteryCalculations.IncomingEnergyKilowattHours(-500, -600, TimeSpan.FromSeconds(5)));
        Assert.Equal(
            0,
            BatteryCalculations.IncomingEnergyKilowattHours(1_000, 1_000, TimeSpan.FromMinutes(2)));
    }

    [Theory]
    [InlineData(1, ChargeState.Charging)]
    [InlineData(-1, ChargeState.Discharging)]
    [InlineData(0.2, ChargeState.Idle)]
    [InlineData(-0.2, ChargeState.Idle)]
    public void DetectsChargeStateWithDeadband(double currentAmps, ChargeState expected)
    {
        Assert.Equal(expected, BatteryCalculations.DetermineChargeState(currentAmps));
    }

    [Fact]
    public void PrefersReportedRemainingCapacity()
    {
        var estimate = BatteryCalculations.EstimateEnergy(
            150,
            300,
            90,
            25.6,
            [-1_000, -1_010, -990, -1_005, -995]);

        Assert.True(estimate.UsedReportedCapacity);
        Assert.Equal(3.84, estimate.RemainingKilowattHours, 3);
        Assert.NotNull(estimate.RuntimeHours);
        Assert.Null(estimate.ChargeTimeHours);
    }

    [Fact]
    public void FallsBackToSoc()
    {
        var estimate = BatteryCalculations.EstimateEnergy(
            null,
            null,
            50,
            25.6,
            [500, 505, 495, 500, 500]);

        Assert.False(estimate.UsedReportedCapacity);
        Assert.Equal(3.84, estimate.RemainingKilowattHours, 3);
        Assert.Equal(7.68, estimate.ChargeTimeHours!.Value, 3);
    }

    [Fact]
    public void DoesNotEstimateRuntimeWithInsufficientOrUnstableSamples()
    {
        var insufficient = BatteryCalculations.EstimateEnergy(150, 300, 50, 25.6, [-1_000, -1_000]);
        var unstable = BatteryCalculations.EstimateEnergy(150, 300, 50, 25.6, [-100, -2_000, -50, -1_500, -70]);

        Assert.Equal("calculando", insufficient.Confidence);
        Assert.Null(insufficient.RuntimeHours);
        Assert.Equal("inestable", unstable.Confidence);
        Assert.Null(unstable.RuntimeHours);
    }
}
