using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional Performance declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedPerformanceRuntime
{
    internal EmittedPerformanceRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _now;
    public MethodBuilder Now
    {
        get => Require(_now);
        internal set => Set(ref _now, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Performance metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Performance metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Now;
        IsComplete = true;
    }
}
