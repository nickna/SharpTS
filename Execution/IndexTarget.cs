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
    public sealed record Array(SharpTSArray Target, int Index) : IndexTarget;
    public sealed record TypedArray(SharpTSTypedArray Target, int Index) : IndexTarget;
    public sealed record Buffer(SharpTSBuffer Target, int Index) : IndexTarget;
    public sealed record ObjectString(SharpTSObject Target, string Key) : IndexTarget;
    public sealed record ObjectSymbol(SharpTSObject Target, SharpTSSymbol Key) : IndexTarget;
    public sealed record InstanceString(SharpTSInstance Target, string Key) : IndexTarget;
    public sealed record InstanceSymbol(SharpTSInstance Target, SharpTSSymbol Key) : IndexTarget;
    public sealed record GlobalThis(SharpTSGlobalThis Target, string Key) : IndexTarget;

    // Get-only targets
    public sealed record EnumReverse(SharpTSEnum Target, double Index) : IndexTarget;
    public sealed record ConstEnumError(ConstEnumValues Target) : IndexTarget;

    // Fallback
    public sealed record Unsupported(object? Obj, object? Index) : IndexTarget;
}
