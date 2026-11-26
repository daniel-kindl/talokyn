using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Talokyn.Providers.JsonRpc;

/// <summary>Correlates requests with replies and dispatches notifications on one JSON-RPC 2.0 connection.</summary>
/// <remarks>
/// <para>
/// <see cref="RunAsync"/> owns the reader. It handles one message at a time in arrival order, so a
/// notification that arrives before a reply reaches the handler before the request completes.
/// </para>
/// <para>
/// A reply with an unknown or a duplicate ID, and a notification that the handler calls out of
/// order, are dropped with a typed <see cref="JsonRpcDroppedMessage"/>. The connection keeps
/// running. A request from the peer gets the error -32601, because this connection accepts no
/// method from the peer. A rejected frame or the end of the input ends the connection: each
/// pending request fails with <see cref="JsonRpcConnectionClosedException"/>, and so does each
/// later call.
/// </para>
/// <para>Request IDs are integers from 1 that increase by one.</para>
/// </remarks>
public sealed partial class JsonRpcConnection
{
    private const long MethodNotFound = -32601;
    private const int MaxLoggedIdLength = 64;

    private readonly JsonRpcFrameReader _reader;
    private readonly JsonRpcFrameWriter _writer;
    private readonly JsonRpcConnectionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lock _gate = new();
    private readonly Dictionary<long, PendingRequest> _pending = [];
    private long _lastId;
    private bool _running;
    private JsonRpcConnectionClosedException? _closed;

    public JsonRpcConnection(
        string connectionId,
        JsonRpcFrameReader reader,
        JsonRpcFrameWriter writer,
        JsonRpcConnectionOptions options,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.RequestTimeout, TimeSpan.Zero);
        ConnectionId = connectionId;
        _reader = reader;
        _writer = writer;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>The ID that the log events of this connection carry.</summary>
    public string ConnectionId { get; }

    /// <summary>Reads and handles messages until the input ends. Call it once.</summary>
    /// <returns>A task that completes when the peer closes the input between frames.</returns>
    /// <exception cref="JsonRpcProtocolException">The peer sent a frame that the reader rejected.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="InvalidOperationException">The connection already runs or ran.</exception>
    /// <remarks>An exception from the notification handler also ends the connection and comes out of this task.</remarks>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_running)
            {
                throw new InvalidOperationException("The connection already runs or ran.");
            }

            _running = true;
        }

        try
        {
            while (await _reader.ReadAsync(cancellationToken) is JsonRpcMessage message)
            {
                await HandleAsync(message, cancellationToken);
            }

            Close(new JsonRpcConnectionClosedException("The peer closed the connection."));
        }
        catch (Exception exception)
        {
            Close(new JsonRpcConnectionClosedException("The connection ended because of an error.", exception));
            throw;
        }
    }

    /// <summary>Sends a request and waits for its reply.</summary>
    /// <returns>The result of the reply.</returns>
    /// <exception cref="JsonRpcRemoteException">The peer answered with an error object.</exception>
    /// <exception cref="JsonRpcRequestTimeoutException">No reply came within the request timeout.</exception>
    /// <exception cref="JsonRpcConnectionClosedException">The connection ended before the reply, or the peer no longer reads.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled. The request is no longer pending.</exception>
    public async Task<JsonElement> SendRequestAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        using CancellationTokenSource timeout = new(_options.RequestTimeout, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        PendingRequest pending = Register(method);

        // The reply, the close, the timeout and the cancellation each try to remove the request
        // from the pending set under the lock. Only the one that removes it sets the outcome.
        using CancellationTokenRegistration registration = linked.Token.Register(() => Abandon(pending, cancellationToken));
        try
        {
            await WriteAsync(new JsonRpcRequest(pending.Id, method, parameters), linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // The write stopped because of the timeout or the cancellation. Abandon sets the
            // outcome, unless a reply or the close removed the request first.
        }
        catch
        {
            Unregister(pending);
            throw;
        }

        return await pending.Completion.Task;
    }

    /// <summary>Sends a notification. The peer sends no reply.</summary>
    /// <exception cref="JsonRpcConnectionClosedException">The connection ended, or the peer no longer reads.</exception>
    public async ValueTask SendNotificationAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        lock (_gate)
        {
            ThrowIfClosed();
        }

        await WriteAsync(new JsonRpcNotification(method, parameters), cancellationToken);
    }

    private async ValueTask HandleAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case JsonRpcResponse response:
                Complete(response.Id, pending => pending.Completion.TrySetResult(response.Result));
                break;
            case JsonRpcErrorResponse error when error.Id is JsonRpcId id:
                Complete(id, pending => pending.Completion.TrySetException(new JsonRpcRemoteException(pending.Method, error.Error)));
                break;
            case JsonRpcErrorResponse:
                Drop(JsonRpcDropReason.UnknownResponseId, null, null);
                break;
            case JsonRpcNotification notification:
                bool accepted = _options.NotificationHandler is not JsonRpcNotificationHandler handler
                    || await handler(notification, cancellationToken);
                if (!accepted)
                {
                    Drop(JsonRpcDropReason.OutOfOrderNotification, null, notification.Method);
                }

                break;
            case JsonRpcRequest request:
                await WriteAsync(
                    new JsonRpcErrorResponse(request.Id, new JsonRpcError(MethodNotFound, "Method not found", null)),
                    cancellationToken);
                break;
        }
    }

    private PendingRequest Register(string method)
    {
        lock (_gate)
        {
            ThrowIfClosed();
            PendingRequest pending = new(JsonRpcId.FromNumber(++_lastId), method);
            _pending.Add(pending.Id.Number!.Value, pending);
            return pending;
        }
    }

    private void Abandon(PendingRequest pending, CancellationToken callerToken)
    {
        if (!Unregister(pending))
        {
            return;
        }

        if (callerToken.IsCancellationRequested)
        {
            pending.Completion.TrySetCanceled(callerToken);
        }
        else
        {
            pending.Completion.TrySetException(new JsonRpcRequestTimeoutException(pending.Method, pending.Id, _options.RequestTimeout));
        }
    }

    private bool Unregister(PendingRequest pending)
    {
        lock (_gate)
        {
            return _pending.Remove(pending.Id.Number!.Value);
        }
    }

    private void Complete(JsonRpcId id, Action<PendingRequest> complete)
    {
        PendingRequest? pending = null;
        bool issued = false;
        lock (_gate)
        {
            if (id.Number is long number)
            {
                issued = number >= 1 && number <= _lastId;
                _pending.Remove(number, out pending);
            }
        }

        if (pending is not null)
        {
            complete(pending);
            return;
        }

        Drop(issued ? JsonRpcDropReason.DuplicateResponse : JsonRpcDropReason.UnknownResponseId, id, null);
    }

    private void Drop(JsonRpcDropReason reason, JsonRpcId? id, string? method)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            LogMessageDropped(ConnectionId, reason, FormatIdForLog(id));
        }

        _options.MessageDropped?.Invoke(new JsonRpcDroppedMessage(reason, id, method));
    }

    private void Close(JsonRpcConnectionClosedException closed)
    {
        PendingRequest[] pending;
        lock (_gate)
        {
            _closed ??= closed;
            pending = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (PendingRequest request in pending)
        {
            request.Completion.TrySetException(_closed);
        }
    }

    private void ThrowIfClosed()
    {
        if (_closed is not null)
        {
            throw new JsonRpcConnectionClosedException("The connection is closed.", _closed.InnerException);
        }
    }

    private async ValueTask WriteAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The peer closed its input, or the owner of the stream closed it, for example because
            // the child exited or is stopping.
            throw new JsonRpcConnectionClosedException("The peer no longer reads from the connection.", exception);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // A text ID comes from the peer. Escape each character outside printable ASCII, and the quote
    // and the backslash, as \uXXXX, and cap the length. So the log cannot be injected, flooded or
    // shown differently from its content, for example with U+2028 or U+202E. The result is
    // printable ASCII only, and the escape never throws, even for a lone surrogate.
    private static string? FormatIdForLog(JsonRpcId? id)
    {
        if (id is not JsonRpcId value)
        {
            return null;
        }

        if (value.Text is not string text)
        {
            return value.ToString();
        }

        System.Text.StringBuilder escaped = new(MaxLoggedIdLength + 8);
        foreach (char c in text)
        {
            if (escaped.Length >= MaxLoggedIdLength)
            {
                return escaped.Append("...").ToString();
            }

            if (c is < ' ' or > '~' or '"' or '\\')
            {
                escaped.Append(System.Globalization.CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
            }
            else
            {
                escaped.Append(c);
            }
        }

        return escaped.ToString();
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "JsonRpcMessageDropped",
        Level = LogLevel.Debug,
        Message = "Connection {ConnectionId} dropped a message: {Reason}, response ID {ResponseId}.",
        SkipEnabledCheck = true)]
    private partial void LogMessageDropped(string connectionId, JsonRpcDropReason reason, string? responseId);

    private sealed class PendingRequest(JsonRpcId id, string method)
    {
        public JsonRpcId Id { get; } = id;

        public string Method { get; } = method;

        public TaskCompletionSource<JsonElement> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
