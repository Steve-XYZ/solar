using SolarBmsMonitor.App.ViewModels;

namespace SolarBmsMonitor.App;

public partial class CellsPage : ContentPage
{
    public CellsPage(MonitorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
