namespace Talokyn.Providers.JsonRpc;

/// <summary>A request ID: an integer or a string, as JSON-RPC 2.0 allows.</summary>
public readonly record struct JsonRpcId
{
    private JsonRpcId(long? number, string? text)
    {
        Number = number;
        Text = text;
    }

    /// <summary>The integer value, or null when the ID is a string.</summary>
    public long? Number { get; }

    /// <summary>The string value, or null when the ID is an integer.</summary>
    public string? Text { get; }

    public static JsonRpcId FromNumber(long number) => new(number, null);

    public static JsonRpcId FromText(string text) => new(null, text ?? throw new ArgumentNullException(nameof(text)));

    public override string ToString() => Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Text ?? string.Empty;
}
