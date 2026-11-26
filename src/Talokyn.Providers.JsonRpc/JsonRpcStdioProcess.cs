using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Talokyn.Providers.JsonRpc;

/// <summary>How to start a provider child process.</summary>
/// <param name="Executable">The path of the executable. The process starts without a shell.</param>
/// <param name="Arguments">Each argument as its own item. No item is parsed, split or expanded.</param>
/// <param name="StopGracePeriod">How long <see cref="JsonRpcStdioProcess.StopAsync"/> waits for the child to exit after it closes the child's input, before it terminates the child.</param>
/// <param name="Connection">The settings of the JSON-RPC connection to the child.</param>
/// <param name="Limits">The frame limits. Null uses <see cref="JsonRpcFrameLimits.Default"/>.</param>
/// <param name="WorkingDirectory">The working directory of the child. Null uses the one of the caller.</param>
public sealed record JsonRpcStdioStartInfo(
    string Executable,
    IReadOnlyList<string> Arguments,
    TimeSpan StopGracePeriod,
    JsonRpcConnectionOptions Connection,
    JsonRpcFrameLimits? Limits = null,
    string? WorkingDirectory = null);

/// <summary>How a child process ended.</summary>
/// <param name="ExitCode">The exit code of the child.</param>
/// <param name="Forced">True when the child was terminated, because it did not exit in time or was disposed.</param>
/// <param name="Elapsed">The time from the start of the child to its exit.</param>
/// <param name="Stderr">The standard error text of the child, cut at a fixed limit. It is not part of the protocol.</param>
public sealed record JsonRpcStdioExit(int ExitCode, bool Forced, TimeSpan Elapsed, string Stderr);

/// <summary>Runs a provider as a child process and speaks JSON-RPC 2.0 to it over its standard streams.</summary>
/// <remarks>
/// <para>
/// The child starts from an executable and an argument list, never through a shell. Its standard
/// output carries the protocol frames. Its standard error is kept apart and is never parsed.
/// </para>
/// <para>
/// <see cref="StopAsync"/> closes the input of the child and waits for the grace period on the
/// injected <see cref="TimeProvider"/>. A child that is still alive then is terminated, and the
/// exit says so. <see cref="DisposeAsync"/> terminates a live child at once. This type does not
/// supervise the process tree of the child.
/// </para>
/// </remarks>
public sealed partial class JsonRpcStdioProcess : IAsyncDisposable
{
    private const int MaxStderrChars = 64 * 1024;

    // After the child exits, a grandchild can still hold its pipes. Wait this long for the ends
    // of stderr and of the connection, then go on without them.
    private static readonly TimeSpan PipeDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly Process _process;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly TimeSpan _gracePeriod;
    private readonly string _executableName;
    private readonly long _startTimestamp;
    private readonly StringBuilder _stderr = new();
    private readonly CancellationTokenSource _runCancellation = new();
    private int _forced;
    private int _disposed;

    private JsonRpcStdioProcess(
        Process process,
        JsonRpcStdioStartInfo startInfo,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _process = process;
        _timeProvider = timeProvider;
        _logger = logger;
        _gracePeriod = startInfo.StopGracePeriod;
        _executableName = Path.GetFileName(startInfo.Executable);
        _startTimestamp = timeProvider.GetTimestamp();
        ProcessId = process.Id;

        JsonRpcFrameLimits limits = startInfo.Limits ?? JsonRpcFrameLimits.Default;
        Connection = new JsonRpcConnection(
            $"stdio-{ProcessId}",
            new JsonRpcFrameReader(process.StandardOutput.BaseStream, limits),
            new JsonRpcFrameWriter(process.StandardInput.BaseStream, limits),
            startInfo.Connection,
            timeProvider,
            logger);
        LogStarted(_executableName, ProcessId);

        Task stderrDone = Task.Run(() => DrainStderrAsync(process.StandardError));
        ConnectionCompleted = Task.Run(() => Connection.RunAsync(_runCancellation.Token));
        Exited = MonitorAsync(stderrDone);
    }

    /// <summary>The connection to the child.</summary>
    public JsonRpcConnection Connection { get; }

    /// <summary>The operating system process ID of the child.</summary>
    public int ProcessId { get; }

    /// <summary>Completes when the child has exited and its standard error is read.</summary>
    public Task<JsonRpcStdioExit> Exited { get; }

    /// <summary>
    /// Completes when the connection ends. It completes normally when the child closes its output
    /// between frames. It fails with the <see cref="JsonRpcProtocolException"/> when a frame is rejected.
    /// It fails with an <see cref="OperationCanceledException"/> when the child exited, but a
    /// grandchild still holds its output open after the pipe drain timeout of two seconds.
    /// </summary>
    public Task ConnectionCompleted { get; }

    /// <summary>Starts the child.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The executable cannot start.</exception>
    public static JsonRpcStdioProcess Start(JsonRpcStdioStartInfo startInfo, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrEmpty(startInfo.Executable);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(startInfo.StopGracePeriod, TimeSpan.Zero);

        // Check every setting before the child starts, so that a bad setting leaves no process behind.
        ArgumentNullException.ThrowIfNull(startInfo.Arguments);
        ArgumentNullException.ThrowIfNull(startInfo.Connection);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(startInfo.Connection.RequestTimeout, TimeSpan.Zero);
        if (startInfo.Limits is JsonRpcFrameLimits limits)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limits.MaxFrameBytes, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limits.MaxDepth, 0);
        }

        ProcessStartInfo processStartInfo = new(startInfo.Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in startInfo.Arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        if (startInfo.WorkingDirectory is not null)
        {
            processStartInfo.WorkingDirectory = startInfo.WorkingDirectory;
        }

        Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("The provider did not start.");
        return new JsonRpcStdioProcess(process, startInfo, timeProvider, logger);
    }

    /// <summary>Closes the input of the child, waits for the grace period, and terminates the child if it is still alive.</summary>
    /// <returns>How the child ended. Calling it again returns the same result.</returns>
    public async Task<JsonRpcStdioExit> StopAsync(CancellationToken cancellationToken)
    {
        CloseInput();
        using CancellationTokenSource grace = new(_gracePeriod, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, grace.Token);
        try
        {
            return await Exited.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Terminate("GracePeriodExpired");
        }

        return await Exited.WaitAsync(cancellationToken);
    }

    /// <summary>Terminates a live child at once and releases the process. Calling it again does nothing.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            Terminate("Disposed");
            await Exited;
            await _runCancellation.CancelAsync();

            // A cancelled read of a pipe does not end on each system. Do not wait for it for ever:
            // disposing the process below closes the pipe.
            if (await WaitForAsync(ConnectionCompleted))
            {
                await ConnectionCompleted;
            }
        }
        catch (Exception)
        {
            // Dispose does not throw. The caller had the chance to see the failure of the
            // connection in ConnectionCompleted.
        }
        finally
        {
            _process.Dispose();
            _runCancellation.Dispose();
        }
    }

    private void CloseInput()
    {
        try
        {
            _process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child already closed its end.
        }
    }

    private void Terminate(string reason)
    {
        try
        {
            if (_process.HasExited)
            {
                return;
            }

            Interlocked.Exchange(ref _forced, 1);
            LogForcedTermination(_executableName, ProcessId, reason);
            _process.Kill();
        }
        catch (InvalidOperationException)
        {
            // The child exited between the check and the kill.
        }
    }

    private async Task<JsonRpcStdioExit> MonitorAsync(Task stderrDone)
    {
        await Task.Yield();
        await _process.WaitForExitAsync(CancellationToken.None);

        // A grandchild can keep the pipes open after the child exits. Do not wait for it for ever.
        await WaitForAsync(stderrDone);
        if (!await WaitForAsync(ConnectionCompleted))
        {
            await _runCancellation.CancelAsync();
        }

        TimeSpan elapsed = _timeProvider.GetElapsedTime(_startTimestamp);
        LogExited(_executableName, ProcessId, _process.ExitCode, elapsed.TotalMilliseconds);
        string stderr;
        lock (_stderr)
        {
            stderr = _stderr.ToString();
        }

        return new JsonRpcStdioExit(_process.ExitCode, Volatile.Read(ref _forced) == 1, elapsed, stderr);
    }

    // Returns true when the task completed, with any outcome, within the pipe drain timeout.
    private async Task<bool> WaitForAsync(Task task)
    {
        using CancellationTokenSource timerCancellation = new();
        Task timer = Task.Delay(PipeDrainTimeout, _timeProvider, timerCancellation.Token);
        await Task.WhenAny(task, timer);
        await timerCancellation.CancelAsync();
        return task.IsCompleted;
    }

    // Read to the end, so that the child never blocks on a full pipe. Keep only the first part.
    private async Task DrainStderrAsync(StreamReader reader)
    {
        char[] buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            lock (_stderr)
            {
                int room = MaxStderrChars - _stderr.Length;
                if (room > 0)
                {
                    _stderr.Append(buffer, 0, Math.Min(read, room));
                }
            }
        }
    }

    [LoggerMessage(
        EventId = 10,
        EventName = "ProviderStarted",
        Level = LogLevel.Information,
        Message = "Started provider {Executable} with process ID {ProcessId}.")]
    private partial void LogStarted(string executable, int processId);

    [LoggerMessage(
        EventId = 11,
        EventName = "ProviderExited",
        Level = LogLevel.Information,
        Message = "Provider {Executable} with process ID {ProcessId} exited with code {ExitCode} after {ElapsedMs} ms.")]
    private partial void LogExited(string executable, int processId, int exitCode, double elapsedMs);

    [LoggerMessage(
        EventId = 12,
        EventName = "ProviderForcedTermination",
        Level = LogLevel.Warning,
        Message = "Terminating provider {Executable} with process ID {ProcessId}: {Reason}.")]
    private partial void LogForcedTermination(string executable, int processId, string reason);
}
