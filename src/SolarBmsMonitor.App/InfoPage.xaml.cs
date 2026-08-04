using SolarBmsMonitor.App.ViewModels;

namespace SolarBmsMonitor.App;

public partial class InfoPage : ContentPage
{
    public InfoPage(MonitorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
