namespace SharpTS.Runtime.Types;

/// <summary>
/// A built-in prototype singleton — <c>Object.prototype</c>, <c>Array.prototype</c>,
/// <c>String.prototype</c>, <c>Number.prototype</c>, <c>Boolean.prototype</c>,
/// <c>Function.prototype</c>. ECMA-262 makes each of these an *ordinary* object, so guest
/// code can assign to it, index into it, define descriptors on it, delete from it, and
/// enumerate it — Test262 patches these constantly to exercise inherited-property paths.
/// <para>
/// Each implementor already backs its own properties with a <see cref="SharpTSObject"/>;
/// this interface exposes that uniformly so the interpreter's generic property paths
/// (index assignment, <c>for...in</c>) can handle all six at once instead of naming each
/// type in a switch — which is how <c>Object.prototype</c> came to be the one that supported
/// none of them.
/// </para>
/// </summary>
public interface ISharpTSBuiltInPrototype
{
    /// <summary>Assigns an own data property, resurrecting a previously deleted built-in.</summary>
    void SetExtra(string name, object? value);

    /// <summary>Own enumerable string keys — the <c>for...in</c> / <c>Object.keys</c> surface.</summary>
    IEnumerable<string> OwnEnumerableKeys();
}
