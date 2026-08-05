using SolarBmsMonitor.App.ViewModels;

namespace SolarBmsMonitor.App;

public partial class HistoryPage : ContentPage
{
    public HistoryPage(MonitorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
