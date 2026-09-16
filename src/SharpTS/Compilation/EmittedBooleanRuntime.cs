using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required truthiness and Boolean prototype declarations for one compilation.</summary>
public sealed class EmittedBooleanRuntime
{
    internal EmittedBooleanRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _isTruthy;
    public MethodBuilder IsTruthy
    {
        get => Require(_isTruthy);
        internal set => Set(ref _isTruthy, value);
    }

    private FieldBuilder? _prototypeField;
    /// <summary>
    /// Boolean.prototype singleton — a Dictionary&lt;string, object&gt; that surfaces
    /// when user code does <c>Boolean.prototype[0] = …</c> or
    /// <c>Boolean.prototype.length = …</c>. Read by GetProperty's Type-receiver
    /// branch and by the array-like materializer for primitive bool receivers.
    /// </summary>
    public FieldBuilder PrototypeField
    {
        get => Require(_prototypeField);
        internal set => Set(ref _prototypeField, value);
    }

    private MethodBuilder? _prototypePopulateMethod;
    /// <summary>Populates <see cref="PrototypeField"/> with $TSFunction wrappers for toString/valueOf; idempotent.</summary>
    public MethodBuilder PrototypePopulateMethod
    {
        get => Require(_prototypePopulateMethod);
        internal set => Set(ref _prototypePopulateMethod, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Required Boolean metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Required Boolean metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = IsTruthy;
        _ = PrototypeField;
        _ = PrototypePopulateMethod;
        IsComplete = true;
    }
}
