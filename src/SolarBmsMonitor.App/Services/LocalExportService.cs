using System.Globalization;
using System.Text;
using System.Text.Json;
using SolarBmsMonitor.Core.Models;
using SolarBmsMonitor.Core.Services;

namespace SolarBmsMonitor.App.Services;

public sealed class LocalExportService : IExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<string> ExportDiagnosticsJsonAsync(
        DiagnosticReport report,
        CancellationToken cancellationToken)
    {
        var path = BuildPath("diagnostico", "json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken);
        return path;
    }

    public async Task<string> ExportHistoryCsvAsync(
        IReadOnlyList<BatterySnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var path = BuildPath("historico", "csv");
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        await writer.WriteLineAsync("timestamp,device_id,soc_percent,voltage_v,current_a,power_w,remaining_ah,temperature_c,cell_delta_mv,charge_state,data_quality");

        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = string.Join(',',
                Csv(snapshot.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
                Csv(snapshot.DeviceId),
                Number(snapshot.StateOfChargePercent),
                Number(snapshot.PackVoltageVolts),
                Number(snapshot.CurrentAmps),
                Number(snapshot.PowerWatts),
                Number(snapshot.RemainingCapacityAh),
                Number(snapshot.TemperaturesCelsius.Count == 0 ? null : snapshot.TemperaturesCelsius.Average()),
                Number(snapshot.CellDeltaMillivolts),
                snapshot.ChargeState,
                snapshot.DataQuality);
            await writer.WriteLineAsync(row.AsMemory(), cancellationToken);
        }

        return path;
    }

    private static string BuildPath(string prefix, string extension) => Path.Combine(
        FileSystem.AppDataDirectory,
        $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.{extension}");

    private static string Number(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
