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
