using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Talokyn.Providers.JsonRpc;

namespace Talokyn.ContractTests.JsonRpc;

public sealed class JsonRpcStdioProcessTests : IAsyncLifetime
{
    // A broken behavior must fail the test, not hang it. Each wait goes through the guard token.
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly List<JsonRpcStdioProcess> _started = [];
    private readonly Channel<JsonRpcDroppedMessage> _dropped = Channel.CreateUnbounded<JsonRpcDroppedMessage>();
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingLogger _logger = new();
    private readonly CancellationToken _token;

    public JsonRpcStdioProcessTests()
    {
        _guard.CancelAfter(Guard);
        _token = _guard.Token;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (JsonRpcStdioProcess process in _started)
        {
            await process.DisposeAsync();
        }

        _guard.Dispose();
    }

    [Fact]
    public async Task NormalModeAnswersARequestAndStopsCleanly()
    {
        JsonRpcStdioProcess process = Start("normal");

        JsonElement result = await process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token);
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal("turn/start", result.GetProperty("method").GetString());
        Assert.Equal(0, exit.ExitCode);
        Assert.False(exit.Forced);
        Assert.Equal(1, RequestCount(exit));
        await process.ConnectionCompleted.WaitAsync(_token);
    }

    [Fact]
    public async Task SlowModeAnswersAfterADelay()
    {
        JsonRpcStdioProcess process = Start("slow");

        JsonElement result = await process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token);
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal("turn/start", result.GetProperty("method").GetString());
        Assert.Equal(0, exit.ExitCode);
        Assert.False(exit.Forced);
    }

    [Fact]
    public async Task HungModeTimesOutTheRequestAndEndsByForcedTermination()
    {
        TimeSpan step = TimeSpan.FromSeconds(5);
        JsonRpcStdioProcess process = Start("hung", timeout: step, grace: step);

        Task<JsonElement> call = process.Connection.SendRequestAsync("turn/start", null, _token);
        await Assert.ThrowsAsync<JsonRpcRequestTimeoutException>(() => AdvanceUntilAsync(call, step));
        JsonRpcStdioExit exit = await AdvanceUntilAsync(process.StopAsync(_token), step);

        Assert.True(exit.Forced);
        Assert.NotEqual(0, exit.ExitCode);
        await process.ConnectionCompleted.WaitAsync(_token);
        RecordedEvent warning = Assert.Single(_logger.Events, e => e.EventId.Name == "ProviderForcedTermination");
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal(process.ProcessId, warning.Fields["ProcessId"]);
    }

    [Fact]
    public async Task PartialModeEndsTheConnectionWithATruncatedFrame()
    {
        JsonRpcStdioProcess process = Start("partial");

        JsonRpcConnectionClosedException closed = await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(
            () => process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token));
        JsonRpcProtocolException fault = await Assert.ThrowsAsync<JsonRpcProtocolException>(() => process.ConnectionCompleted.WaitAsync(_token));
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal(JsonRpcFrameError.TruncatedFrame, fault.Error);
        Assert.Same(fault, closed.InnerException);
        Assert.Equal(0, exit.ExitCode);
        Assert.False(exit.Forced);
    }

    [Fact]
    public async Task MalformedModeEndsTheConnectionAndNothingIsSentAfterTheRejection()
    {
        JsonRpcStdioProcess process = Start("malformed");

        await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(
            () => process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token));
        JsonRpcProtocolException fault = await Assert.ThrowsAsync<JsonRpcProtocolException>(() => process.ConnectionCompleted.WaitAsync(_token));
        await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(
            () => process.Connection.SendRequestAsync("turn/steer", null, _token).WaitAsync(_token));
        await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(
            () => process.Connection.SendNotificationAsync("turn/interrupt", null, _token).AsTask().WaitAsync(_token));
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal(JsonRpcFrameError.InvalidJson, fault.Error);
        Assert.Equal(0, exit.ExitCode);
        Assert.Equal(1, RequestCount(exit));
    }

    [Fact]
    public async Task DoubleAckModeDeliversTheFirstReplyAndDropsTheSecond()
    {
        JsonRpcStdioProcess process = Start("double-ack");

        JsonElement result = await process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token);
        JsonRpcDroppedMessage dropped = await _dropped.Reader.ReadAsync(_token);
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal("turn/start", result.GetProperty("method").GetString());
        Assert.Equal(JsonRpcDropReason.DuplicateResponse, dropped.Reason);
        Assert.Equal(0, exit.ExitCode);
        Assert.False(exit.Forced);
    }

    [Fact]
    public async Task CrashBeforeAckFailsTheRequestAndTheChildExitsWithItsCode()
    {
        JsonRpcStdioProcess process = Start("crash-before-ack");

        await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(
            () => process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token));
        await process.ConnectionCompleted.WaitAsync(_token);
        await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(
            () => process.Connection.SendRequestAsync("turn/steer", null, _token).WaitAsync(_token));
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal(3, exit.ExitCode);
        Assert.False(exit.Forced);
        Assert.Equal(1, RequestCount(exit));
    }

    [Fact]
    public async Task CrashAfterAckDeliversTheReplyAndThenTheChildExitsWithItsCode()
    {
        JsonRpcStdioProcess process = Start("crash-after-ack");

        JsonElement result = await process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token);
        await process.ConnectionCompleted.WaitAsync(_token);
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal("turn/start", result.GetProperty("method").GetString());
        Assert.Equal(3, exit.ExitCode);
        Assert.False(exit.Forced);
    }

    [Fact]
    public async Task PassesEachArgumentAsOneItemWithoutAShell()
    {
        // A shell would split this at "&&" and run the second command. The fake provider takes
        // it as one unknown mode and exits with the usage code.
        JsonRpcStdioProcess process = StartWith(["--mode", "normal && echo hacked"]);

        await process.ConnectionCompleted.WaitAsync(_token);
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal(2, exit.ExitCode);
        Assert.Contains("usage", exit.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsStderrApartFromTheProtocolStream()
    {
        JsonRpcStdioProcess process = Start("normal");

        await process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token);
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        // The request counter is on stderr. If it were on the protocol stream, the reader would reject it.
        Assert.Contains("\"requests\"", exit.Stderr, StringComparison.Ordinal);
        await process.ConnectionCompleted.WaitAsync(_token);
    }

    [Fact]
    public async Task LogsTheStartAndTheExitWithTheExecutableNameTheProcessIdAndTheExitCode()
    {
        JsonRpcStdioProcess process = Start("crash-after-ack");

        await process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token);
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        string name = Path.GetFileName(FakeProviderProcess.ExecutablePath);
        RecordedEvent started = Assert.Single(_logger.Events, e => e.EventId.Name == "ProviderStarted");
        Assert.Equal(LogLevel.Information, started.Level);
        Assert.Equal(name, started.Fields["Executable"]);
        Assert.Equal(process.ProcessId, started.Fields["ProcessId"]);
        RecordedEvent exited = Assert.Single(_logger.Events, e => e.EventId.Name == "ProviderExited");
        Assert.Equal(LogLevel.Information, exited.Level);
        Assert.Equal(name, exited.Fields["Executable"]);
        Assert.Equal(process.ProcessId, exited.Fields["ProcessId"]);
        Assert.Equal(3, exited.Fields["ExitCode"]);
        Assert.IsType<double>(exited.Fields["ElapsedMs"]);
        Assert.DoesNotContain(_logger.Events, e => e.EventId.Name == "ProviderForcedTermination");
        Assert.False(exit.Forced);
    }

    private JsonRpcStdioProcess Start(string mode, TimeSpan? timeout = null, TimeSpan? grace = null) =>
        StartWith(["--mode", mode], timeout, grace);

    private JsonRpcStdioProcess StartWith(string[] arguments, TimeSpan? timeout = null, TimeSpan? grace = null)
    {
        JsonRpcStdioStartInfo startInfo = new(
            FakeProviderProcess.ExecutablePath,
            arguments,
            grace ?? TimeSpan.FromSeconds(30),
            new JsonRpcConnectionOptions(timeout ?? TimeSpan.FromSeconds(30), null, dropped => _dropped.Writer.TryWrite(dropped)));
        JsonRpcStdioProcess process = JsonRpcStdioProcess.Start(startInfo, _time, _logger);
        _started.Add(process);
        return process;
    }

    // The fake clock does not move by itself. Advance it until the task completes. A timer that
    // the code under test creates after an advance still fires on a later one.
    private async Task<T> AdvanceUntilAsync<T>(Task<T> task, TimeSpan step)
    {
        while (!task.IsCompleted)
        {
            _time.Advance(step);
            await Task.Delay(20, _token);
        }

        return await task;
    }

    // The fake provider writes one JSON object per line to stderr. The last one has the count.
    private static int RequestCount(JsonRpcStdioExit exit)
    {
        string last = exit.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        using JsonDocument document = JsonDocument.Parse(last);
        return document.RootElement.GetProperty("requests").GetInt32();
    }
}
