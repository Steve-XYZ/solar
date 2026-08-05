using SolarBmsMonitor.App.ViewModels;

namespace SolarBmsMonitor.App;

public partial class DashboardPage : ContentPage
{
    public DashboardPage(MonitorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
