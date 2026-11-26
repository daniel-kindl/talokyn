namespace Talokyn.Providers.JsonRpc;

/// <summary>The kind of rule that a frame broke.</summary>
public enum JsonRpcFrameError
{
    /// <summary>The frame is longer than the limit.</summary>
    FrameTooLarge,

    /// <summary>The stream ended in the middle of a frame.</summary>
    TruncatedFrame,

    /// <summary>The frame is not valid UTF-8.</summary>
    InvalidUtf8,

    /// <summary>The frame is not valid JSON.</summary>
    InvalidJson,

    /// <summary>The frame nests objects or arrays deeper than the limit.</summary>
    DepthExceeded,

    /// <summary>An object in the frame has the same property name twice.</summary>
    DuplicateProperty,

    /// <summary>The frame is valid JSON but not a valid JSON-RPC 2.0 message.</summary>
    InvalidMessage,
}

/// <summary>A peer broke the framing or the JSON-RPC 2.0 rules. The connection cannot continue.</summary>
public sealed class JsonRpcProtocolException : Exception
{
    public JsonRpcProtocolException(JsonRpcFrameError error, string message)
        : base(message)
    {
        Error = error;
    }

    public JsonRpcProtocolException(JsonRpcFrameError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    public JsonRpcFrameError Error { get; }
}
