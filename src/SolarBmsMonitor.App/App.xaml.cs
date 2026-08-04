using SolarBmsMonitor.App.ViewModels;
using SolarBmsMonitor.Core.Services;

namespace SolarBmsMonitor.App;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly IBleMonitorService _bleService;
    private readonly MonitorViewModel _viewModel;

    public App(AppShell shell, IBleMonitorService bleService, MonitorViewModel viewModel)
    {
        InitializeComponent();
        _shell = shell;
        _bleService = bleService;
        _viewModel = viewModel;
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
