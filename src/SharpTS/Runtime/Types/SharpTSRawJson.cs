namespace SharpTS.Runtime.Types;

/// <summary>
/// The ordinary null-prototype object produced by <c>JSON.rawJSON</c>.
/// Its CLR type represents the spec's unforgeable [[IsRawJSON]] internal slot.
/// </summary>
public sealed class SharpTSRawJson : SharpTSObject
{
    public string RawText { get; }

    public SharpTSRawJson(string rawText)
        : base(new Dictionary<string, object?> { ["rawJSON"] = rawText })
    {
        RawText = rawText;
        IsNullPrototype = true;
        Freeze();
    }
}
