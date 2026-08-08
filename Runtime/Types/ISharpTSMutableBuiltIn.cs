namespace SharpTS.Runtime.Types;

/// <summary>
/// A built-in object that ECMA-262 defines as *ordinary* and therefore mutable: the prototype
/// singletons (<c>Object.prototype</c>, <c>Array.prototype</c>, <c>String.prototype</c>,
/// <c>Number.prototype</c>, <c>Boolean.prototype</c>, <c>Function.prototype</c>, a class's
/// <c>prototype</c>) and the constructor/namespace objects (<c>Number</c>, <c>String</c>,
/// <c>Boolean</c>). Guest code can assign to these, index into them, define descriptors on
/// them, delete from them, and enumerate them — Test262 patches them constantly to exercise
/// inherited-property and read-only-slot paths.
/// <para>
/// Each implementor backs its guest-visible properties with a <see cref="SharpTSObject"/>;
/// this interface exposes that uniformly so the interpreter's generic property paths (property
/// assignment, index assignment, <c>for...in</c>) handle all of them at once instead of naming
/// each type in a switch — which is how <c>Object.prototype</c> came to support none of them
/// and <c>Number.prototype</c> to be missing from <c>for...in</c>.
/// </para>
/// </summary>
public interface ISharpTSMutableBuiltIn
{
    /// <summary>
    /// Assigns an own data property. Implementors that carry non-writable built-in slots
    /// (<c>Number.MAX_VALUE</c>) ignore a write to those, per ECMA-262 sloppy-mode
    /// assignment to a non-writable property.
    /// </summary>
    void SetExtra(string name, object? value);

    /// <summary>Own enumerable string keys — the <c>for...in</c> / <c>Object.keys</c> surface.</summary>
    IEnumerable<string> OwnEnumerableKeys();
}

/// <summary>
/// Symbol-keyed own-property surface for ordinary built-in objects whose
/// expando storage is backed by <see cref="SharpTSObject"/>. Keeping this
/// separate from <see cref="ISharpTSMutableBuiltIn"/> lets built-ins opt in as
/// their symbol semantics are implemented without weakening string-key support.
/// </summary>
internal interface ISharpTSSymbolPropertyBag
{
    bool HasSymbolProperty(SharpTSSymbol symbol);
    object? GetBySymbol(SharpTSSymbol symbol);
    bool TryGetSymbolAccessor(
        SharpTSSymbol symbol, out ISharpTSCallable? getter, out ISharpTSCallable? setter);
    void SetBySymbolStrict(SharpTSSymbol symbol, object? value, bool strictMode);
}
