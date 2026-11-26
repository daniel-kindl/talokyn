using System.Buffers;
using System.Text.Json;

namespace Talokyn.Providers.JsonRpc;

/// <summary>Writes JSON-RPC 2.0 messages to a stream, one frame per line.</summary>
/// <remarks>
/// The JSON is compact and escapes each newline in a string, so a frame never contains a
/// raw newline. The writer is not safe for concurrent calls.
/// </remarks>
public sealed class JsonRpcFrameWriter
{
    private readonly Stream _stream;
    private readonly JsonRpcFrameLimits _limits;

    public JsonRpcFrameWriter(Stream stream, JsonRpcFrameLimits limits)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(limits);
        _stream = stream;
        _limits = limits;
    }

    /// <summary>Writes one message and flushes the stream.</summary>
    /// <exception cref="JsonRpcProtocolException">The frame is longer than the limit. Nothing was written.</exception>
    public async ValueTask WriteAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            Write(json, message);
        }

        if (buffer.WrittenCount > _limits.MaxFrameBytes)
        {
            throw new JsonRpcProtocolException(
                JsonRpcFrameError.FrameTooLarge,
                $"A frame is longer than the limit of {_limits.MaxFrameBytes} bytes.");
        }

        buffer.Write("\n"u8);
        await _stream.WriteAsync(buffer.WrittenMemory, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private static void Write(Utf8JsonWriter json, JsonRpcMessage message)
    {
        json.WriteStartObject();
        json.WriteString("jsonrpc"u8, "2.0"u8);
        switch (message)
        {
            case JsonRpcRequest request:
                WriteId(json, request.Id);
                json.WriteString("method"u8, request.Method);
                WriteOptional(json, "params"u8, request.Params);
                break;
            case JsonRpcNotification notification:
                json.WriteString("method"u8, notification.Method);
                WriteOptional(json, "params"u8, notification.Params);
                break;
            case JsonRpcResponse response:
                WriteId(json, response.Id);
                json.WritePropertyName("result"u8);
                response.Result.WriteTo(json);
                break;
            case JsonRpcErrorResponse error:
                WriteId(json, error.Id);
                json.WriteStartObject("error"u8);
                json.WriteNumber("code"u8, error.Error.Code);
                json.WriteString("message"u8, error.Error.Message);
                WriteOptional(json, "data"u8, error.Error.Data);
                json.WriteEndObject();
                break;
            default:
                throw new ArgumentException($"Unknown message type {message.GetType().Name}.", nameof(message));
        }

        json.WriteEndObject();
    }

    private static void WriteId(Utf8JsonWriter json, JsonRpcId? id)
    {
        if (id is not JsonRpcId value)
        {
            json.WriteNull("id"u8);
        }
        else if (value.Number is long number)
        {
            json.WriteNumber("id"u8, number);
        }
        else
        {
            json.WriteString("id"u8, value.Text);
        }
    }

    private static void WriteOptional(Utf8JsonWriter json, ReadOnlySpan<byte> name, JsonElement? value)
    {
        if (value is JsonElement element)
        {
            json.WritePropertyName(name);
            element.WriteTo(json);
        }
    }
}
