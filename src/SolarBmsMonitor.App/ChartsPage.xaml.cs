using SolarBmsMonitor.App.ViewModels;

namespace SolarBmsMonitor.App;

public partial class ChartsPage : ContentPage
{
    private readonly ChartsViewModel _viewModel;

    public ChartsPage(ChartsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.LoadDataCommand.CanExecute(null))
        {
            _viewModel.LoadDataCommand.Execute(null);
        }
    }
}
