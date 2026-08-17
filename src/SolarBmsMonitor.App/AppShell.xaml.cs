namespace SolarBmsMonitor.App;

public partial class AppShell : Shell
{
    private readonly TabBar _tabs;
    private readonly Tab _devicesTab;
    private readonly Tab _dashboardTab;
    private readonly CellsPage _cellsPage;
    private readonly HistoryPage _historyPage;
    private readonly ChartsPage _chartsPage;
    private readonly DiagnosticsPage _diagnosticsPage;
    private readonly InfoPage _infoPage;

    public AppShell(
        DevicesPage devicesPage,
        DashboardPage dashboardPage,
        CellsPage cellsPage,
        HistoryPage historyPage,
        ChartsPage chartsPage,
        DiagnosticsPage diagnosticsPage,
        InfoPage infoPage)
    {
        InitializeComponent();

        _cellsPage = cellsPage;
        _historyPage = historyPage;
        _chartsPage = chartsPage;
        _diagnosticsPage = diagnosticsPage;
        _infoPage = infoPage;

        _tabs = new TabBar();
        _devicesTab = CreateTab("Conexión", devicesPage);
        _dashboardTab = CreateTab("Resumen", dashboardPage);
        _tabs.Items.Add(_devicesTab);
        _tabs.Items.Add(_dashboardTab);
        Items.Add(_tabs);
    }

    public async Task ShowDashboardAsync()
    {
        await Current.Navigation.PopToRootAsync(false);
        _tabs.CurrentItem = _dashboardTab;
    }

    public async Task ShowConnectionAsync()
    {
        await Current.Navigation.PopToRootAsync(false);
        _tabs.CurrentItem = _devicesTab;
    }

    public Task ShowCellsAsync() => OpenSecondaryPageAsync(_cellsPage);

    public Task ShowHistoryAsync() => OpenSecondaryPageAsync(_historyPage);

    public Task ShowChartsAsync() => OpenSecondaryPageAsync(_chartsPage);

    public Task ShowDiagnosticsAsync() => OpenSecondaryPageAsync(_diagnosticsPage);

    public Task ShowInfoAsync() => OpenSecondaryPageAsync(_infoPage);

    private static async Task OpenSecondaryPageAsync(Page page)
    {
        Shell.SetNavBarIsVisible(page, true);
        Shell.SetTabBarIsVisible(page, false);
        await Current.Navigation.PushAsync(page);
    }

    private static Tab CreateTab(string title, Page page)
    {
        Shell.SetTabBarIsVisible(page, false);
        Shell.SetNavBarIsVisible(page, false);

        var tab = new Tab { Title = title };
        tab.Items.Add(new ShellContent { Title = title, Content = page });
        return tab;
    }
}
