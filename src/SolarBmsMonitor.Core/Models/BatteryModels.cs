namespace SolarBmsMonitor.Core.Models;

public enum ChargeState
{
    Unknown,
    Charging,
    Discharging,
    Idle,
}

public enum DataQuality
{
    Unknown,
    Partial,
    Valid,
    Invalid,
}

public enum EstimatePrecision
{
    Unavailable,
    Approximate,
    Stable,
}

public enum CellBalanceLevel
{
    Unknown,
    Balanced,
    Acceptable,
    Review,
}

public enum BatteryHealthMode
{
    StateOfHealth,
    Capacity,
}

public sealed record BatteryAlarm(string Code, string Description, bool IsActive);

public sealed record BatterySnapshot(
    DateTimeOffset Timestamp,
    string DeviceId,
    double? StateOfChargePercent,
    double? StateOfHealthPercent,
    double? PackVoltageVolts,
    double? CurrentAmps,
    double? PowerWatts,
    double? RemainingCapacityAh,
    double? FullCapacityAh,
    double? DesignedCapacityAh,
    int? CycleCount,
    IReadOnlyList<double> CellVoltages,
    IReadOnlyList<double> TemperaturesCelsius,
    double? CellDeltaMillivolts,
    ChargeState ChargeState,
    IReadOnlyList<BatteryAlarm> Alarms,
    DataQuality DataQuality,
    bool IsStale);

public sealed record DeviceInfo(
    string BleName,
    string DeviceId,
    int Rssi,
    string? FirmwareVersion,
    string? HardwareVersion,
    int? CellCount,
    double? NominalCapacityAh,
    DateTimeOffset FirstConnectedAt,
    DateTimeOffset LastConnectedAt);

public sealed record EnergyEstimate(
    double RemainingKilowattHours,
    bool UsedReportedCapacity,
    double? RuntimeHours,
    double? ChargeTimeHours,
    string Confidence,
    EstimatePrecision Precision);

public sealed record BatteryHealthSummary(
    BatteryHealthMode Mode,
    double? StateOfHealthPercent,
    double? RemainingCapacityAh,
    double? ReferenceCapacityAh,
    int? CycleCount);
