using System.Diagnostics;

namespace Talokyn.ContractTests;

/// <summary>Starts the fake provider executable that the build copies next to the tests.</summary>
internal static class FakeProviderProcess
{
    public static string ExecutablePath { get; } = Path.Combine(
        AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "Talokyn.FakeProvider.exe" : "Talokyn.FakeProvider");

    public static Process Start(string mode)
    {
        ProcessStartInfo startInfo = new(ExecutablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(mode);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The fake provider did not start.");
    }
}
