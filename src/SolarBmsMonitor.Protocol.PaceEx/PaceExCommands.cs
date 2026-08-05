namespace SolarBmsMonitor.Protocol.PaceEx;

public enum PaceExReadQuery
{
    SystemTelemetry,
    CellTelemetry,
    SerialNumber,
    VersionInformation,
}

public static class PaceExCommands
{
    private static readonly Dictionary<PaceExReadQuery, byte[]> AllowedQueries =
        new Dictionary<PaceExReadQuery, byte[]>
        {
            [PaceExReadQuery.SystemTelemetry] = [0x00, 0x00, 0x0A, 0x00, 0x00, 0x00],
            [PaceExReadQuery.CellTelemetry] = [0x00, 0x00, 0x0A, 0x02, 0x00, 0x00],
            [PaceExReadQuery.SerialNumber] = [0x00, 0x00, 0x00, 0x02, 0x00, 0x00],
            [PaceExReadQuery.VersionInformation] = [0x00, 0x00, 0x00, 0x01, 0x00, 0x00],
        };

    public static byte[] Build(PaceExReadQuery query)
    {
        if (!AllowedQueries.TryGetValue(query, out var command))
        {
            throw new ArgumentOutOfRangeException(nameof(query), "La consulta no está en la lista blanca de solo lectura.");
        }

        ReadOnlySpan<byte> payload = query == PaceExReadQuery.CellTelemetry ? [0x01, 0x01] : [];
        var frame = new byte[PaceExFrame.MinimumLength + payload.Length];
        frame[0] = PaceExFrame.StartByte;
        command.CopyTo(frame, 1);
        frame[7] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(8));
        var crc = PaceExCrc.Compute(frame.AsSpan(0, frame.Length - 3));
        frame[^3] = (byte)(crc >> 8);
        frame[^2] = (byte)crc;
        frame[^1] = PaceExFrame.EndByte;
        return frame;
    }
}
