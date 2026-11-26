using System.Diagnostics;
using System.Text.Json;

namespace Talokyn.ContractTests;

public sealed class FakeProviderTests
{
    [Fact]
    public async Task NormalModeExitsCleanlyWhenStdinClosesWithoutRequests()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using Process process = FakeProviderProcess.Start("normal");

        process.StandardInput.Close();
        string summary = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.Equal(0, process.ExitCode);
        using JsonDocument document = JsonDocument.Parse(summary);
        Assert.Equal(0, document.RootElement.GetProperty("requests").GetInt32());
    }

    [Fact]
    public async Task UnknownModeExitsWithUsageError()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using Process process = FakeProviderProcess.Start("no-such-mode");

        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);

        Assert.Equal(2, process.ExitCode);
    }
}
