namespace SharpTS.Runtime.Types;

/// <summary>
/// Internal sentinel for an ECMA-262 array hole — an index <c>i</c> with <c>0 &le; i &lt; length</c>
/// that was never written. Distinct from <see cref="SharpTSUndefined"/>:
/// <code>
///   let a = [];
///   a[5] = "x";
///   // a[0..4] are holes (ArrayHole); a[5] is "x"
///
///   let b = [undefined, undefined, undefined, undefined, undefined, "x"];
///   // b[0..4] are SharpTSUndefined.Instance (explicit undefined)
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>User-facing reads return undefined.</b> Reading <c>a[0]</c> still produces
/// <c>undefined</c> at the language level (<c>a[0] === undefined</c> is <c>true</c>
/// for a hole). <see cref="SharpTSArray.Get(int)"/> converts holes to
/// <see cref="SharpTSUndefined"/>.<c>Instance</c> at the boundary so spread,
/// for-of, indexed access, and general flow see undefined.
/// </para>
/// <para>
/// <b>Built-in methods distinguish.</b> Array methods that need hole-awareness
/// (forEach skips; map preserves; indexOf skips; includes does not) iterate
/// with <see cref="SharpTSArray.GetRaw(int)"/> and check
/// <see cref="SharpTSArray.HasIndex(int)"/> before invoking callbacks or
/// deciding present-vs-absent.
/// </para>
/// <para>
/// Only the runtime interpreter and interpreter-side built-ins know about
/// this sentinel. It must never leak into user-visible state, serialization
/// (JSON.stringify renders holes as <c>null</c>), or typeof reporting (holes
/// are <c>"undefined"</c>).
/// </para>
/// </remarks>
public sealed class ArrayHole
{
    public static readonly ArrayHole Instance = new();
    private ArrayHole() { }
    public override string ToString() => "undefined";
}
