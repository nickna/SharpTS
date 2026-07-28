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
    /// <summary>
    /// Process-wide template instance. Retained for existence checks and as the
    /// BuiltInRegistry singleton (e.g. <c>"Math" in globalThis</c>), but guest
    /// reads of <c>Math</c> / <c>globalThis.Math</c> resolve to a per-realm
    /// instance (see <c>Interpreter.GetMath</c>) so user-added properties
    /// (<c>Math.x = …</c>) stay realm-local and don't race across worker
    /// threads. Mirrors the per-realm RegExp.prototype (#101).
    /// </summary>
    public static readonly SharpTSMath Instance = new();

    // internal (not private) so each Interpreter can construct its own realm
    // instance; the base built-in members are stateless, only _extras differs.
    internal SharpTSMath() { }

    // Math is an ordinary object. Descriptor-aware storage preserves the
    // writable/enumerable/configurable attributes of defineProperty expandos.
    private readonly SharpTSObject _extras = new([]);

    /// <summary>
    /// Returns the user-assigned value for <paramref name="name"/> if one
    /// has been set, or null if the built-in dispatch should handle the read.
    /// </summary>
    public object? TryGetExtra(string name)
        => _extras.GetProperty(name);

    /// <summary>
    /// True when a user-assigned property with this name exists.
    /// </summary>
    public bool HasExtra(string name)
        => _extras.HasProperty(name) || _extras.HasSetter(name);

    /// <summary>
    /// Assigns a user property. Allowed per JS spec — Math is a regular
    /// extensible object.
    /// </summary>
    public void SetExtra(string name, object? value) => _extras.SetProperty(name, value);
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _extras.DefineProperty(name, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public bool DeleteExtra(string name) => _extras.DeleteProperty(name);
    internal IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

    /// <summary>
    /// The own enumerable properties of Math. All built-in members (abs, max,
    /// PI, …) are non-enumerable per ECMA-262, so only user-assigned extras
    /// appear here — empty in the common case. Backs Object.keys/values/entries.
    /// </summary>
    public IEnumerable<KeyValuePair<string, object?>> OwnEnumerableProperties =>
        _extras.OwnEnumerableKeys().Select(
            key => new KeyValuePair<string, object?>(key, _extras.GetProperty(key)));

    public override string ToString() => "[object Math]";
}
