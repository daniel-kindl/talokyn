using System.Text.Json;

// A deterministic stand-in for a provider CLI. It never contacts a network or a model.
// It reads newline-delimited messages on stdin until stdin closes. Then it writes a JSON
// summary with the request count to stderr, so that a test can prove what reached it.
if (args is not ["--mode", "normal"])
{
    Console.Error.WriteLine("usage: Talokyn.FakeProvider --mode normal");
    return 2;
}

int requests = 0;
while (await Console.In.ReadLineAsync() is string line)
{
    if (line.Length > 0)
    {
        requests++;
    }
}

Console.Error.Write(JsonSerializer.Serialize(new { requests }));
return 0;
