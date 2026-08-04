using SolarBmsMonitor.App.ViewModels;

namespace SolarBmsMonitor.App;

public partial class DiagnosticsPage : ContentPage
{
    public DiagnosticsPage(MonitorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
