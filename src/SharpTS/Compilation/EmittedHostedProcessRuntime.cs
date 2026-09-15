using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional hosted process lifecycle declarations for one compilation.
/// Declarations support forward references; completion validates every handle and freezes writes.
/// </summary>
public sealed class EmittedHostedProcessRuntime
{
    internal EmittedHostedProcessRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _emitBeforeExit;
    public MethodBuilder EmitBeforeExit
    {
        get => Require(_emitBeforeExit);
        internal set => Set(ref _emitBeforeExit, value);
    }

    private MethodBuilder? _emitExit;
    public MethodBuilder EmitExit
    {
        get => Require(_emitExit);
        internal set => Set(ref _emitExit, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Hosted process lifecycle metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Hosted process lifecycle metadata emission is already complete.");
    }

    internal void ValidateDeclarations()
    {
        _ = EmitBeforeExit;
        _ = EmitExit;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        ValidateDeclarations();
        IsComplete = true;
    }
}
