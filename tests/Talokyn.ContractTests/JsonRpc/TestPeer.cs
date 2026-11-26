using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Talokyn.Providers.JsonRpc;

namespace Talokyn.ContractTests.JsonRpc;

/// <summary>A one-way in-memory pipe. Writes become readable at once. Completing the writer ends the stream.</summary>
internal sealed class PipeStream : Stream
{
    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
    private ReadOnlyMemory<byte> _current;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void CompleteWriting() => _chunks.Writer.TryComplete();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_current.IsEmpty)
        {
            if (!await _chunks.Reader.WaitToReadAsync(cancellationToken))
            {
                return 0;
            }

            _chunks.Reader.TryRead(out byte[]? chunk);
            _current = chunk;
        }

        int count = Math.Min(buffer.Length, _current.Length);
        _current[..count].CopyTo(buffer);
        _current = _current[count..];
        return count;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (!_chunks.Writer.TryWrite(buffer.ToArray()))
        {
            throw new IOException("The pipe is closed.");
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>The far end of a connection under test. The test plays the provider through it.</summary>
internal sealed class TestPeer
{
    private readonly PipeStream _toConnection = new();
    private readonly PipeStream _fromConnection = new();

    public TestPeer()
    {
        Reader = new JsonRpcFrameReader(_fromConnection, JsonRpcFrameLimits.Default);
        Writer = new JsonRpcFrameWriter(_toConnection, JsonRpcFrameLimits.Default);
    }

    /// <summary>Reads the frames that the connection writes.</summary>
    public JsonRpcFrameReader Reader { get; }

    /// <summary>Writes frames that the connection reads.</summary>
    public JsonRpcFrameWriter Writer { get; }

    public JsonRpcConnection Connect(JsonRpcConnectionOptions options, TimeProvider timeProvider, ILogger logger) =>
        new(
            "conn-1",
            new JsonRpcFrameReader(_toConnection, JsonRpcFrameLimits.Default),
            new JsonRpcFrameWriter(_fromConnection, JsonRpcFrameLimits.Default),
            options,
            timeProvider,
            logger);

    public void WriteRaw(string line) => _toConnection.Write(Encoding.UTF8.GetBytes(line));

    /// <summary>Ends the stream that the connection reads.</summary>
    public void Close() => _toConnection.CompleteWriting();

    public async Task<JsonRpcRequest> ReadRequestAsync(CancellationToken cancellationToken) =>
        Assert.IsType<JsonRpcRequest>(await Reader.ReadAsync(cancellationToken));
}

/// <summary>A logger that keeps each event with its structured fields.</summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly Lock _gate = new();
    private readonly List<RecordedEvent> _events = [];

    public IReadOnlyList<RecordedEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Dictionary<string, object?> fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
            : [];
        lock (_gate)
        {
            _events.Add(new RecordedEvent(logLevel, eventId, fields, formatter(state, exception)));
        }
    }
}

internal sealed record RecordedEvent(LogLevel Level, EventId EventId, IReadOnlyDictionary<string, object?> Fields, string Message);
