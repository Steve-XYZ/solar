namespace SolarBmsMonitor.Protocol.PaceEx;

public sealed class PaceExFrameAssembler
{
    private const int MaximumFrameLength = 255 + PaceExFrame.MinimumLength;
    private readonly List<byte> _buffer = [];
    private DateTimeOffset? _lastFragmentAt;

    public int BufferedByteCount => _buffer.Count;

    public IReadOnlyList<PaceExFrame> Append(
        ReadOnlySpan<byte> fragment,
        DateTimeOffset receivedAt,
        TimeSpan fragmentTimeout,
        Action<string>? onDiscardedFrame = null)
    {
        if (_lastFragmentAt is not null && receivedAt - _lastFragmentAt > fragmentTimeout)
        {
            onDiscardedFrame?.Invoke($"Se descartaron {_buffer.Count} bytes incompletos por timeout.");
            _buffer.Clear();
        }

        _lastFragmentAt = receivedAt;
        _buffer.AddRange(fragment.ToArray());
        var frames = new List<PaceExFrame>();

        while (_buffer.Count > 0)
        {
            var start = _buffer.IndexOf(PaceExFrame.StartByte);
            if (start < 0)
            {
                onDiscardedFrame?.Invoke($"Se descartaron {_buffer.Count} bytes sin marcador 9A.");
                _buffer.Clear();
                break;
            }

            if (start > 0)
            {
                onDiscardedFrame?.Invoke($"Se descartaron {start} bytes previos al marcador 9A.");
                _buffer.RemoveRange(0, start);
            }

            if (_buffer.Count < 8)
            {
                break;
            }

            var expectedLength = PaceExFrame.MinimumLength + _buffer[7];
            if (expectedLength > MaximumFrameLength)
            {
                onDiscardedFrame?.Invoke("Longitud de frame fuera de rango.");
                _buffer.RemoveAt(0);
                continue;
            }

            if (_buffer.Count < expectedLength)
            {
                break;
            }

            var candidate = _buffer.GetRange(0, expectedLength).ToArray();
            if (PaceExFrame.TryParse(candidate, out var frame, out var error))
            {
                frames.Add(frame!);
                _buffer.RemoveRange(0, expectedLength);
            }
            else
            {
                onDiscardedFrame?.Invoke(error ?? "Frame inválido.");
                _buffer.RemoveAt(0);
            }
        }

        return frames;
    }

    public void Reset()
    {
        _buffer.Clear();
        _lastFragmentAt = null;
    }
}
