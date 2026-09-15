using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required primitive-wrapper and default-hint conversion declarations for one compilation.</summary>
public sealed class EmittedBoxedPrimitiveRuntime
{
    internal EmittedBoxedPrimitiveRuntime() { }

    public bool IsComplete { get; private set; }

    // ToObject, UnwrapIfBoxed and IsOfType are declared before their bodies.
    private MethodBuilder? _new;
    /// <summary>$Runtime.NewBoxedPrimitive(typeTag, value) — builds a $Object wrapper with __primitiveType + __primitiveValue marker fields, prototype-linked to Boolean/Number/String prototype.</summary>
    public MethodBuilder New
    {
        get => Require(_new);
        internal set => Set(ref _new, value);
    }

    private MethodBuilder? _normalizeForeignEvalValue;
    /// <summary>$Runtime.NormalizeForeignEvalValue(value) — converts interpreter-side undefined and boxed primitive values returned across the eval boundary into their emitted-runtime representations.</summary>
    public MethodBuilder NormalizeForeignEvalValue
    {
        get => Require(_normalizeForeignEvalValue);
        internal set => Set(ref _normalizeForeignEvalValue, value);
    }

    private MethodBuilder? _toObject;
    /// <summary>$Runtime.ToObject(value) — creates an empty $Object for nullish values, boxes Boolean/Number/String/BigInt/Symbol primitives, and preserves object identity. Used by `new Object(v)`.</summary>
    public MethodBuilder ToObject
    {
        get => Require(_toObject);
        internal set => Set(ref _toObject, value);
    }

    private MethodBuilder? _isOfType;
    /// <summary>$Runtime.IsBoxedPrimitiveOfType(obj, typeTag) — true iff obj is a $Object with matching __primitiveType marker. Used by the instanceof emitter.</summary>
    public MethodBuilder IsOfType
    {
        get => Require(_isOfType);
        internal set => Set(ref _isOfType, value);
    }

    private MethodBuilder? _unwrapStringReceiver;
    /// <summary>$Runtime.UnwrapStringReceiver(object) -> string — coerces a String-method receiver to its underlying string. Fast-paths actual strings; unwraps Stage-4z19 boxed primitives ($Object with __primitiveType="String") to their __primitiveValue; otherwise falls back to ToJsString. Called by StringEmitter's direct dispatch prologue so `(new String("x")).charAt(...)` works once the new-String wrapper is enabled.</summary>
    public MethodBuilder UnwrapStringReceiver
    {
        get => Require(_unwrapStringReceiver);
        internal set => Set(ref _unwrapStringReceiver, value);
    }

    private MethodBuilder? _unwrapIfBoxed;
    /// <summary>$Runtime.UnwrapIfBoxed(obj) — default-hint primitive conversion, including observable hooks, wrapper values and Date's string preference. Used by addition and abstract equality.</summary>
    public MethodBuilder UnwrapIfBoxed
    {
        get => Require(_unwrapIfBoxed);
        internal set => Set(ref _unwrapIfBoxed, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"boxed primitive metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("boxed primitive metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = New;
        _ = NormalizeForeignEvalValue;
        _ = ToObject;
        _ = IsOfType;
        _ = UnwrapStringReceiver;
        _ = UnwrapIfBoxed;
        IsComplete = true;
    }
}
