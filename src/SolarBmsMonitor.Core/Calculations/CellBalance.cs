using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Core.Calculations;

/// <summary>
/// Classifies the spread between the highest and lowest cell of the pack.
/// The thresholds are a conservative LiFePO4 convention, not a MUST
/// specification, and a single sample never diagnoses a defective cell: the
/// spread widens under load and narrows again at rest.
/// </summary>
public static class CellBalance
{
    public const double BalancedThresholdMillivolts = 20;
    public const double AcceptableThresholdMillivolts = 50;

    /// <summary>
    /// Smallest spread that still names a highest and a lowest cell. A pack
    /// whose cells read identically has no meaningful extreme, and badging
    /// every card would be noise; anything above this is a real difference the
    /// reader can act on. This is deliberately far below
    /// <see cref="BalancedThresholdMillivolts"/>: pointing at the ends of the
    /// spread is not the same claim as calling the pack unbalanced.
    /// </summary>
    public const double MeaningfulSpreadVolts = 0.0005;

    /// <summary>
    /// True when the pack has a spread wide enough to name its extremes.
    /// </summary>
    public static bool HasMeaningfulSpread(IReadOnlyList<double> cellVoltages) =>
        cellVoltages is { Count: > 0 }
        && cellVoltages.Max() - cellVoltages.Min() > MeaningfulSpreadVolts;

    public static CellBalanceLevel Evaluate(double? deltaMillivolts)
    {
        if (deltaMillivolts is not { } delta || !double.IsFinite(delta) || delta < 0)
        {
            return CellBalanceLevel.Unknown;
        }

        if (delta <= BalancedThresholdMillivolts)
        {
            return CellBalanceLevel.Balanced;
        }

        return delta <= AcceptableThresholdMillivolts
            ? CellBalanceLevel.Acceptable
            : CellBalanceLevel.Review;
    }
}
