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
        Assert.Equal("GracePeriodExpired", warning.Fields["Reason"]);
    }

    [Fact]
    public async Task DisposeTerminatesALiveChildAndLogsThatItWasDisposed()
    {
        JsonRpcStdioProcess process = Start("hung");

        await process.DisposeAsync().AsTask().WaitAsync(_token);
        JsonRpcStdioExit exit = await process.Exited.WaitAsync(_token);

        Assert.True(exit.Forced);
        RecordedEvent warning = Assert.Single(_logger.Events, e => e.EventId.Name == "ProviderForcedTermination");
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("Disposed", warning.Fields["Reason"]);
    }

    [Fact]
    public async Task RefusesASendAfterStopClosedTheInputWithATypedError()
    {
        TimeSpan step = TimeSpan.FromSeconds(5);
        JsonRpcStdioProcess process = Start("hung", grace: step);

        // StopAsync closes the input before it first waits. The hung child stays alive for the grace period.
        Task<JsonRpcStdioExit> stop = process.StopAsync(_token);
        JsonRpcConnectionClosedException request = await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(
            () => process.Connection.SendRequestAsync("turn/start", null, _token).WaitAsync(_token));
        JsonRpcConnectionClosedException notification = await Assert.ThrowsAsync<JsonRpcConnectionClosedException>(
            () => process.Connection.SendNotificationAsync("turn/interrupt", null, _token).AsTask().WaitAsync(_token));
        JsonRpcStdioExit exit = await AdvanceUntilAsync(stop, step);

        Assert.NotNull(request.InnerException);
        Assert.NotNull(notification.InnerException);
        Assert.True(exit.Forced);
    }

    [Fact]
    public void RejectsInvalidSettingsBeforeItLaunchesTheChild()
    {
        // The executable does not exist. If the code launched the child first, it would throw a Win32Exception.
        JsonRpcConnectionOptions zeroTimeout = new(TimeSpan.Zero);
        JsonRpcConnectionOptions options = new(TimeSpan.FromSeconds(1));
        TimeSpan grace = TimeSpan.FromSeconds(1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => JsonRpcStdioProcess.Start(new JsonRpcStdioStartInfo("no-such-executable", [], grace, zeroTimeout), _time, _logger));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => JsonRpcStdioProcess.Start(new JsonRpcStdioStartInfo("no-such-executable", [], grace, options, new JsonRpcFrameLimits(0, 64)), _time, _logger));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => JsonRpcStdioProcess.Start(new JsonRpcStdioStartInfo("no-such-executable", [], grace, options, new JsonRpcFrameLimits(1024, 0)), _time, _logger));
    }

    [Fact]
    public async Task ReleasesAChildThatAGrandchildKeepsTheirPipesOpenFor()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The test uses /bin/sh.");
        // The shell exits at once. Its background sleep inherits the pipes and outlives the guard.
        JsonRpcStdioProcess process = StartExecutable("/bin/sh", ["-c", "sleep 40 & exit 0"]);

        JsonRpcStdioExit exit = await AdvanceUntilAsync(process.Exited, TimeSpan.FromSeconds(5));
        await process.DisposeAsync().AsTask().WaitAsync(_token);

        Assert.Equal(0, exit.ExitCode);
        Assert.False(exit.Forced);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.ConnectionCompleted.WaitAsync(_token));
    }

    [Fact]
    public async Task KeepsReadingStderrPastTheCapSoThatTheChildDoesNotBlock()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The test uses /bin/sh.");
        // 300000 bytes are more than the cap of 64 KiB and more than a pipe buffer. A child that
        // cannot write them all never exits.
        JsonRpcStdioProcess process = StartExecutable("/bin/sh", ["-c", "head -c 300000 /dev/zero | tr '\\0' x >&2"]);

        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal(0, exit.ExitCode);
        Assert.False(exit.Forced);
        Assert.Equal(64 * 1024, exit.Stderr.Length);
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
        // A shell, or a joined command line, would split this at the spaces and run the second
        // command. The fake provider takes it as one unknown mode, exits with the usage code, and
        // reports the arguments that it received.
        const string Mode = "normal && echo \"hacked\" ; $HOME";
        JsonRpcStdioProcess process = StartWith(["--mode", Mode]);

        await process.ConnectionCompleted.WaitAsync(_token);
        JsonRpcStdioExit exit = await process.StopAsync(_token);

        Assert.Equal(2, exit.ExitCode);
        string report = exit.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        using JsonDocument document = JsonDocument.Parse(report);
        Assert.Equal(["--mode", Mode], document.RootElement.GetProperty("received").EnumerateArray().Select(item => item.GetString()));
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

    private JsonRpcStdioProcess StartWith(string[] arguments, TimeSpan? timeout = null, TimeSpan? grace = null) =>
        StartExecutable(FakeProviderProcess.ExecutablePath, arguments, timeout, grace);

    private JsonRpcStdioProcess StartExecutable(string executable, string[] arguments, TimeSpan? timeout = null, TimeSpan? grace = null)
    {
        JsonRpcStdioStartInfo startInfo = new(
            executable,
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
