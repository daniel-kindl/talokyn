using System.Text;
using System.Text.Json;
using Talokyn.Providers.JsonRpc;

namespace Talokyn.ContractTests.JsonRpc;

public sealed class JsonRpcFrameReaderTests
{
    private static readonly JsonRpcFrameLimits SmallLimits = new(MaxFrameBytes: 64, MaxDepth: 4);

    [Fact]
    public async Task ReadsEachMessageKind()
    {
        JsonRpcFrameReader reader = ReaderFor(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"a":1}}""" + "\n" +
            """{"jsonrpc":"2.0","method":"session/cancel"}""" + "\n" +
            """{"jsonrpc":"2.0","id":"x","result":null}""" + "\n" +
            """{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"parse"}}""" + "\n",
            JsonRpcFrameLimits.Default);

        JsonRpcRequest request = Assert.IsType<JsonRpcRequest>(await ReadAsync(reader));
        Assert.Equal(JsonRpcId.FromNumber(1), request.Id);
        Assert.Equal("initialize", request.Method);
        Assert.Equal(1, request.Params!.Value.GetProperty("a").GetInt32());

        JsonRpcNotification notification = Assert.IsType<JsonRpcNotification>(await ReadAsync(reader));
        Assert.Equal("session/cancel", notification.Method);
        Assert.Null(notification.Params);

        JsonRpcResponse response = Assert.IsType<JsonRpcResponse>(await ReadAsync(reader));
        Assert.Equal(JsonRpcId.FromText("x"), response.Id);
        Assert.Equal(JsonValueKind.Null, response.Result.ValueKind);

        JsonRpcErrorResponse error = Assert.IsType<JsonRpcErrorResponse>(await ReadAsync(reader));
        Assert.Null(error.Id);
        Assert.Equal(-32700, error.Error.Code);
        Assert.Equal("parse", error.Error.Message);

        Assert.Null(await ReadAsync(reader));
    }

    [Fact]
    public async Task SkipsBlankLinesAndAcceptsCarriageReturns()
    {
        JsonRpcFrameReader reader = ReaderFor("\n  \r\n" + """{"jsonrpc":"2.0","method":"m"}""" + "\r\n", JsonRpcFrameLimits.Default);

        Assert.IsType<JsonRpcNotification>(await ReadAsync(reader));
        Assert.Null(await ReadAsync(reader));
    }

    [Fact]
    public async Task AcceptsAFrameOfExactlyTheMaximumLength()
    {
        string frame = PadTo("""{"jsonrpc":"2.0","method":"m","params":{"p":""}}""", SmallLimits.MaxFrameBytes);

        JsonRpcFrameReader reader = ReaderFor(frame + "\n", SmallLimits);

        Assert.IsType<JsonRpcNotification>(await ReadAsync(reader));
    }

    [Fact]
    public async Task RejectsAnOversizedFrameBeforeReadingAllOfIt()
    {
        CountingStream stream = new(new EndlessStream((byte)'a'));
        JsonRpcFrameReader reader = new(stream, SmallLimits);

        await AssertRejectedAsync(reader, JsonRpcFrameError.FrameTooLarge);
        Assert.True(stream.BytesRead <= 4096, $"The reader read {stream.BytesRead} bytes.");
    }

    [Fact]
    public async Task RejectsAnOversizedFrameThatEndsWithANewline()
    {
        string frame = PadTo("""{"jsonrpc":"2.0","method":"m","params":{"p":""}}""", SmallLimits.MaxFrameBytes + 1);

        await AssertRejectedAsync(ReaderFor(frame + "\n", SmallLimits), JsonRpcFrameError.FrameTooLarge);
    }

    [Fact]
    public async Task RejectsAFrameThatTheStreamCutsOff()
    {
        await AssertRejectedAsync(ReaderFor("""{"jsonrpc":"2.0","met""", JsonRpcFrameLimits.Default), JsonRpcFrameError.TruncatedFrame);
    }

    [Fact]
    public async Task RejectsInvalidUtf8()
    {
        byte[] prefix = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"m""");
        byte[] suffix = Encoding.UTF8.GetBytes("\"}\n");
        byte[] bytes = [.. prefix, 0xC3, 0x28, .. suffix];

        await AssertRejectedAsync(new JsonRpcFrameReader(new MemoryStream(bytes), JsonRpcFrameLimits.Default), JsonRpcFrameError.InvalidUtf8);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0",""")]
    [InlineData("""not json""")]
    [InlineData("""{"jsonrpc":"2.0","method":"m"} {}""")]
    public async Task RejectsInvalidJson(string frame)
    {
        await AssertRejectedAsync(ReaderFor(frame + "\n", JsonRpcFrameLimits.Default), JsonRpcFrameError.InvalidJson);
    }

    [Fact]
    public async Task AcceptsNestingAtTheDepthLimit()
    {
        JsonRpcFrameReader reader = ReaderFor("""{"jsonrpc":"2.0","method":"m","params":{"a":{"b":[]}}}""" + "\n", SmallLimits);

        Assert.IsType<JsonRpcNotification>(await ReadAsync(reader));
    }

    [Fact]
    public async Task RejectsNestingDeeperThanTheLimit()
    {
        JsonRpcFrameReader reader = ReaderFor("""{"jsonrpc":"2.0","method":"m","params":{"a":{"b":[[]]}}}""" + "\n", SmallLimits);

        await AssertRejectedAsync(reader, JsonRpcFrameError.DepthExceeded);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","method":"m","method":"n"}""")]
    [InlineData("""{"jsonrpc":"2.0","method":"m","params":{"a":1,"a":2}}""")]
    public async Task RejectsDuplicatePropertyNames(string frame)
    {
        await AssertRejectedAsync(ReaderFor(frame + "\n", JsonRpcFrameLimits.Default), JsonRpcFrameError.DuplicateProperty);
    }

    [Theory]
    [InlineData("""[{"jsonrpc":"2.0","method":"m"}]""")]
    [InlineData("""42""")]
    [InlineData("""{"method":"m"}""")]
    [InlineData("""{"jsonrpc":"1.0","method":"m"}""")]
    [InlineData("""{"jsonrpc":"2.0","method":7}""")]
    [InlineData("""{"jsonrpc":"2.0","id":true,"method":"m"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1.5,"method":"m"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":null,"method":"m"}""")]
    [InlineData("""{"jsonrpc":"2.0","method":"m","params":3}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":1,"error":{"code":1,"message":"x"}}""")]
    [InlineData("""{"jsonrpc":"2.0","result":1}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"error":{"code":"1","message":"x"}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"error":{"code":1}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"m","result":1}""")]
    public async Task RejectsJsonThatIsNotAJsonRpcMessage(string frame)
    {
        await AssertRejectedAsync(ReaderFor(frame + "\n", JsonRpcFrameLimits.Default), JsonRpcFrameError.InvalidMessage);
    }

    [Fact]
    public async Task StaysFaultedAfterARejectedFrame()
    {
        JsonRpcFrameReader reader = ReaderFor("not json\n" + """{"jsonrpc":"2.0","method":"m"}""" + "\n", JsonRpcFrameLimits.Default);

        await AssertRejectedAsync(reader, JsonRpcFrameError.InvalidJson);
        await AssertRejectedAsync(reader, JsonRpcFrameError.InvalidJson);
    }

    private static JsonRpcFrameReader ReaderFor(string text, JsonRpcFrameLimits limits) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(text)), limits);

    private static ValueTask<JsonRpcMessage?> ReadAsync(JsonRpcFrameReader reader) =>
        reader.ReadAsync(TestContext.Current.CancellationToken);

    private static async Task AssertRejectedAsync(JsonRpcFrameReader reader, JsonRpcFrameError expected)
    {
        JsonRpcProtocolException exception = await Assert.ThrowsAsync<JsonRpcProtocolException>(async () => await ReadAsync(reader));
        Assert.Equal(expected, exception.Error);
    }

    private static string PadTo(string frame, int length)
    {
        int insertAt = frame.IndexOf("\"\"", StringComparison.Ordinal) + 1;
        return frame.Insert(insertAt, new string('x', length - frame.Length));
    }
}
