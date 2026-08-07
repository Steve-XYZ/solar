using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Core.Services;

public readonly record struct BleConnectionPresentationState(bool IsConnecting, bool IsConnected);

public readonly record struct BleConnectionPresentationTransition(
    BleConnectionPresentationState State,
    bool ConnectionEnded);

public static class BleConnectionStateReducer
{
    public static BleConnectionPresentationTransition Apply(
        BleConnectionPresentationState current,
        BleConnectionState next)
    {
        var state = new BleConnectionPresentationState(
            next == BleConnectionState.Connecting,
            next switch
            {
                BleConnectionState.Connected => true,
                BleConnectionState.Disconnecting => current.IsConnected,
                _ => false,
            });
        return new BleConnectionPresentationTransition(
            state,
            current.IsConnected && next is BleConnectionState.Disconnected or BleConnectionState.Error);
    }
}

public enum BleScanOutcome
{
    DeviceFound,
    NoDeviceFound,
    PermissionDenied,
    BluetoothDisabled,
    Unsupported,
    TransientFailure,
    RetryLimitReached,
    Canceled,
}

public static class BleScanRetryPolicy
{
    public const int MaximumAutomaticAttempts = 3;

    public static bool IsRetryableOutcome(BleScanOutcome outcome) =>
        outcome is BleScanOutcome.NoDeviceFound or BleScanOutcome.TransientFailure;

    public static bool ShouldRetry(BleScanOutcome outcome, int completedAttempts) =>
        completedAttempts < MaximumAutomaticAttempts &&
        IsRetryableOutcome(outcome);

    public static TimeSpan DelayAfterAttempt(int completedAttempts) =>
        TimeSpan.FromSeconds(Math.Pow(2, completedAttempts));
}
