using System.Text.Encodings.Web;
using System.Text.Json;

// A deterministic stand-in for a provider CLI. It never contacts a network or a model.
// It reads newline-delimited messages on stdin. It counts each message that arrives and writes
// the count to stderr as one JSON object per line, so that a test can prove what reached it.
// The last object is written when the child exits on its own. Stdout carries only protocol frames.
string[] modes = ["normal", "slow", "hung", "partial", "malformed", "double-ack", "crash-before-ack", "crash-after-ack"];
if (args is not ["--mode", string mode] || !modes.Contains(mode))
{
    // The received arguments are on stderr, as one JSON array, so that a test can prove that each
    // argument arrived as one item.
    string received = JsonSerializer.Serialize(args, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    Console.Error.WriteLine("usage: Talokyn.FakeProvider --mode <normal|slow|hung|partial|malformed|double-ack|crash-before-ack|crash-after-ack>");
    Console.Error.WriteLine($$"""{"received":{{received}}}""");
    return 2;
}

int requests = 0;
while (await Console.In.ReadLineAsync() is string line)
{
    if (line.Length == 0)
    {
        continue;
    }

    requests++;
    Console.Error.WriteLine($$"""{"requests":{{requests}}}""");
    string? reply = ReplyFor(line);
    switch (mode)
    {
        case "normal" when reply is not null:
            Console.Out.WriteLine(reply);
            break;
        case "slow" when reply is not null:
            await Task.Delay(300);
            Console.Out.WriteLine(reply);
            break;
        case "partial":
            // Half a frame with no newline, then the child ends.
            Console.Out.Write("""{"jsonrpc":"2.0","id":""");
            Console.Out.Flush();
            return Exit(0);
        case "malformed":
            Console.Out.WriteLine("this is not json");
            break;
        case "double-ack" when reply is not null:
            Console.Out.WriteLine(reply);
            Console.Out.WriteLine(reply);
            break;
        case "crash-before-ack":
            return Exit(3);
        case "crash-after-ack":
            if (reply is not null)
            {
                Console.Out.WriteLine(reply);
            }

            return Exit(3);
    }
}

if (mode == "hung")
{
    // A hung child ignores the end of its input. Only a termination ends it.
    await Task.Delay(Timeout.Infinite);
}

return Exit(0);

int Exit(int code)
{
    Console.Error.WriteLine($$"""{"requests":{{requests}}}""");
    return code;
}

// A request has an id and a method. A notification has no id and gets no reply.
static string? ReplyFor(string line)
{
    using JsonDocument document = JsonDocument.Parse(line);
    JsonElement root = document.RootElement;
    if (!root.TryGetProperty("id", out JsonElement id) || !root.TryGetProperty("method", out JsonElement method))
    {
        return null;
    }

    return "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":{\"method\":" + JsonSerializer.Serialize(method.GetString()) + "}}";
}
