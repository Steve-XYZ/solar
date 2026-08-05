using System.Buffers.Binary;
using SolarBmsMonitor.Core.Calculations;
using SolarBmsMonitor.Core.Models;

namespace SolarBmsMonitor.Protocol.PaceEx;

public sealed record PaceExSystemTelemetry(
    int PackCount,
    double CurrentAmps,
    double PackVoltageVolts,
    double RemainingCapacityAh,
    double DesignedCapacityAh,
    double StateOfChargePercent,
    double StateOfHealthPercent,
    int CycleCount);

public sealed record PaceExCellTelemetry(
    IReadOnlyList<double> CellVoltages,
    IReadOnlyList<double> TemperaturesCelsius);

public static class PaceExParser
{
    private static readonly byte[] SystemReplyType = [0, 0, 0x0A, 0, 0, 0];
    private static readonly byte[] CellReplyType = [0, 0, 0x0A, 2, 0, 0];

    public static bool TryParseSystem(
        PaceExFrame frame,
        out PaceExSystemTelemetry? telemetry,
        out string? error)
    {
        telemetry = null;
        error = null;
        if (!frame.Type.Span.SequenceEqual(SystemReplyType))
        {
            error = "Tipo de respuesta inesperado para telemetría de sistema.";
            return false;
        }

        var data = frame.Payload.Span;
        if (data.Length < 27)
        {
            error = "Payload de sistema truncado.";
            return false;
        }

        var packCount = data[0];
        var current = BinaryPrimitives.ReadInt32BigEndian(data.Slice(1, 4)) / 100d;
        var voltage = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(5, 4)) / 100d;
        var remainingCapacity = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(9, 4)) / 100d;
        var designCapacity = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(13, 4)) / 100d;
        var soc = data[21];
        var soh = data[22];
        var cyclesRaw = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(23, 4));

        if (packCount is < 1 or > 32 || voltage is < 0 or > 1_000 || soc > 100 || soh > 100 || cyclesRaw > int.MaxValue)
        {
            error = "Uno o más valores de sistema están fuera de rango plausible.";
            return false;
        }

        telemetry = new PaceExSystemTelemetry(
            packCount,
            current,
            voltage,
            remainingCapacity,
            designCapacity,
            soc,
            soh,
            (int)cyclesRaw);
        return true;
    }

    public static bool TryParseCells(
        PaceExFrame frame,
        out PaceExCellTelemetry? telemetry,
        out string? error)
    {
        telemetry = null;
        error = null;
        if (!frame.Type.Span.SequenceEqual(CellReplyType))
        {
            error = "Tipo de respuesta inesperado para telemetría de celdas.";
            return false;
        }

        var data = frame.Payload.Span;
        if (data.Length < 4)
        {
            error = "Payload de celdas truncado.";
            return false;
        }

        var cellCount = data[3];
        const int valueSize = 2;
        const int valueGap = 2;
        var requiredCellBytes = 4 + (cellCount - 1) * (valueSize + valueGap) + valueSize;
        if (cellCount is < 1 or > 32 || data.Length < requiredCellBytes)
        {
            error = "Número de celdas o longitud de payload inválidos.";
            return false;
        }

        var cells = new double[cellCount];
        for (var index = 0; index < cellCount; index++)
        {
            cells[index] = BinaryPrimitives.ReadUInt16BigEndian(
                data.Slice(4 + index * (valueSize + valueGap), valueSize)) / 1_000d;
            if (cells[index] is < 1 or > 5)
            {
                error = $"Voltaje de celda {index + 1} fuera de rango plausible.";
                return false;
            }
        }

        // PaceEX interleaves six temperature slots with the first six cell values.
        var temperatures = new List<double>(6);
        for (var index = 0; index < 6; index++)
        {
            var offset = 6 + index * (valueSize + valueGap);
            if (offset + 2 > data.Length)
            {
                break;
            }

            var raw = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            var celsius = (raw - 2731) / 10d;
            if (celsius is >= -50 and <= 150)
            {
                temperatures.Add(celsius);
            }
        }

        telemetry = new PaceExCellTelemetry(cells, temperatures);
        return true;
    }

    public static BatterySnapshot ToSnapshot(
        string deviceId,
        DateTimeOffset timestamp,
        PaceExSystemTelemetry system,
        PaceExCellTelemetry? cells)
    {
        var power = BatteryCalculations.PowerWatts(system.PackVoltageVolts, system.CurrentAmps);
        var cellValues = cells?.CellVoltages ?? [];
        return new BatterySnapshot(
            timestamp,
            deviceId,
            system.StateOfChargePercent,
            system.StateOfHealthPercent,
            system.PackVoltageVolts,
            system.CurrentAmps,
            power,
            system.RemainingCapacityAh,
            null,
            system.DesignedCapacityAh,
            system.CycleCount,
            cellValues,
            cells?.TemperaturesCelsius ?? [],
            cellValues.Count > 0 ? BatteryCalculations.CellDeltaMillivolts(cellValues) : null,
            BatteryCalculations.DetermineChargeState(system.CurrentAmps),
            [],
            cells is null ? DataQuality.Partial : DataQuality.Valid,
            false);
    }
}
