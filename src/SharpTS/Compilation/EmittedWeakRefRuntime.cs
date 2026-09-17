using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional WeakRef declarations for one compilation.</summary>
public sealed class EmittedWeakRefRuntime
{
    internal EmittedWeakRefRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _dereference;
    public MethodBuilder Dereference
    {
        get => Require(_dereference);
        internal set => SetHandle(ref _dereference, value);
    }

    private MethodBuilder? _validateTarget;
    public MethodBuilder ValidateTarget
    {
        get => Require(_validateTarget);
        internal set => SetHandle(ref _validateTarget, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("WeakRef metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("WeakRef metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Create;
        _ = Dereference;
        _ = ValidateTarget;
        IsComplete = true;
    }
}
