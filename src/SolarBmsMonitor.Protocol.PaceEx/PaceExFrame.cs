namespace SolarBmsMonitor.Protocol.PaceEx;

public sealed record PaceExFrame(ReadOnlyMemory<byte> Bytes)
{
    public const byte StartByte = 0x9A;
    public const byte EndByte = 0x9D;
    public const int MinimumLength = 11;

    public ReadOnlyMemory<byte> Type => Bytes.Slice(1, 6);
    public ReadOnlyMemory<byte> Payload => Bytes.Slice(8, Bytes.Span[7]);

    public static bool TryParse(ReadOnlySpan<byte> data, out PaceExFrame? frame, out string? error)
    {
        frame = null;
        error = null;

        if (data.Length < MinimumLength)
        {
            error = "Frame truncado.";
            return false;
        }

        if (data[0] != StartByte || data[^1] != EndByte)
        {
            error = "Marcadores de inicio o fin inválidos.";
            return false;
        }

        var expectedLength = MinimumLength + data[7];
        if (data.Length != expectedLength)
        {
            error = $"Longitud inválida: {data.Length}; esperada: {expectedLength}.";
            return false;
        }

        var expectedCrc = (ushort)((data[^3] << 8) | data[^2]);
        var actualCrc = PaceExCrc.Compute(data[..^3]);
        if (expectedCrc != actualCrc)
        {
            error = $"CRC inválido: {expectedCrc:X4}; calculado: {actualCrc:X4}.";
            return false;
        }

        frame = new PaceExFrame(data.ToArray());
        return true;
    }
}
