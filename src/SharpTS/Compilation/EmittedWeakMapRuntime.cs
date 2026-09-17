using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional WeakMap declarations for one compilation.</summary>
public sealed class EmittedWeakMapRuntime
{
    internal EmittedWeakMapRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _get;
    public MethodBuilder Get
    {
        get => Require(_get);
        internal set => SetHandle(ref _get, value);
    }

    private MethodBuilder? _set;
    public MethodBuilder Set
    {
        get => Require(_set);
        internal set => SetHandle(ref _set, value);
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

    private MethodBuilder? _validateKey;
    public MethodBuilder ValidateKey
    {
        get => Require(_validateKey);
        internal set => SetHandle(ref _validateKey, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("WeakMap metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("WeakMap metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Create;
        _ = Get;
        _ = Set;
        _ = Has;
        _ = Delete;
        _ = ValidateKey;
        IsComplete = true;
    }
}
