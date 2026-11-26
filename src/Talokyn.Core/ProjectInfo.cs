namespace Talokyn.Core;

/// <summary>Identifies the unimplemented Talokyn bootstrap.</summary>
/// <remarks>
/// The values are properties, not constants. A caller then reads them at run
/// time, so the compiler does not fold a check of a value into dead code.
/// </remarks>
public static class ProjectInfo
{
    public static string Name { get; } = "Talokyn";

    public static string Version { get; } = "0.1.0";

    public static bool RuntimeImplemented { get; }
}
