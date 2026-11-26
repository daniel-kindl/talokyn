namespace Talokyn.Providers.JsonRpc;

/// <summary>Limits that the frame reader and writer apply to each frame.</summary>
/// <param name="MaxFrameBytes">The largest frame in bytes, without the newline.</param>
/// <param name="MaxDepth">The deepest nesting of objects and arrays. The message object is depth 1.</param>
public sealed record JsonRpcFrameLimits(int MaxFrameBytes, int MaxDepth)
{
    /// <summary>1 MiB frames and a nesting depth of 64.</summary>
    public static JsonRpcFrameLimits Default { get; } = new(1024 * 1024, 64);
}
