namespace Talokyn.Providers.JsonRpc;

/// <summary>Reads newline-delimited JSON-RPC 2.0 frames from a stream.</summary>
/// <remarks>
/// The buffer never grows past <see cref="JsonRpcFrameLimits.MaxFrameBytes"/> plus one byte,
/// so a peer cannot make the reader allocate an unbounded frame. After the first rejected
/// frame the reader is faulted: each later call throws the same error. The reader is not
/// safe for concurrent calls.
/// </remarks>
public sealed class JsonRpcFrameReader
{
    private const int InitialBufferSize = 4096;

    private readonly Stream _stream;
    private readonly JsonRpcFrameLimits _limits;
    private byte[] _buffer;
    private int _start;
    private int _end;
    private int _scanned;
    private JsonRpcProtocolException? _fault;

    public JsonRpcFrameReader(Stream stream, JsonRpcFrameLimits limits)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);
        _stream = stream;
        _limits = limits;
        _buffer = new byte[Math.Min(InitialBufferSize, limits.MaxFrameBytes + 1)];
    }

    /// <summary>Reads the next message. Returns null when the stream ends between frames.</summary>
    /// <exception cref="JsonRpcProtocolException">The peer broke the framing or the protocol.</exception>
    public async ValueTask<JsonRpcMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        if (_fault is not null)
        {
            throw _fault;
        }

        try
        {
            while (await ReadFrameAsync(cancellationToken) is ReadOnlyMemory<byte> frame)
            {
                if (!IsBlank(frame.Span))
                {
                    return JsonRpcMessageParser.Parse(frame, _limits);
                }
            }

            return null;
        }
        catch (JsonRpcProtocolException exception)
        {
            _fault = exception;
            throw;
        }
    }

    // The returned memory is valid only until the next call, because the buffer is reused.
    private async ValueTask<ReadOnlyMemory<byte>?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            int index = _buffer.AsSpan(_scanned, _end - _scanned).IndexOf((byte)'\n');
            if (index >= 0)
            {
                int newline = _scanned + index;
                int length = newline - _start;
                ThrowIfTooLarge(length);
                ReadOnlyMemory<byte> frame = new(_buffer, _start, length);
                _start = newline + 1;
                _scanned = _start;
                return frame;
            }

            _scanned = _end;
            ThrowIfTooLarge(_end - _start);
            MakeRoom();

            int read = await _stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken);
            if (read == 0)
            {
                bool partial = !IsBlank(_buffer.AsSpan(_start, _end - _start));
                _start = _end;
                _scanned = _end;
                return partial
                    ? throw new JsonRpcProtocolException(JsonRpcFrameError.TruncatedFrame, "The stream ended in the middle of a frame.")
                    : null;
            }

            _end += read;
        }
    }

    private void MakeRoom()
    {
        if (_end < _buffer.Length)
        {
            return;
        }

        int pending = _end - _start;
        if (_start > 0)
        {
            _buffer.AsSpan(_start, pending).CopyTo(_buffer);
        }
        else
        {
            // The caller checked the length, so the buffer is below its cap here.
            byte[] larger = new byte[Math.Min(_buffer.Length * 2, _limits.MaxFrameBytes + 1)];
            _buffer.AsSpan(0, pending).CopyTo(larger);
            _buffer = larger;
        }

        _scanned -= _start;
        _start = 0;
        _end = pending;
    }

    private void ThrowIfTooLarge(int length)
    {
        if (length > _limits.MaxFrameBytes)
        {
            throw new JsonRpcProtocolException(
                JsonRpcFrameError.FrameTooLarge,
                $"A frame is longer than the limit of {_limits.MaxFrameBytes} bytes.");
        }
    }

    private static bool IsBlank(ReadOnlySpan<byte> bytes) => bytes.Trim(" \t\r"u8).IsEmpty;
}
