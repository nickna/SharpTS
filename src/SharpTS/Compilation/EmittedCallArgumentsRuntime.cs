using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required argument-array pooling and spread-expansion metadata.</summary>
public sealed class EmittedCallArgumentsRuntime
{
    internal EmittedCallArgumentsRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _poolGet;
    /// <summary>
    /// $CallArgsPool.Get(int arity) — returns a thread-static object[]
    /// of the given arity (cached for arities 1..4, fresh allocation
    /// for ≥5). Used at method-call sites to avoid per-call newarr.
    /// </summary>
    public MethodBuilder PoolGet
    {
        get => Require(_poolGet);
        internal set => SetHandle(ref _poolGet, value);
    }

    private MethodBuilder? _expand;
    public MethodBuilder Expand
    {
        get => Require(_expand);
        internal set => SetHandle(ref _expand, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Call argument metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Call argument metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Call argument metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = PoolGet;
        _ = Expand;
        IsComplete = true;
    }
}
