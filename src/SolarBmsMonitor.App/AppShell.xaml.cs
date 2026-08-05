namespace SolarBmsMonitor.App;

public partial class AppShell : Shell
{
    public AppShell(
        DevicesPage devicesPage,
        DashboardPage dashboardPage,
        CellsPage cellsPage,
        HistoryPage historyPage,
        DiagnosticsPage diagnosticsPage,
        InfoPage infoPage)
    {
        InitializeComponent();

        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Dispositivos", devicesPage));
        tabs.Items.Add(CreateTab("Resumen", dashboardPage));
        tabs.Items.Add(CreateTab("Celdas", cellsPage));
        tabs.Items.Add(CreateTab("Histórico", historyPage));
        tabs.Items.Add(CreateTab("Diagnóstico", diagnosticsPage));
        tabs.Items.Add(CreateTab("Información", infoPage));
        Items.Add(tabs);
    }

    private static Tab CreateTab(string title, Page page)
    {
        var tab = new Tab { Title = title };
        tab.Items.Add(new ShellContent { Title = title, Content = page });
        return tab;
    }
}
