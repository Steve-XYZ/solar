using Microsoft.Extensions.Logging;
using SolarBmsMonitor.App.Platforms.Android;
using SolarBmsMonitor.App.Services;
using SolarBmsMonitor.App.ViewModels;
using SolarBmsMonitor.Core.Services;

namespace SolarBmsMonitor.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("FontAwesomeSolid.ttf", "FontAwesomeSolid");
                fonts.AddFont("FontAwesomeBrands.ttf", "FontAwesomeBrands");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IBleMonitorService, AndroidBleMonitorService>();
        builder.Services.AddSingleton<IBatteryRepository, SqliteBatteryRepository>();
        builder.Services.AddSingleton<IExportService, LocalExportService>();
        builder.Services.AddSingleton<MonitorViewModel>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<DevicesPage>();
        builder.Services.AddSingleton<DashboardPage>();
        builder.Services.AddSingleton<CellsPage>();
        builder.Services.AddSingleton<HistoryPage>();
        builder.Services.AddSingleton<DiagnosticsPage>();
        builder.Services.AddSingleton<InfoPage>();

        return builder.Build();
    }
}
