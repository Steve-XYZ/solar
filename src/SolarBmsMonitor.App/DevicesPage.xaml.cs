using SolarBmsMonitor.App.ViewModels;
using SolarBmsMonitor.Core.Services;

namespace SolarBmsMonitor.App;

public partial class DevicesPage : ContentPage, IDisposable
{
    private readonly MonitorViewModel _viewModel;
    private CancellationTokenSource? _pageCancellation;

    public DevicesPage(MonitorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _viewModel.ConnectionSucceeded += OnConnectionSucceeded;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageRoot.Opacity = 1;
        PageRoot.Scale = 1;

        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = new CancellationTokenSource();

        _ = RunRadarAsync(_pageCancellation.Token);
        if (!_viewModel.IsConnected && !_viewModel.IsConnecting)
        {
            _ = ScanUntilFoundAsync(_pageCancellation.Token);
        }
    }

    protected override void OnDisappearing()
    {
        CloseOverflowMenu();
        _pageCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async Task ScanUntilFoundAsync(CancellationToken cancellationToken)
    {
        try
        {
            for (var completedAttempts = 1;
                 completedAttempts <= BleScanRetryPolicy.MaximumAutomaticAttempts &&
                 !cancellationToken.IsCancellationRequested &&
                 !_viewModel.IsConnected &&
                 !_viewModel.IsConnecting;
                 completedAttempts++)
            {
                var outcome = await _viewModel.ScanAsync(cancellationToken);
                if (!BleScanRetryPolicy.ShouldRetry(outcome, completedAttempts))
                {
                    if (completedAttempts == BleScanRetryPolicy.MaximumAutomaticAttempts &&
                        BleScanRetryPolicy.IsRetryableOutcome(outcome))
                    {
                        _viewModel.MarkAutomaticScanLimitReached();
                    }

                    return;
                }

                await Task.Delay(BleScanRetryPolicy.DelayAfterAttempt(completedAttempts), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leaving the connection screen cancels both the loop and its active BLE scan.
        }
    }

    private async Task RunRadarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(
                AnimateWaveAsync(RadarWave1, 0.36, 0, cancellationToken),
                AnimateWaveAsync(RadarWave2, 0.25, 350, cancellationToken),
                AnimateWaveAsync(RadarWave3, 0.19, 700, cancellationToken),
                AnimateWaveAsync(RadarWave4, 0.14, 1_050, cancellationToken),
                AnimateDetectorAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Page navigation cancels the visual loop.
        }
    }

    private static async Task AnimateWaveAsync(
        VisualElement wave,
        double peakOpacity,
        int initialDelayMilliseconds,
        CancellationToken cancellationToken)
    {
        await Task.Delay(initialDelayMilliseconds, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            wave.Scale = 0.94;
            wave.Opacity = peakOpacity;
            await Task.WhenAll(
                wave.ScaleToAsync(1.06, 1_750, Easing.CubicOut),
                wave.FadeToAsync(peakOpacity * 0.28, 1_750, Easing.CubicOut));
            await Task.Delay(180, cancellationToken);
        }
    }

    private async Task AnimateDetectorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            DetectorGlow.Scale = 0.7;
            DetectorGlow.Opacity = 0.45;
            await Task.WhenAll(
                DetectorGlow.ScaleToAsync(1.25, 1_050, Easing.CubicOut),
                DetectorGlow.FadeToAsync(0.08, 1_050, Easing.CubicOut),
                DetectorPoint.ScaleToAsync(1.15, 520, Easing.CubicInOut));
            await DetectorPoint.ScaleToAsync(1, 420, Easing.CubicInOut);
            await Task.Delay(150, cancellationToken);
        }
    }

    private void OnConnectionSucceeded(object? sender, EventArgs eventArgs) =>
        Dispatcher.Dispatch(async () =>
        {
            _pageCancellation?.Cancel();
            CloseOverflowMenu();
            await Task.WhenAll(
                PageRoot.FadeToAsync(0, 220, Easing.CubicIn),
                PageRoot.ScaleToAsync(0.985, 220, Easing.CubicIn));

            if (Shell.Current is AppShell shell)
            {
                await shell.ShowDashboardAsync();
            }
        });

    private void OnOverflowClicked(object? sender, EventArgs eventArgs)
    {
        var show = !OverflowMenu.IsVisible;
        OverflowMenu.IsVisible = show;
        MenuScrim.IsVisible = show;
    }

    private void OnMenuScrimTapped(object? sender, TappedEventArgs eventArgs) => CloseOverflowMenu();

    private async void OnCellsClicked(object? sender, EventArgs eventArgs) =>
        await NavigateFromMenuAsync(shell => shell.ShowCellsAsync());

    private async void OnHistoryClicked(object? sender, EventArgs eventArgs) =>
        await NavigateFromMenuAsync(shell => shell.ShowHistoryAsync());

    private async void OnDiagnosticsClicked(object? sender, EventArgs eventArgs) =>
        await NavigateFromMenuAsync(shell => shell.ShowDiagnosticsAsync());

    private async void OnInfoClicked(object? sender, EventArgs eventArgs) =>
        await NavigateFromMenuAsync(shell => shell.ShowInfoAsync());

    private async Task NavigateFromMenuAsync(Func<AppShell, Task> navigation)
    {
        CloseOverflowMenu();
        if (Shell.Current is AppShell shell)
        {
            await navigation(shell);
        }
    }

    private void CloseOverflowMenu()
    {
        OverflowMenu.IsVisible = false;
        MenuScrim.IsVisible = false;
    }

    public void Dispose()
    {
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _viewModel.ConnectionSucceeded -= OnConnectionSucceeded;
        GC.SuppressFinalize(this);
    }
}
