using Microsoft.Extensions.DependencyInjection;
using SolarBmsMonitor.App.ViewModels;
using SolarBmsMonitor.Core.Services;

namespace SolarBmsMonitor.App;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly IBleMonitorService _bleService;
    private readonly MonitorViewModel _viewModel;

    public App(IServiceProvider services)
    {
        InitializeComponent();

        // Colors.xaml is light-only: there is no dark counterpart for Navy text
        // or the white card surfaces. Following the system theme lets platform
        // dark defaults paint the window and nav bar behind that palette, so the
        // theme stays pinned until a real dark palette exists.
        UserAppTheme = AppTheme.Light;

        // Pages use resources declared in App.xaml. Resolve the visual tree only
        // after InitializeComponent has loaded those dictionaries.
        _shell = services.GetRequiredService<AppShell>();
        _bleService = services.GetRequiredService<IBleMonitorService>();
        _viewModel = services.GetRequiredService<MonitorViewModel>();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell);
        window.Stopped += OnWindowStopped;
        return window;
    }

    private async void OnWindowStopped(object? sender, EventArgs eventArgs)
    {
        try
        {
            _viewModel.CancelActiveOperation();
            await _bleService.DisconnectAsync(CancellationToken.None);
        }
        catch
        {
            // App shutdown must continue; the BLE service still closes GATT in its finally path.
        }
    }
}
