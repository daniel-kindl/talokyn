using System.Text.Json;
using System.Text.Unicode;

namespace Talokyn.Providers.JsonRpc;

/// <summary>Turns one frame into a message, or rejects it with a typed error.</summary>
internal static class JsonRpcMessageParser
{
    public static JsonRpcMessage Parse(ReadOnlyMemory<byte> frame, JsonRpcFrameLimits limits)
    {
        if (!Utf8.IsValid(frame.Span))
        {
            throw new JsonRpcProtocolException(JsonRpcFrameError.InvalidUtf8, "A frame is not valid UTF-8.");
        }

        CheckSyntaxAndDepth(frame.Span, limits.MaxDepth);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                frame,
                new JsonDocumentOptions { MaxDepth = limits.MaxDepth + 1, AllowDuplicateProperties = false });
        }
        catch (JsonException exception)
        {
            // The syntax and the depth passed, so the only rule left is duplicate names.
            throw new JsonRpcProtocolException(JsonRpcFrameError.DuplicateProperty, "An object has the same property name twice.", exception);
        }

        using (document)
        {
            return ToMessage(document.RootElement);
        }
    }

    internal static void CheckSyntaxAndDepth(ReadOnlySpan<byte> frame, int maxDepth)
    {
        // The reader allows one more level than the limit, so that this check reports the depth.
        Utf8JsonReader reader = new(frame, new JsonReaderOptions { MaxDepth = maxDepth + 1 });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && reader.CurrentDepth + 1 > maxDepth)
                {
                    throw new JsonRpcProtocolException(
                        JsonRpcFrameError.DepthExceeded,
                        $"A frame nests objects or arrays deeper than the limit of {maxDepth}.");
                }
            }
        }
        catch (JsonException exception)
        {
            throw new JsonRpcProtocolException(JsonRpcFrameError.InvalidJson, "A frame is not valid JSON.", exception);
        }
    }

    private static JsonRpcMessage ToMessage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("The message is not a JSON object.");
        }

        if (!root.TryGetProperty("jsonrpc", out JsonElement version)
            || version.ValueKind != JsonValueKind.String
            || !version.ValueEquals("2.0"u8))
        {
            throw Invalid("The message does not have \"jsonrpc\": \"2.0\".");
        }

        bool hasId = root.TryGetProperty("id", out JsonElement id);
        bool hasResult = root.TryGetProperty("result", out JsonElement result);
        bool hasError = root.TryGetProperty("error", out JsonElement error);

        if (root.TryGetProperty("method", out JsonElement method))
        {
            if (method.ValueKind != JsonValueKind.String)
            {
                throw Invalid("The method is not a string.");
            }

            if (hasResult || hasError)
            {
                throw Invalid("A call has a result or an error.");
            }

            JsonElement? parameters = ReadParams(root);
            string name = method.GetString()!;
            return hasId
                ? new JsonRpcRequest(ReadId(id) ?? throw Invalid("A request ID is null."), name, parameters)
                : new JsonRpcNotification(name, parameters);
        }

        if (!hasId)
        {
            throw Invalid("A response has no ID.");
        }

        if (hasResult == hasError)
        {
            throw Invalid("A response must have exactly one of result and error.");
        }

        return hasResult
            ? new JsonRpcResponse(ReadId(id) ?? throw Invalid("A successful response ID is null."), result.Clone())
            : new JsonRpcErrorResponse(ReadId(id), ReadError(error));
    }

    private static JsonElement? ReadParams(JsonElement root)
    {
        if (!root.TryGetProperty("params", out JsonElement parameters))
        {
            return null;
        }

        return parameters.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? parameters.Clone()
            : throw Invalid("The params are not an object or an array.");
    }

    private static JsonRpcId? ReadId(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String => JsonRpcId.FromText(id.GetString()!),
        JsonValueKind.Number when id.TryGetInt64(out long number) => JsonRpcId.FromNumber(number),
        JsonValueKind.Null => null,
        _ => throw Invalid("The ID is not an integer, a string or null."),
    };

    private static JsonRpcError ReadError(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out JsonElement code)
            || code.ValueKind != JsonValueKind.Number
            || !code.TryGetInt64(out long value)
            || !error.TryGetProperty("message", out JsonElement message)
            || message.ValueKind != JsonValueKind.String)
        {
            throw Invalid("The error does not have an integer code and a string message.");
        }

        JsonElement? data = error.TryGetProperty("data", out JsonElement element) ? element.Clone() : null;
        return new JsonRpcError(value, message.GetString()!, data);
    }

    private static JsonRpcProtocolException Invalid(string message) => new(JsonRpcFrameError.InvalidMessage, message);
}
