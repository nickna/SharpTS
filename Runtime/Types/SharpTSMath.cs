namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton marker for the JavaScript Math object.
/// </summary>
/// <remarks>
/// This class exists primarily as a type marker for <c>Math.method()</c> call resolution.
/// The actual Math methods (abs, floor, random, etc.) and constants (PI, E) are handled
/// as special cases in <see cref="Interpreter"/>. The singleton pattern ensures only one
/// Math object exists, consistent with JavaScript semantics.
///
/// Math is an extensible object per ECMA-262 — user code is allowed to add
/// its own properties (<c>Math.length = 1; Math[0] = v</c>). The extra
/// properties live in a small backing dictionary that takes precedence over
/// built-in members on read and is the only target for writes.
/// </remarks>
public class SharpTSMath
{
    public static readonly SharpTSMath Instance = new();
    private SharpTSMath() { }

    // Extra user-assigned properties — populated lazily on first write.
    // Small object population in practice (Test262 tests set a handful);
    // Dictionary keeps the common no-extras case zero-allocation.
    private Dictionary<string, object?>? _extras;

    /// <summary>
    /// Returns the user-assigned value for <paramref name="name"/> if one
    /// has been set, or null if the built-in dispatch should handle the read.
    /// </summary>
    public object? TryGetExtra(string name)
        => _extras is not null && _extras.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// True when a user-assigned property with this name exists.
    /// </summary>
    public bool HasExtra(string name)
        => _extras is not null && _extras.ContainsKey(name);

    /// <summary>
    /// Assigns a user property. Allowed per JS spec — Math is a regular
    /// extensible object.
    /// </summary>
    public void SetExtra(string name, object? value)
    {
        _extras ??= new Dictionary<string, object?>();
        _extras[name] = value;
    }

    /// <summary>
    /// The own enumerable properties of Math. All built-in members (abs, max,
    /// PI, …) are non-enumerable per ECMA-262, so only user-assigned extras
    /// appear here — empty in the common case. Backs Object.keys/values/entries.
    /// </summary>
    public IReadOnlyCollection<KeyValuePair<string, object?>> OwnEnumerableProperties
        => _extras ?? (IReadOnlyCollection<KeyValuePair<string, object?>>)Array.Empty<KeyValuePair<string, object?>>();

    public override string ToString() => "[object Math]";
}
