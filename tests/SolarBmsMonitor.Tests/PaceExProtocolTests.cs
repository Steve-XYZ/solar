using SolarBmsMonitor.Protocol.PaceEx;

namespace SolarBmsMonitor.Tests;

public sealed class PaceExProtocolTests
{
    // Anonymized frame from patman15/aiobmsble tests, Apache-2.0.
    private static readonly byte[] SystemFrame = Convert.FromHexString(
        "9A00000A0000003301FFFFFE59000014B500001CE800002710000027104A64000000EF000000000000000001080CEF01100CEC01030B8E01010B8999169D");

    private static readonly byte[] CellFrame = Convert.FromHexString(
        "9A00000A020000440214B6100CEE0B890CEC0B8B0CEE0B8E0CEF0B8B0CEF00000CEE00000CEF00000CEF00000CEF00000CF000000CEF00000CEE00000CEE00000CEF00000CEE00000CEC000032A19D");

    [Fact]
    public void ReadOnlyCommandsMatchReferenceFrames()
    {
        Assert.Equal("9A00000A0000000019519D", Convert.ToHexString(PaceExCommands.Build(PaceExReadQuery.SystemTelemetry)));
        Assert.Equal("9A00000A020000020101289C9D", Convert.ToHexString(PaceExCommands.Build(PaceExReadQuery.CellTelemetry)));
        Assert.Equal("9A00000002000000A0C89D", Convert.ToHexString(PaceExCommands.Build(PaceExReadQuery.SerialNumber)));
        Assert.Equal("9A00000001000000E4C89D", Convert.ToHexString(PaceExCommands.Build(PaceExReadQuery.VersionInformation)));
    }

    [Fact]
    public void ParsesValidCrcAndRejectsInvalidCrc()
    {
        Assert.True(PaceExFrame.TryParse(SystemFrame, out var frame, out var error), error);
        Assert.NotNull(frame);

        var invalid = SystemFrame.ToArray();
        invalid[^2] ^= 0xFF;
        Assert.False(PaceExFrame.TryParse(invalid, out _, out var invalidError));
        Assert.Contains("CRC", invalidError, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTruncatedPacket()
    {
        Assert.False(PaceExFrame.TryParse(SystemFrame.AsSpan(0, 10), out _, out var error));
        Assert.Equal("Frame truncado.", error);
    }

    [Fact]
    public void AssemblesFragmentedFrame()
    {
        var assembler = new PaceExFrameAssembler();
        var now = DateTimeOffset.UtcNow;

        Assert.Empty(assembler.Append(SystemFrame.AsSpan(0, 17), now, TimeSpan.FromSeconds(2)));
        var frames = assembler.Append(SystemFrame.AsSpan(17), now.AddMilliseconds(20), TimeSpan.FromSeconds(2));

        var frame = Assert.Single(frames);
        Assert.Equal(SystemFrame, frame.Bytes.ToArray());
        Assert.Equal(0, assembler.BufferedByteCount);
    }

    [Fact]
    public void AssemblesMultipleFramesFromOneNotification()
    {
        var assembler = new PaceExFrameAssembler();
        var combined = SystemFrame.Concat(CellFrame).ToArray();

        var frames = assembler.Append(combined, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));

        Assert.Equal(2, frames.Count);
        Assert.Equal(SystemFrame, frames[0].Bytes.ToArray());
        Assert.Equal(CellFrame, frames[1].Bytes.ToArray());
    }

    [Fact]
    public void DropsIncompleteDataAfterTimeout()
    {
        var messages = new List<string>();
        var assembler = new PaceExFrameAssembler();
        var now = DateTimeOffset.UtcNow;
        assembler.Append(SystemFrame.AsSpan(0, 9), now, TimeSpan.FromSeconds(1));

        var frames = assembler.Append(SystemFrame, now.AddSeconds(2), TimeSpan.FromSeconds(1), messages.Add);

        Assert.Single(frames);
        Assert.Contains(messages, message => message.Contains("timeout", StringComparison.Ordinal));
    }

    [Fact]
    public void ParsesSignedSystemTelemetryAndScales()
    {
        Assert.True(PaceExFrame.TryParse(SystemFrame, out var frame, out var frameError), frameError);
        Assert.True(PaceExParser.TryParseSystem(frame!, out var telemetry, out var parseError), parseError);

        Assert.NotNull(telemetry);
        Assert.Equal(-4.23, telemetry.CurrentAmps, 2);
        Assert.Equal(53.01, telemetry.PackVoltageVolts, 2);
        Assert.Equal(74, telemetry.StateOfChargePercent);
        Assert.Equal(100, telemetry.StateOfHealthPercent);
        Assert.Equal(74, telemetry.RemainingCapacityAh, 2);
        Assert.Equal(100, telemetry.DesignedCapacityAh, 2);
        Assert.Equal(239, telemetry.CycleCount);
    }

    [Fact]
    public void ParsesInterleavedCellsAndTemperatures()
    {
        Assert.True(PaceExFrame.TryParse(CellFrame, out var frame, out var frameError), frameError);
        Assert.True(PaceExParser.TryParseCells(frame!, out var telemetry, out var parseError), parseError);

        Assert.NotNull(telemetry);
        Assert.Equal(16, telemetry.CellVoltages.Count);
        Assert.Equal(3.31, telemetry.CellVoltages[0], 3);
        Assert.Equal(3.308, telemetry.CellVoltages[^1], 3);
        Assert.Equal([22.2, 22.4, 22.7, 22.4], telemetry.TemperaturesCelsius);
    }

    [Fact]
    public void ParsesEightCellLayoutExpectedForTargetBattery()
    {
        var payload = new byte[4 + 8 * 4];
        payload[3] = 8;
        for (var index = 0; index < 8; index++)
        {
            var millivolts = (ushort)(3_300 + index);
            payload[4 + index * 4] = (byte)(millivolts >> 8);
            payload[5 + index * 4] = (byte)millivolts;
            if (index < 4)
            {
                const ushort temperatureRaw = 2_951; // 22.0 °C after Kelvin offset.
                payload[6 + index * 4] = (byte)(temperatureRaw >> 8);
                payload[7 + index * 4] = (byte)(temperatureRaw & 0xFF);
            }
        }

        var bytes = BuildResponse([0, 0, 0x0A, 2, 0, 0], payload);
        Assert.True(PaceExFrame.TryParse(bytes, out var frame, out var frameError), frameError);
        Assert.True(PaceExParser.TryParseCells(frame!, out var telemetry, out var parseError), parseError);

        Assert.NotNull(telemetry);
        Assert.Equal(8, telemetry.CellVoltages.Count);
        Assert.Equal(3.300, telemetry.CellVoltages[0], 3);
        Assert.Equal(3.307, telemetry.CellVoltages[^1], 3);
        Assert.Equal([22.0, 22.0, 22.0, 22.0], telemetry.TemperaturesCelsius);
    }

    [Fact]
    public void RejectsOutOfRangeSystemValue()
    {
        var invalid = SystemFrame.ToArray();
        invalid[29] = 101;
        RewriteCrc(invalid);
        Assert.True(PaceExFrame.TryParse(invalid, out var frame, out _));

        Assert.False(PaceExParser.TryParseSystem(frame!, out _, out var error));
        Assert.Contains("fuera de rango", error, StringComparison.Ordinal);
    }

    private static void RewriteCrc(Span<byte> frame)
    {
        var crc = PaceExCrc.Compute(frame[..^3]);
        frame[^3] = (byte)(crc >> 8);
        frame[^2] = (byte)crc;
    }

    private static byte[] BuildResponse(ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[PaceExFrame.MinimumLength + payload.Length];
        frame[0] = PaceExFrame.StartByte;
        type.CopyTo(frame.AsSpan(1, 6));
        frame[7] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(8));
        frame[^1] = PaceExFrame.EndByte;
        RewriteCrc(frame);
        return frame;
    }
}
