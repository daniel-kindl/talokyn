using System.Text;
using System.Text.Json;
using Talokyn.Providers.JsonRpc;

namespace Talokyn.ContractTests.JsonRpc;

public sealed class JsonRpcFrameWriterTests
{
    [Fact]
    public async Task WritesFramesThatTheReaderReadsBack()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using JsonDocument parameters = JsonDocument.Parse("""{"text":"line one\nline two","n":[1,2]}""");
        using JsonDocument data = JsonDocument.Parse("""{"why":"x"}""");
        JsonRpcMessage[] messages =
        [
            new JsonRpcRequest(JsonRpcId.FromNumber(7), "turn/start", parameters.RootElement),
            new JsonRpcNotification("session/cancel", null),
            new JsonRpcResponse(JsonRpcId.FromText("a-1"), parameters.RootElement),
            new JsonRpcErrorResponse(JsonRpcId.FromNumber(8), new JsonRpcError(-32601, "no such method", data.RootElement)),
            new JsonRpcErrorResponse(null, new JsonRpcError(-32700, "parse error", null)),
        ];
        MemoryStream stream = new();
        JsonRpcFrameWriter writer = new(stream, JsonRpcFrameLimits.Default);

        foreach (JsonRpcMessage message in messages)
        {
            await writer.WriteAsync(message, cancellationToken);
        }

        string written = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal(messages.Length, written.Count(c => c == '\n'));
        Assert.EndsWith("\n", written, StringComparison.Ordinal);

        stream.Position = 0;
        JsonRpcFrameReader reader = new(stream, JsonRpcFrameLimits.Default);
        foreach (JsonRpcMessage expected in messages)
        {
            JsonRpcMessage? actual = await reader.ReadAsync(cancellationToken);
            AssertSameMessage(expected, actual);
        }

        Assert.Null(await reader.ReadAsync(cancellationToken));
    }

    [Fact]
    public async Task RefusesAFrameLargerThanTheLimit()
    {
        using JsonDocument parameters = JsonDocument.Parse($$"""{"p":"{{new string('x', 100)}}"}""");
        MemoryStream stream = new();
        JsonRpcFrameWriter writer = new(stream, new JsonRpcFrameLimits(MaxFrameBytes: 64, MaxDepth: 4));

        JsonRpcProtocolException exception = await Assert.ThrowsAsync<JsonRpcProtocolException>(
            async () => await writer.WriteAsync(new JsonRpcNotification("m", parameters.RootElement), TestContext.Current.CancellationToken));

        Assert.Equal(JsonRpcFrameError.FrameTooLarge, exception.Error);
        Assert.Equal(0, stream.Length);
    }

    private static void AssertSameMessage(JsonRpcMessage expected, JsonRpcMessage? actual)
    {
        switch (expected)
        {
            case JsonRpcRequest request:
                JsonRpcRequest actualRequest = Assert.IsType<JsonRpcRequest>(actual);
                Assert.Equal(request.Id, actualRequest.Id);
                Assert.Equal(request.Method, actualRequest.Method);
                AssertSameJson(request.Params, actualRequest.Params);
                break;
            case JsonRpcNotification notification:
                JsonRpcNotification actualNotification = Assert.IsType<JsonRpcNotification>(actual);
                Assert.Equal(notification.Method, actualNotification.Method);
                AssertSameJson(notification.Params, actualNotification.Params);
                break;
            case JsonRpcResponse response:
                JsonRpcResponse actualResponse = Assert.IsType<JsonRpcResponse>(actual);
                Assert.Equal(response.Id, actualResponse.Id);
                AssertSameJson(response.Result, actualResponse.Result);
                break;
            case JsonRpcErrorResponse error:
                JsonRpcErrorResponse actualError = Assert.IsType<JsonRpcErrorResponse>(actual);
                Assert.Equal(error.Id, actualError.Id);
                Assert.Equal(error.Error.Code, actualError.Error.Code);
                Assert.Equal(error.Error.Message, actualError.Error.Message);
                AssertSameJson(error.Error.Data, actualError.Error.Data);
                break;
            default:
                Assert.Fail($"Unexpected message type {expected.GetType().Name}.");
                break;
        }
    }

    private static void AssertSameJson(JsonElement? expected, JsonElement? actual)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue)
        {
            Assert.True(JsonElement.DeepEquals(expected.Value, actual!.Value), $"{expected} != {actual}");
        }
    }
}
