using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Core.Calculations;

public static class BatteryCalculations
{
    private static readonly TimeSpan MaximumEnergySampleInterval = TimeSpan.FromMinutes(1);

    public const double NominalVoltageVolts = 25.6;
    public const double NominalCapacityAmpHours = 300;
    public const double NominalEnergyKilowattHours = 7.68;
    public const double DefaultIdleDeadbandAmps = 0.25;

    /// <summary>
    /// Above this dispersion the estimate is still published, but as an
    /// approximation: the load has moved enough during the sampling window that
    /// the remaining minutes should not be read as a precise figure.
    /// </summary>
    public const double StablePrecisionCoefficientOfVariation = 0.15;

    public static double PowerWatts(double packVoltageVolts, double currentAmps) =>
        packVoltageVolts * currentAmps;

    public static double CellDeltaMillivolts(IReadOnlyList<double> cellVoltages)
    {
        ArgumentNullException.ThrowIfNull(cellVoltages);
        return cellVoltages.Count == 0
            ? 0
            : (cellVoltages.Max() - cellVoltages.Min()) * 1_000;
    }

    public static double IncomingEnergyKilowattHours(
        double previousPowerWatts,
        double currentPowerWatts,
        TimeSpan elapsed)
    {
        if (!double.IsFinite(previousPowerWatts) ||
            !double.IsFinite(currentPowerWatts) ||
            elapsed <= TimeSpan.Zero ||
            elapsed > MaximumEnergySampleInterval)
        {
            return 0;
        }

        var averageIncomingPowerWatts =
            (Math.Max(0, previousPowerWatts) + Math.Max(0, currentPowerWatts)) / 2;
        return averageIncomingPowerWatts * elapsed.TotalHours / 1_000;
    }

    public static ChargeState DetermineChargeState(
        double currentAmps,
        double deadbandAmps = DefaultIdleDeadbandAmps)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deadbandAmps);

        if (currentAmps > deadbandAmps)
        {
            return ChargeState.Charging;
        }

        return currentAmps < -deadbandAmps
            ? ChargeState.Discharging
            : ChargeState.Idle;
    }

    public static EnergyEstimate EstimateEnergy(
        double? remainingCapacityAh,
        double? fullCapacityAh,
        double? stateOfChargePercent,
        double packVoltageVolts,
        IReadOnlyList<double> recentPowerWatts,
        double minimumStablePowerWatts = 50,
        double maximumCoefficientOfVariation = 0.35)
    {
        var usedReportedCapacity = remainingCapacityAh is >= 0;
        var remainingKwh = usedReportedCapacity
            ? packVoltageVolts * remainingCapacityAh!.Value / 1_000
            : NominalEnergyKilowattHours * Math.Clamp(stateOfChargePercent ?? 0, 0, 100) / 100;

        var fullKwh = fullCapacityAh is > 0
            ? packVoltageVolts * fullCapacityAh.Value / 1_000
            : NominalEnergyKilowattHours;

        var samples = recentPowerWatts.Where(double.IsFinite).ToArray();
        if (samples.Length < 5)
        {
            return new EnergyEstimate(
                remainingKwh,
                usedReportedCapacity,
                null,
                null,
                "calculando",
                EstimatePrecision.Unavailable);
        }

        var mean = samples.Average();
        var meanMagnitude = samples.Select(Math.Abs).Average();
        var variance = samples.Sum(value => Math.Pow(Math.Abs(value) - meanMagnitude, 2)) / samples.Length;
        var coefficientOfVariation = meanMagnitude == 0 ? double.PositiveInfinity : Math.Sqrt(variance) / meanMagnitude;

        if (meanMagnitude < minimumStablePowerWatts || coefficientOfVariation > maximumCoefficientOfVariation)
        {
            return new EnergyEstimate(
                remainingKwh,
                usedReportedCapacity,
                null,
                null,
                "inestable",
                EstimatePrecision.Unavailable);
        }

        double? runtime = mean < -minimumStablePowerWatts ? remainingKwh * 1_000 / -mean : null;
        var missingKwh = Math.Max(0, fullKwh - remainingKwh);
        double? chargeTime = mean > minimumStablePowerWatts ? missingKwh * 1_000 / mean : null;
        var precision = coefficientOfVariation <= StablePrecisionCoefficientOfVariation
            ? EstimatePrecision.Stable
            : EstimatePrecision.Approximate;
        return new EnergyEstimate(
            remainingKwh,
            usedReportedCapacity,
            runtime,
            chargeTime,
            "estimada",
            precision);
    }
}
