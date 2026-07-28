namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton marker for the JavaScript JSON object.
/// </summary>
/// <remarks>
/// Per ECMA-262 25.5: JSON is an ordinary object (not callable, not
/// constructable). Wiring this as a singleton lets bare references like
/// <c>var x = JSON</c> resolve, while <c>JSON()</c> and <c>new JSON()</c>
/// flow through the interpreter's "non-callable" / "non-constructable"
/// dispatch and surface the spec-mandated TypeError.
///
/// Method/property dispatch (<c>JSON.parse</c>, <c>JSON.stringify</c>) is
/// handled by <see cref="BuiltIns.JSONBuiltIns.GetStaticMethod"/> via the
/// registry's instance-type lookup.
/// </remarks>
public class SharpTSJSON
{
    public static readonly SharpTSJSON Instance = new();
    private Dictionary<string, object?>? _extras;
    internal SharpTSJSON() { }

    public bool HasExtra(string name) => _extras is not null && _extras.ContainsKey(name);
    public object? TryGetExtra(string name) =>
        _extras is not null && _extras.TryGetValue(name, out var value) ? value : null;
    public void SetExtra(string name, object? value)
    {
        _extras ??= new Dictionary<string, object?>();
        _extras[name] = value;
    }

    public override string ToString() => "[object JSON]";
}
