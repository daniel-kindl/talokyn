namespace Talokyn.Providers.JsonRpc;

/// <summary>The peer answered a request with an error object.</summary>
public sealed class JsonRpcRemoteException : Exception
{
    public JsonRpcRemoteException(string method, JsonRpcError error)
        : base($"The peer answered {method} with error {error?.Code}.")
    {
        ArgumentNullException.ThrowIfNull(error);
        Method = method;
        Error = error;
    }

    /// <summary>The method of the request.</summary>
    public string Method { get; }

    /// <summary>The error object of the reply.</summary>
    public JsonRpcError Error { get; }
}

/// <summary>A request got no reply within the request timeout.</summary>
public sealed class JsonRpcRequestTimeoutException : TimeoutException
{
    public JsonRpcRequestTimeoutException(string method, JsonRpcId id, TimeSpan timeout)
        : base($"The request {method} with ID {id} got no reply within {timeout}.")
    {
        Method = method;
        Id = id;
        Timeout = timeout;
    }

    /// <summary>The method of the request.</summary>
    public string Method { get; }

    /// <summary>The ID of the request.</summary>
    public JsonRpcId Id { get; }

    /// <summary>The request timeout that passed.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>
/// The connection ended before the request got a reply, or before the request was sent.
/// <see cref="Exception.InnerException"/> holds the <see cref="JsonRpcProtocolException"/>
/// when a rejected frame ended the connection.
/// </summary>
public sealed class JsonRpcConnectionClosedException : Exception
{
    public JsonRpcConnectionClosedException(string message)
        : base(message)
    {
    }

    public JsonRpcConnectionClosedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
