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
