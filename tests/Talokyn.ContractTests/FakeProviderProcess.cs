using System.Diagnostics;

namespace Talokyn.ContractTests;

/// <summary>Starts the fake provider executable that the build copies next to the tests.</summary>
internal static class FakeProviderProcess
{
    public static Process Start(string mode)
    {
        string name = OperatingSystem.IsWindows() ? "Talokyn.FakeProvider.exe" : "Talokyn.FakeProvider";
        ProcessStartInfo startInfo = new(Path.Combine(AppContext.BaseDirectory, name))
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
