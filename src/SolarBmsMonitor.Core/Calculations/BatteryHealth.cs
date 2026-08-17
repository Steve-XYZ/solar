using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Core.Calculations;

/// <summary>
/// Decides what the health card can honestly claim. The PaceEX state of health
/// is a single byte that has not been confirmed against this battery, so when
/// it is missing or out of range the summary falls back to the capacity pair,
/// which is always reported.
/// </summary>
public static class BatteryHealth
{
    public static BatteryHealthSummary Summarize(
        double? stateOfHealthPercent,
        double? remainingCapacityAh,
        double? referenceCapacityAh,
        int? cycleCount)
    {
        var isPlausible = stateOfHealthPercent is { } soh &&
            double.IsFinite(soh) &&
            soh is > 0 and <= 100;

        return new BatteryHealthSummary(
            isPlausible ? BatteryHealthMode.StateOfHealth : BatteryHealthMode.Capacity,
            isPlausible ? stateOfHealthPercent : null,
            remainingCapacityAh is { } remaining && double.IsFinite(remaining) && remaining >= 0
                ? remaining
                : null,
            referenceCapacityAh is { } reference && double.IsFinite(reference) && reference > 0
                ? reference
                : null,
            cycleCount is >= 0 ? cycleCount : null);
    }
}
