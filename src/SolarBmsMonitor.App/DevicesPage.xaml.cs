using SolarBmsMonitor.App.ViewModels;

namespace SolarBmsMonitor.App;

public partial class DevicesPage : ContentPage
{
    public DevicesPage(MonitorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
