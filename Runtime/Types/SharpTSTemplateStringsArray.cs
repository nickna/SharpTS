namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the template strings array passed to tag functions.
/// Implements TypeScript's TemplateStringsArray interface with a 'raw' property.
/// Both the main array and raw array are frozen (immutable).
/// </summary>
public class SharpTSTemplateStringsArray : SharpTSArray, ISharpTSPropertyAccessor
{
    private readonly SharpTSArray _rawStrings;

    public SharpTSTemplateStringsArray(List<object?> cookedStrings, List<string> rawStrings)
        : base(cookedStrings)
    {
        _rawStrings = new SharpTSArray(rawStrings.Cast<object?>().ToList());
        Freeze();
        _rawStrings.Freeze();
    }

    public SharpTSArray Raw => _rawStrings;

    /// <inheritdoc />
    public object? GetProperty(string name)
    {
        return name switch
        {
            "raw" => _rawStrings,
            "length" => (double)Elements.Count,
            _ => null
        };
    }

    /// <inheritdoc />
    public void SetProperty(string name, object? value)
    {
        // Template strings array is frozen, ignore writes
    }

    /// <inheritdoc />
    public bool HasProperty(string name) => name is "raw" or "length";

    /// <inheritdoc />
    public IEnumerable<string> PropertyNames => ["raw", "length"];

    public override string ToString()
    {
        var cookedStr = string.Join(", ", Elements.Select(e => e == null ? "undefined" : $"\"{e}\""));
        var rawStr = string.Join(", ", _rawStrings.Elements.Select(e => $"\"{e}\""));
        return $"[{cookedStr}] (raw: [{rawStr}])";
    }
}
