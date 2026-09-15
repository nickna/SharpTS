using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional cluster declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedClusterRuntime
{
    internal EmittedClusterRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _fork;
    public MethodBuilder Fork
    {
        get => Require(_fork);
        internal set => Set(ref _fork, value);
    }

    private MethodBuilder? _invoke;
    public MethodBuilder Invoke
    {
        get => Require(_invoke);
        internal set => Set(ref _invoke, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"cluster metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("cluster metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Fork;
        _ = Invoke;
        IsComplete = true;
    }
}
