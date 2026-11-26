namespace Talokyn.Providers.JsonRpc;

/// <summary>Handles one notification from the peer.</summary>
/// <returns>
/// True when the handler accepts the notification. False when the notification is out of order
/// for the current state of the session. The connection then drops it with
/// <see cref="JsonRpcDropReason.OutOfOrderNotification"/>.
/// </returns>
public delegate ValueTask<bool> JsonRpcNotificationHandler(JsonRpcNotification notification, CancellationToken cancellationToken);

/// <summary>The settings of a <see cref="JsonRpcConnection"/>.</summary>
/// <param name="RequestTimeout">How long a request waits for its reply. The time comes from the injected <see cref="TimeProvider"/>.</param>
/// <param name="NotificationHandler">Receives each notification in arrival order. Null accepts and ignores each notification.</param>
/// <param name="MessageDropped">Receives each message that the connection drops. Null ignores them.</param>
public sealed record JsonRpcConnectionOptions(
    TimeSpan RequestTimeout,
    JsonRpcNotificationHandler? NotificationHandler = null,
    Action<JsonRpcDroppedMessage>? MessageDropped = null);

/// <summary>Why the connection dropped a message from the peer.</summary>
public enum JsonRpcDropReason
{
    /// <summary>A reply has an ID that the connection never sent, or a null ID.</summary>
    UnknownResponseId,

    /// <summary>
    /// A reply has an ID that the connection sent, but the request is no longer pending:
    /// it already got a reply, timed out or was cancelled.
    /// </summary>
    DuplicateResponse,

    /// <summary>The notification handler said that the notification is out of order.</summary>
    OutOfOrderNotification,
}

/// <summary>A message that the connection dropped. The connection keeps running.</summary>
/// <param name="Reason">Why the message was dropped.</param>
/// <param name="Id">The reply ID, or null for a notification or a reply with a null ID.</param>
/// <param name="Method">The notification method, or null for a reply.</param>
public sealed record JsonRpcDroppedMessage(JsonRpcDropReason Reason, JsonRpcId? Id, string? Method);
