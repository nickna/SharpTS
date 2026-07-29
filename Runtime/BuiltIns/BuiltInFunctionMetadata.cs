namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// ECMA-262 §17: every built-in function object exposes <c>name</c> and <c>length</c> as own
/// data properties with <c>{ [[Writable]]: false, [[Enumerable]]: false, [[Configurable]]: true }</c>.
/// Configurable means guest code can <c>delete</c> them — Test262's <c>propertyHelper.js</c>
/// <c>isConfigurable()</c> proves configurability by deleting the property and re-checking
/// <c>hasOwnProperty</c>, so a wrapper that silently ignores the delete fails every
/// <c>&lt;method&gt;/length.js</c> and <c>&lt;method&gt;/name.js</c> test.
/// <para>
/// Implemented by each built-in callable wrapper (rather than folded into
/// <see cref="Types.ISharpTSCallable"/>) because plain guest functions carry real own-property
/// storage instead and must not route through this shim.
/// </para>
/// </summary>
public interface IBuiltInFunctionMetadata
{
    /// <summary>
    /// The value of this function object's <c>name</c> property — the spec name of the wrapped
    /// method (<c>"concat"</c>, <c>"toFixed"</c>, …), not the wrapper's C# type name.
    /// </summary>
    string FunctionName { get; }

    /// <summary>True when <paramref name="name"/> is still present as an own property.</summary>
    bool HasMetadataProperty(string name);

    /// <summary>
    /// Removes <paramref name="name"/> from the own properties. Returns true (the
    /// <c>[[Delete]]</c> result) for any key, including keys this shim does not track.
    /// </summary>
    bool DeleteMetadataProperty(string name);
}

/// <summary>
/// Mutable deleted-key set backing <see cref="IBuiltInFunctionMetadata"/>. Allocated lazily —
/// the overwhelmingly common case is that nothing is ever deleted.
/// </summary>
public sealed class BuiltInFunctionMetadata
{
    private HashSet<string>? _deleted;

    public bool Has(string name)
        => name is "name" or "length" && !(_deleted?.Contains(name) ?? false);

    public bool Delete(string name)
    {
        if (name is not ("name" or "length")) return true;
        _deleted ??= [];
        _deleted.Add(name);
        return true;
    }
}
