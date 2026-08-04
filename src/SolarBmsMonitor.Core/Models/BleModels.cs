namespace SolarBmsMonitor.Core.Models;

[Flags]
public enum GattCharacteristicCapabilities
{
    None = 0,
    Read = 1,
    Write = 2,
    WriteWithoutResponse = 4,
    Notify = 8,
    Indicate = 16,
}

public enum BleAvailability
{
    Unsupported,
    Disabled,
    PermissionRequired,
    Ready,
}

public enum BleConnectionState
{
    Available,
    Connecting,
    Connected,
    Disconnecting,
    Disconnected,
    Error,
}

public sealed record BleDevice(
    string Name,
    string DeviceId,
    int Rssi,
    DateTimeOffset LastSeenAt,
    BleConnectionState ConnectionState = BleConnectionState.Available);

public sealed record GattDescriptorInfo(string Uuid);

public sealed record GattCharacteristicInfo(
    string Uuid,
    GattCharacteristicCapabilities Capabilities,
    IReadOnlyList<GattDescriptorInfo> Descriptors,
    bool IsSelectedWriteChannel,
    bool IsSelectedNotificationChannel);

public sealed record GattServiceInfo(
    string Uuid,
    IReadOnlyList<GattCharacteristicInfo> Characteristics);

public sealed record GattProfile(
    DateTimeOffset DiscoveredAt,
    int? Mtu,
    IReadOnlyList<GattServiceInfo> Services,
    string? WriteCharacteristicUuid,
    string? NotificationCharacteristicUuid,
    string? CccdUuid,
    bool NotificationsEnabled);

public enum DiagnosticDirection
{
    Event,
    Tx,
    RxFragment,
    RxFrame,
    Error,
}

public sealed record DiagnosticEntry(
    DateTimeOffset Timestamp,
    DiagnosticDirection Direction,
    string Message,
    string? Hex = null);

public sealed record DiagnosticReport(
    string AppVersion,
    BleDevice? Device,
    GattProfile? Profile,
    IReadOnlyList<DiagnosticEntry> Entries,
    int ConnectionAttempts,
    int Timeouts,
    int Reconnects,
    DateTimeOffset ExportedAt);
