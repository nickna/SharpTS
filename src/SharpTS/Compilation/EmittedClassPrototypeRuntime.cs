using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required user-class prototype declarations and registration for one compilation.</summary>
public sealed class EmittedClassPrototypeRuntime
{
    internal EmittedClassPrototypeRuntime() { }
    public bool IsComplete { get; private set; }

    private Type? _markerType;
    public Type MarkerType
    {
        get => Require(_markerType);
        internal set => SetHandle(ref _markerType, value);
    }

    private MethodBuilder? _get;
    public MethodBuilder Get
    {
        get => Require(_get);
        internal set => SetHandle(ref _get, value);
    }

    private MethodBuilder? _register;
    public MethodBuilder Register
    {
        get => Require(_register);
        internal set => SetHandle(ref _register, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Prototype metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Prototype metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Prototype metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = MarkerType;
        _ = Get;
        _ = Register;
        IsComplete = true;
    }
}
