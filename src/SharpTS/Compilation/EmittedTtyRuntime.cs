using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional TTY declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedTtyRuntime
{
    internal EmittedTtyRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _isatty;
    public MethodBuilder Isatty
    {
        get => Require(_isatty);
        internal set => Set(ref _isatty, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"TTY metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("TTY metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Isatty;
        IsComplete = true;
    }
}
