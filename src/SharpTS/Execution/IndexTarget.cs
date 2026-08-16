using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

/// <summary>
/// Discriminated union for resolved index access targets.
/// Centralizes (object, index) pair classification for EvaluateGetIndex/EvaluateSetIndex.
/// </summary>
public abstract record IndexTarget
{
    private IndexTarget() { }

    // Get/Set targets
    /// <summary>
    /// Array index target — <c>Index</c> is <c>long</c> so JS literals like
    /// <c>a[2147483648]</c> (which exceed int range but sit inside ECMA-262's
    /// uint32 array index range) route correctly through SharpTSArray's long APIs.
    /// </summary>
    public sealed record Array(SharpTSArray Target, long Index) : IndexTarget;
    /// <summary>
    /// Array property key that is not an array index (for example 2^32 - 1,
    /// a negative number, or a fractional number).
    /// </summary>
    public sealed record ArrayString(SharpTSArray Target, string Key) : IndexTarget;
    public sealed record ArraySymbol(SharpTSArray Target, SharpTSSymbol Key) : IndexTarget;
    public sealed record TypedArray(SharpTSTypedArray Target, int Index) : IndexTarget;
    public sealed record Buffer(SharpTSBuffer Target, int Index) : IndexTarget;
    public sealed record ObjectString(SharpTSObject Target, string Key) : IndexTarget;
    public sealed record ObjectSymbol(SharpTSObject Target, SharpTSSymbol Key) : IndexTarget;
    public sealed record InstanceString(SharpTSInstance Target, string Key) : IndexTarget;
    public sealed record InstanceSymbol(SharpTSInstance Target, SharpTSSymbol Key) : IndexTarget;
    public sealed record GlobalThis(SharpTSGlobalThis Target, string Key) : IndexTarget;
    /// <summary>
    /// Indexed write onto a built-in prototype singleton (<c>Object.prototype[1] = 1</c>).
    /// ECMA-262 makes these ordinary objects, so the write must land in their own-property
    /// storage and be visible to every object inheriting from them.
    /// </summary>
    public sealed record BuiltInPrototypeString(ISharpTSMutableBuiltIn Target, string Key) : IndexTarget;
    public sealed record HeadersString(SharpTSHeaders Target, string Key) : IndexTarget;
    /// <summary>
    /// Class constructor expando statics — Node allows arbitrary string/symbol-keyed
    /// statics on class objects (<c>(C as any)["foo"] = 1</c>, <c>(C as any)[Symbol.species] = P</c>).
    /// </summary>
    public sealed record ClassString(SharpTSClass Target, string Key) : IndexTarget;
    public sealed record ClassSymbol(SharpTSClass Target, SharpTSSymbol Key) : IndexTarget;

    // Get-only targets
    public sealed record EnumReverse(SharpTSEnum Target, double Index) : IndexTarget;
    public sealed record ConstEnumError(ConstEnumValues Target) : IndexTarget;
    public sealed record StringChar(string Target, int Index) : IndexTarget;

    // Fallback
    public sealed record Unsupported(object? Obj, object? Index) : IndexTarget;
}
