using System.Text.Json;

namespace Talokyn.Providers.JsonRpc;

/// <summary>A JSON-RPC 2.0 message. The four subtypes are the only kinds.</summary>
public abstract record JsonRpcMessage;

/// <summary>A call that expects a response with the same <see cref="Id"/>.</summary>
public sealed record JsonRpcRequest(JsonRpcId Id, string Method, JsonElement? Params) : JsonRpcMessage;

/// <summary>A call that has no ID and gets no response.</summary>
public sealed record JsonRpcNotification(string Method, JsonElement? Params) : JsonRpcMessage;

/// <summary>A successful reply to the request with the same <see cref="Id"/>.</summary>
public sealed record JsonRpcResponse(JsonRpcId Id, JsonElement Result) : JsonRpcMessage;

/// <summary>
/// A failed reply. <see cref="Id"/> is null only when the peer could not read the request ID.
/// </summary>
public sealed record JsonRpcErrorResponse(JsonRpcId? Id, JsonRpcError Error) : JsonRpcMessage;

/// <summary>The error object of a failed reply.</summary>
public sealed record JsonRpcError(long Code, string Message, JsonElement? Data);
