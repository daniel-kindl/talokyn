using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Talokyn.Providers.JsonRpc;

namespace Talokyn.ContractTests.JsonRpc;

public sealed class JsonRpcConnectionTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // A broken behavior must fail the test, not hang it. Each wait, also on a call or a run,
    // goes through the guard token.
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly CancellationToken _cancellationToken;
    private readonly TestPeer _peer = new();
    private readonly Channel<JsonRpcDroppedMessage> _dropped = Channel.CreateUnbounded<JsonRpcDroppedMessage>();
    private readonly FakeTimeProvider _time = new();

    public JsonRpcConnectionTests()
    {
        _guard.CancelAfter(Guard);
        _cancellationToken = _guard.Token;
    }

    public void Dispose() => _guard.Dispose();

    [Fact]
    public async Task GivesEachRequestAUniqueIdAndMatchesRepliesOutOfOrder()
    {
        JsonRpcConnection connection = Connect();
        Task run = connection.RunAsync(_cancellationToken);

        Task<JsonElement>[] calls = [.. Enumerable.Range(0, 3).Select(_ => connection.SendRequestAsync("echo", null, _cancellationToken))];
        List<JsonRpcRequest> requests = [];
        for (int i = 0; i < calls.Length; i++)
        {
            requests.Add(await _peer.ReadRequestAsync(_cancellationToken));
        }

        Assert.Equal(3, requests.Select(request => request.Id).Distinct().Count());

        // Reply in reverse order. Each call must get the result for its own ID.
        foreach (JsonRpcRequest request in Enumerable.Reverse(requests))
        {
            await ReplyAsync(request.Id, $$"""{"id":"{{request.Id}}"}""");
        }

        for (int i = 0; i < calls.Length; i++)
        {
            JsonElement result = await calls[i].WaitAsync(_cancellationToken);
            Assert.Equal(requests[i].Id.ToString(), result.GetProperty("id").GetString());
        }

        _peer.Close();
        await run.WaitAsync(_cancellationToken);
    }

    [Fact]
    public async Task SendsTheMethodAndTheParams()
    {
        JsonRpcConnection connection = Connect();
        _ = connection.RunAsync(_cancellationToken);
        using JsonDocument parameters = JsonDocument.Parse("""{"thread":"t-1"}""");

        _ = connection.SendRequestAsync("turn/start", parameters.RootElement, _cancellationToken);
        await connection.SendNotificationAsync("session/cancel", parameters.RootElement, _cancellationToken);

        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        Assert.Equal("turn/start", request.Method);
        Assert.Equal("t-1", request.Params!.Value.GetProperty("thread").GetString());
        JsonRpcNotification notification = Assert.IsType<JsonRpcNotification>(await _peer.Reader.ReadAsync(_cancellationToken));
        Assert.Equal("session/cancel", notification.Method);
        Assert.Equal("t-1", notification.Params!.Value.GetProperty("thread").GetString());
    }

    [Fact]
    public async Task ThrowsARemoteExceptionForAnErrorReply()
    {
        JsonRpcConnection connection = Connect();
        _ = connection.RunAsync(_cancellationToken);

        Task<JsonElement> call = connection.SendRequestAsync("thread/resume", null, _cancellationToken);
        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        await _peer.Writer.WriteAsync(new JsonRpcErrorResponse(request.Id, new JsonRpcError(-32602, "bad params", null)), _cancellationToken);

        JsonRpcRemoteException exception = await Assert.ThrowsAsync<JsonRpcRemoteException>(() => call.WaitAsync(_cancellationToken));
        Assert.Equal(-32602, exception.Error.Code);
        Assert.Equal("thread/resume", exception.Method);
    }

    [Fact]
    public async Task DropsAReplyWithAnUnknownIdAndKeepsRunning()
    {
        JsonRpcConnection connection = Connect();
        Task run = connection.RunAsync(_cancellationToken);

        await ReplyAsync(JsonRpcId.FromNumber(999), "{}");
        await _peer.Writer.WriteAsync(new JsonRpcErrorResponse(null, new JsonRpcError(-32700, "parse error", null)), _cancellationToken);
        await ReplyAsync(JsonRpcId.FromText("never-sent"), "{}");

        Assert.Equal(new JsonRpcDroppedMessage(JsonRpcDropReason.UnknownResponseId, JsonRpcId.FromNumber(999), null), await NextDropAsync());
        Assert.Equal(new JsonRpcDroppedMessage(JsonRpcDropReason.UnknownResponseId, null, null), await NextDropAsync());
        Assert.Equal(new JsonRpcDroppedMessage(JsonRpcDropReason.UnknownResponseId, JsonRpcId.FromText("never-sent"), null), await NextDropAsync());
        await AssertStillServesRequestsAsync(connection);
        Assert.False(run.IsCompleted);
    }

    [Fact]
    public async Task DropsADuplicateReplyAndKeepsTheFirst()
    {
        JsonRpcConnection connection = Connect();
        Task run = connection.RunAsync(_cancellationToken);

        Task<JsonElement> call = connection.SendRequestAsync("initialize", null, _cancellationToken);
        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        await ReplyAsync(request.Id, """{"n":1}""");
        await ReplyAsync(request.Id, """{"n":2}""");

        Assert.Equal(1, (await call.WaitAsync(_cancellationToken)).GetProperty("n").GetInt32());
        Assert.Equal(new JsonRpcDroppedMessage(JsonRpcDropReason.DuplicateResponse, request.Id, null), await NextDropAsync());
        await AssertStillServesRequestsAsync(connection);
        Assert.False(run.IsCompleted);
    }

    [Fact]
    public async Task DropsANotificationThatTheHandlerCallsOutOfOrder()
    {
        List<string> accepted = [];
        JsonRpcConnection connection = Connect(async (notification, _) =>
        {
            await Task.Yield();
            if (notification.Method == "turn/late")
            {
                return false;
            }

            accepted.Add(notification.Method);
            return true;
        });
        Task run = connection.RunAsync(_cancellationToken);

        await _peer.Writer.WriteAsync(new JsonRpcNotification("turn/late", null), _cancellationToken);
        await _peer.Writer.WriteAsync(new JsonRpcNotification("turn/event", null), _cancellationToken);

        Assert.Equal(new JsonRpcDroppedMessage(JsonRpcDropReason.OutOfOrderNotification, null, "turn/late"), await NextDropAsync());
        await AssertStillServesRequestsAsync(connection);
        Assert.Equal(["turn/event"], accepted);
        Assert.False(run.IsCompleted);
    }

    [Fact]
    public async Task DispatchesNotificationsInArrivalOrderBeforeALaterReply()
    {
        List<string> seen = [];
        JsonRpcConnection connection = Connect(async (notification, _) =>
        {
            await Task.Yield();
            seen.Add(notification.Method);
            return true;
        });
        _ = connection.RunAsync(_cancellationToken);

        Task<JsonElement> call = connection.SendRequestAsync("turn/start", null, _cancellationToken);
        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        await _peer.Writer.WriteAsync(new JsonRpcNotification("event/1", null), _cancellationToken);
        await _peer.Writer.WriteAsync(new JsonRpcNotification("event/2", null), _cancellationToken);
        await ReplyAsync(request.Id, "{}");

        await call.WaitAsync(_cancellationToken);
        Assert.Equal(["event/1", "event/2"], seen);
    }

    [Fact]
    public async Task AnswersARequestFromThePeerWithMethodNotFound()
    {
        JsonRpcConnection connection = Connect();
        _ = connection.RunAsync(_cancellationToken);

        await _peer.Writer.WriteAsync(new JsonRpcRequest(JsonRpcId.FromText("peer-1"), "fs/read", null), _cancellationToken);

        JsonRpcErrorResponse reply = Assert.IsType<JsonRpcErrorResponse>(await _peer.Reader.ReadAsync(_cancellationToken));
        Assert.Equal(JsonRpcId.FromText("peer-1"), reply.Id);
        Assert.Equal(-32601, reply.Error.Code);
    }

    [Fact]
    public async Task TimesOutARequestOnTheInjectedClock()
    {
        // One hour, so that only the fake clock can reach it within the test guard.
        TimeSpan timeout = TimeSpan.FromHours(1);
        JsonRpcConnection connection = Connect(timeout: timeout);
        _ = connection.RunAsync(_cancellationToken);

        Task<JsonElement> call = connection.SendRequestAsync("turn/start", null, _cancellationToken);
        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        _time.Advance(timeout - TimeSpan.FromTicks(1));
        await Task.Delay(50, _cancellationToken);
        Assert.False(call.IsCompleted);

        _time.Advance(TimeSpan.FromTicks(1));

        JsonRpcRequestTimeoutException exception = await Assert.ThrowsAsync<JsonRpcRequestTimeoutException>(() => call.WaitAsync(_cancellationToken));
        Assert.Equal(request.Id, exception.Id);
        Assert.Equal("turn/start", exception.Method);

        await ReplyAsync(request.Id, "{}");
        Assert.Equal(new JsonRpcDroppedMessage(JsonRpcDropReason.DuplicateResponse, request.Id, null), await NextDropAsync());
    }

    [Fact]
    public async Task DoesNotTimeOutOnTheWallClock()
    {
        JsonRpcConnection connection = Connect(timeout: TimeSpan.FromMilliseconds(1));
        _ = connection.RunAsync(_cancellationToken);

        Task<JsonElement> call = connection.SendRequestAsync("turn/start", null, _cancellationToken);
        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        await Task.Delay(100, _cancellationToken);
        await ReplyAsync(request.Id, "{}");

        await call.WaitAsync(_cancellationToken);
    }

    [Fact]
    public async Task CancelsAPendingRequest()
    {
        JsonRpcConnection connection = Connect();
        _ = connection.RunAsync(_cancellationToken);
        using CancellationTokenSource cancel = new();

        Task<JsonElement> call = connection.SendRequestAsync("turn/start", null, cancel.Token);
        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        await cancel.CancelAsync();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(_cancellationToken));
        Assert.Equal(cancel.Token, exception.CancellationToken);

        await ReplyAsync(request.Id, "{}");
        Assert.Equal(new JsonRpcDroppedMessage(JsonRpcDropReason.DuplicateResponse, request.Id, null), await NextDropAsync());
    }

    [Fact]
    public async Task FailsPendingAndNewRequestsWhenTheInputEnds()
    {
        JsonRpcConnection connection = Connect();
        Task run = connection.RunAsync(_cancellationToken);

        Task<JsonElement> call = connection.SendRequestAsync("turn/start", null, _cancellationToken);
        await _peer.ReadRequestAsync(_cancellationToken);
        _peer.Close();

        await run.WaitAsync(_cancellationToken);
        await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(() => call.WaitAsync(_cancellationToken));
        await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(() => connection.SendRequestAsync("turn/start", null, _cancellationToken));
        await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(() => connection.SendNotificationAsync("x", null, _cancellationToken).AsTask());
    }

    [Fact]
    public async Task FailsPendingRequestsWhenAFrameIsRejected()
    {
        JsonRpcConnection connection = Connect();
        Task run = connection.RunAsync(_cancellationToken);

        Task<JsonElement> call = connection.SendRequestAsync("turn/start", null, _cancellationToken);
        await _peer.ReadRequestAsync(_cancellationToken);
        _peer.WriteRaw("not json\n");

        JsonRpcProtocolException fault = await Assert.ThrowsAsync<JsonRpcProtocolException>(() => run.WaitAsync(_cancellationToken));
        Assert.Equal(JsonRpcFrameError.InvalidJson, fault.Error);
        JsonRpcConnectionClosedException closed = await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(() => call.WaitAsync(_cancellationToken));
        Assert.Same(fault, closed.InnerException);
    }

    [Fact]
    public async Task RefusesASecondRun()
    {
        JsonRpcConnection connection = Connect();
        _ = connection.RunAsync(_cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.RunAsync(_cancellationToken));
    }

    [Fact]
    public async Task LogsADroppedReplyAtDebugWithoutThePayload()
    {
        const string Marker = "MARKER-7f3a9c";
        RecordingLogger logger = new();
        JsonRpcConnection connection = Connect(logger: logger);
        _ = connection.RunAsync(_cancellationToken);

        await ReplyAsync(JsonRpcId.FromNumber(999), $$"""{"secret":"{{Marker}}"}""");
        await NextDropAsync();

        RecordedEvent recorded = Assert.Single(logger.Events, e => e.EventId.Name == "JsonRpcMessageDropped");
        Assert.Equal(LogLevel.Debug, recorded.Level);
        Assert.Equal("conn-1", recorded.Fields["ConnectionId"]);
        Assert.Equal("999", recorded.Fields["ResponseId"]);
        Assert.Equal(JsonRpcDropReason.UnknownResponseId, recorded.Fields["Reason"]);
        Assert.DoesNotContain(Marker, recorded.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(recorded.Fields.Values, value => value?.ToString()?.Contains(Marker, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task EscapesAndShortensATextIdInTheLog()
    {
        RecordingLogger logger = new();
        JsonRpcConnection connection = Connect(logger: logger);
        _ = connection.RunAsync(_cancellationToken);

        await ReplyAsync(JsonRpcId.FromText("a\r\nb" + new string('x', 500)), "{}");
        await NextDropAsync();

        string id = Assert.IsType<string>(Assert.Single(logger.Events).Fields["ResponseId"]);
        Assert.DoesNotContain('\r', id);
        Assert.DoesNotContain('\n', id);
        Assert.True(id.Length <= 80, $"The logged ID has {id.Length} characters.");
    }

    [Fact]
    public async Task DropsATextIdThatHasAnEmojiAcrossTheLogLimit()
    {
        // Regression: the cut for the log split the surrogate pair, the escape threw, and the connection ended.
        RecordingLogger logger = new();
        JsonRpcConnection connection = Connect(logger: logger);
        Task run = connection.RunAsync(_cancellationToken);
        JsonRpcId id = JsonRpcId.FromText(new string('x', 63) + "\U0001F600");

        await ReplyAsync(id, "{}");

        Assert.Equal(new JsonRpcDroppedMessage(JsonRpcDropReason.UnknownResponseId, id, null), await NextDropAsync());
        await AssertStillServesRequestsAsync(connection);
        Assert.False(run.IsCompleted);
        Assert.Single(logger.Events);
    }

    [Fact]
    public async Task DropsATextIdThatHasAnEmojiAcrossTheLogLimitWithLoggingOff()
    {
        JsonRpcConnection connection = Connect();
        Task run = connection.RunAsync(_cancellationToken);
        JsonRpcId id = JsonRpcId.FromText(new string('x', 63) + "\U0001F600");

        await ReplyAsync(id, "{}");

        Assert.Equal(new JsonRpcDroppedMessage(JsonRpcDropReason.UnknownResponseId, id, null), await NextDropAsync());
        await AssertStillServesRequestsAsync(connection);
        Assert.False(run.IsCompleted);
    }

    [Fact]
    public async Task KeepsAReplyThatArrivesBeforeTheTimeoutFires()
    {
        // The reply completes the request first. The timeout then fires before the caller resumes.
        // The caller must get the result, and the late timeout must change nothing.
        TimeSpan timeout = TimeSpan.FromHours(1);
        JsonRpcConnection connection = Connect(
            (notification, _) =>
            {
                _time.Advance(timeout);
                return ValueTask.FromResult(true);
            },
            timeout);
        _ = connection.RunAsync(_cancellationToken);

        Task<JsonElement> call = connection.SendRequestAsync("turn/start", null, _cancellationToken);
        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        await ReplyAsync(request.Id, """{"ok":true}""");
        await _peer.Writer.WriteAsync(new JsonRpcNotification("tick", null), _cancellationToken);

        Assert.True((await call.WaitAsync(_cancellationToken)).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task KeepsAReplyThatArrivesBeforeTheCallerCancels()
    {
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        JsonRpcConnection connection = Connect(async (notification, _) =>
        {
            await cancel.CancelAsync();
            return true;
        });
        _ = connection.RunAsync(_cancellationToken);

        Task<JsonElement> call = connection.SendRequestAsync("turn/start", null, cancel.Token);
        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        await ReplyAsync(request.Id, """{"ok":true}""");
        await _peer.Writer.WriteAsync(new JsonRpcNotification("tick", null), _cancellationToken);

        Assert.True((await call.WaitAsync(_cancellationToken)).GetProperty("ok").GetBoolean());
    }

    private JsonRpcConnection Connect(JsonRpcNotificationHandler? handler = null, TimeSpan? timeout = null, ILogger? logger = null) =>
        _peer.Connect(
            new JsonRpcConnectionOptions(timeout ?? Timeout, handler, dropped => _dropped.Writer.TryWrite(dropped)),
            _time,
            logger ?? NullLogger.Instance);

    private async Task ReplyAsync(JsonRpcId id, string json)
    {
        using JsonDocument result = JsonDocument.Parse(json);
        await _peer.Writer.WriteAsync(new JsonRpcResponse(id, result.RootElement), _cancellationToken);
    }

    private async Task<JsonRpcDroppedMessage> NextDropAsync() => await _dropped.Reader.ReadAsync(_cancellationToken);

    private async Task AssertStillServesRequestsAsync(JsonRpcConnection connection)
    {
        Task<JsonElement> call = connection.SendRequestAsync("ping", null, _cancellationToken);
        JsonRpcRequest request = await _peer.ReadRequestAsync(_cancellationToken);
        await ReplyAsync(request.Id, """{"ok":true}""");
        Assert.True((await call.WaitAsync(_cancellationToken)).GetProperty("ok").GetBoolean());
    }
}
