using SolarBmsMonitor.App.ViewModels;

namespace SolarBmsMonitor.App;

public partial class DashboardPage : ContentPage
{
    private readonly MonitorViewModel _viewModel;

    public DashboardPage(MonitorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _viewModel.ConnectionEnded += OnConnectionEnded;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageRoot.Opacity = 0;
        PageRoot.TranslationY = 12;
        _ = AnimateInAsync();
    }

    protected override void OnDisappearing()
    {
        CloseOverflowMenu();
        base.OnDisappearing();
    }

    private async Task AnimateInAsync()
    {
        await Task.WhenAll(
            PageRoot.FadeToAsync(1, 320, Easing.CubicOut),
            PageRoot.TranslateToAsync(0, 0, 360, Easing.CubicOut));
    }

    private void OnConnectionEnded(object? sender, EventArgs eventArgs) =>
        Dispatcher.Dispatch(async () =>
        {
            CloseOverflowMenu();
            await PageRoot.FadeToAsync(0, 180, Easing.CubicIn);
            if (Shell.Current is AppShell shell)
            {
                await shell.ShowConnectionAsync();
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

    private async void OnChartsClicked(object? sender, EventArgs eventArgs) =>
        await NavigateFromMenuAsync(shell => shell.ShowChartsAsync());

    private async void OnDiagnosticsClicked(object? sender, EventArgs eventArgs) =>
        await NavigateFromMenuAsync(shell => shell.ShowDiagnosticsAsync());

    private async void OnInfoClicked(object? sender, EventArgs eventArgs) =>
        await NavigateFromMenuAsync(shell => shell.ShowInfoAsync());

    private void OnDisconnectClicked(object? sender, EventArgs eventArgs)
    {
        CloseOverflowMenu();
        if (_viewModel.DisconnectCommand.CanExecute(null))
        {
            _viewModel.DisconnectCommand.Execute(null);
        }
    }

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
}
