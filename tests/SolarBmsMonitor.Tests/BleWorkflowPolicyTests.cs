using SolarBmsMonitor.Core.Models;
using SolarBmsMonitor.Core.Services;

namespace SolarBmsMonitor.Tests;

public sealed class BleWorkflowPolicyTests
{
    [Fact]
    public void IntentionalDisconnectPreservesConnectedStateAndEndsExactlyOnce()
    {
        var connected = BleConnectionStateReducer.Apply(default, BleConnectionState.Connected);
        var disconnecting = BleConnectionStateReducer.Apply(connected.State, BleConnectionState.Disconnecting);
        var disconnected = BleConnectionStateReducer.Apply(disconnecting.State, BleConnectionState.Disconnected);
        var duplicate = BleConnectionStateReducer.Apply(disconnected.State, BleConnectionState.Disconnected);

        Assert.True(connected.State.IsConnected);
        Assert.True(disconnecting.State.IsConnected);
        Assert.False(disconnecting.ConnectionEnded);
        Assert.False(disconnected.State.IsConnected);
        Assert.True(disconnected.ConnectionEnded);
        Assert.False(duplicate.ConnectionEnded);
    }

    [Fact]
    public void InitialCleanupDuringConnectionDoesNotEndAConnectedSession()
    {
        var connecting = BleConnectionStateReducer.Apply(default, BleConnectionState.Connecting);
        var initialCleanup = BleConnectionStateReducer.Apply(connecting.State, BleConnectionState.Disconnected);

        Assert.False(initialCleanup.ConnectionEnded);
    }

    [Fact]
    public void ConnectionErrorEndsAnEstablishedConnectionExactlyOnce()
    {
        var connected = BleConnectionStateReducer.Apply(default, BleConnectionState.Connected);
        var failed = BleConnectionStateReducer.Apply(connected.State, BleConnectionState.Error);
        var cleanup = BleConnectionStateReducer.Apply(failed.State, BleConnectionState.Disconnected);

        Assert.False(failed.State.IsConnected);
        Assert.True(failed.ConnectionEnded);
        Assert.False(cleanup.ConnectionEnded);
    }

    [Theory]
    [InlineData(BleScanOutcome.PermissionDenied)]
    [InlineData(BleScanOutcome.BluetoothDisabled)]
    [InlineData(BleScanOutcome.Unsupported)]
    [InlineData(BleScanOutcome.DeviceFound)]
    [InlineData(BleScanOutcome.RetryLimitReached)]
    [InlineData(BleScanOutcome.Canceled)]
    public void PermanentOrTerminalScanOutcomesAreNotRetried(BleScanOutcome outcome)
    {
        Assert.False(BleScanRetryPolicy.ShouldRetry(outcome, completedAttempts: 1));
    }

    [Theory]
    [InlineData(BleScanOutcome.NoDeviceFound)]
    [InlineData(BleScanOutcome.TransientFailure)]
    public void RetryableScanOutcomesUseBoundedBackoff(BleScanOutcome outcome)
    {
        Assert.True(BleScanRetryPolicy.IsRetryableOutcome(outcome));
        Assert.True(BleScanRetryPolicy.ShouldRetry(outcome, completedAttempts: 1));
        Assert.Equal(TimeSpan.FromSeconds(2), BleScanRetryPolicy.DelayAfterAttempt(1));
        Assert.True(BleScanRetryPolicy.ShouldRetry(outcome, completedAttempts: 2));
        Assert.Equal(TimeSpan.FromSeconds(4), BleScanRetryPolicy.DelayAfterAttempt(2));
        Assert.False(BleScanRetryPolicy.ShouldRetry(outcome, completedAttempts: 3));
    }
}
