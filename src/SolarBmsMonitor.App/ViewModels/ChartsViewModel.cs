using System.Collections.ObjectModel;
using System.Windows.Input;
using Microcharts;
using SolarBmsMonitor.App.Services;
using SolarBmsMonitor.Core.Calculations;
using SkiaSharp;

namespace SolarBmsMonitor.App.ViewModels;

public sealed class ChartsViewModel : ObservableObject
{
    private readonly IChartDataService _chartDataService;
    private readonly MonitorViewModel _monitorViewModel;
    private ChartDataBundle? _chartData;
    private TimeRange _selectedTimeRange = TimeRange.LastDay;
    private bool _isLoading;
    private string _statusMessage = "Cargando datos...";

    public ChartsViewModel(
        IChartDataService chartDataService,
        MonitorViewModel monitorViewModel)
    {
        _chartDataService = chartDataService;
        _monitorViewModel = monitorViewModel;

        LoadDataCommand = new AsyncCommand(_ => LoadDataAsync());
        ChangeTimeRangeCommand = new AsyncCommand(param => ChangeTimeRangeAsync((TimeRange)param!));
    }

    public ICommand LoadDataCommand { get; }
    public ICommand ChangeTimeRangeCommand { get; }

    public ChartDataBundle? ChartData
    {
        get => _chartData;
        private set => SetProperty(ref _chartData, value);
    }

    public TimeRange SelectedTimeRange
    {
        get => _selectedTimeRange;
        set => SetProperty(ref _selectedTimeRange, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public LineChart? VoltageChart => CreateLineChart(ChartData?.VoltageSeries);
    public LineChart? TemperatureChart => CreateLineChart(ChartData?.TemperatureSeries);
    public LineChart? PowerChart => CreateLineChart(ChartData?.PowerSeries);
    public LineChart? SocChart => CreateLineChart(ChartData?.SocSeries);

    public async Task LoadDataAsync()
    {
        if (_monitorViewModel.SelectedDevice?.DeviceId is not { } deviceId)
        {
            StatusMessage = "No hay dispositivo conectado";
            return;
        }

        IsLoading = true;
        StatusMessage = "Cargando datos...";

        try
        {
            var data = await _chartDataService.GenerateChartDataAsync(deviceId, SelectedTimeRange);
            ChartData = data;

            OnPropertyChanged(nameof(VoltageChart));
            OnPropertyChanged(nameof(TemperatureChart));
            OnPropertyChanged(nameof(PowerChart));
            OnPropertyChanged(nameof(SocChart));
            OnPropertyChanged(nameof(CellBalanceData));

            if (data.VoltageSeries is null && data.TemperatureSeries is null &&
                data.PowerSeries is null && data.SocSeries is null)
            {
                StatusMessage = "No hay datos disponibles para este período";
            }
            else if (data.IsTruncated)
            {
                StatusMessage = $"{FormatTimeRange(SelectedTimeRange)}: solo las muestras más recientes";
            }
            else
            {
                StatusMessage = $"Datos actualizados: {FormatTimeRange(SelectedTimeRange)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar datos: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ChangeTimeRangeAsync(TimeRange range)
    {
        if (SelectedTimeRange == range) return;

        SelectedTimeRange = range;
        await LoadDataAsync();
    }

    public IReadOnlyList<CellBalanceData> CellBalanceData => ChartData?.CellBalance ?? [];

    private static LineChart? CreateLineChart(ChartSeries? series)
    {
        if (series is null) return null;

        // A point that carries no axis label carries no value label either:
        // Microcharts draws one string per entry, so labelling all of them
        // turns a long series into an unreadable band of overlapping text.
        var entries = series.Points
            .Select(p => new Microcharts.ChartEntry((float)p.Value)
            {
                Label = p.Label,
                ValueLabel = string.IsNullOrEmpty(p.Label)
                    ? string.Empty
                    : p.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                Color = SKColor.Parse(series.Color)
            })
            .ToList();

        return new LineChart
        {
            Entries = entries,
            LineMode = LineMode.Spline,
            LineSize = 3,
            PointMode = PointMode.Circle,
            PointSize = 4,
            LabelTextSize = 12,
            Margin = 20,
            BackgroundColor = SKColors.Transparent,
            ValueLabelOrientation = Orientation.Horizontal,
            ShowYAxisText = true
        };
    }

    private static string FormatTimeRange(TimeRange range) => range switch
    {
        TimeRange.LastHour => "Última hora",
        TimeRange.LastDay => "Último día",
        TimeRange.LastWeek => "Última semana",
        TimeRange.All => "Todo el historial",
        _ => "Desconocido"
    };
}
