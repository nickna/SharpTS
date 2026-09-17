using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional WeakSet declarations for one compilation.</summary>
public sealed class EmittedWeakSetRuntime
{
    internal EmittedWeakSetRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _add;
    public MethodBuilder Add
    {
        get => Require(_add);
        internal set => SetHandle(ref _add, value);
    }

    private MethodBuilder? _has;
    public MethodBuilder Has
    {
        get => Require(_has);
        internal set => SetHandle(ref _has, value);
    }

    private MethodBuilder? _delete;
    public MethodBuilder Delete
    {
        get => Require(_delete);
        internal set => SetHandle(ref _delete, value);
    }

    private MethodBuilder? _validateValue;
    public MethodBuilder ValidateValue
    {
        get => Require(_validateValue);
        internal set => SetHandle(ref _validateValue, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("WeakSet metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("WeakSet metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Create;
        _ = Add;
        _ = Has;
        _ = Delete;
        _ = ValidateValue;
        IsComplete = true;
    }
}
