using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required structured clone and DataCloneError declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedStructuredCloneRuntime
{
    internal EmittedStructuredCloneRuntime() { }

    public bool IsComplete { get; private set; }

    // The exception is declared before $Runtime; Clone is reserved in phase 1 and filled by worker emission.
    private TypeBuilder? _errorType;
    public TypeBuilder ErrorType
    {
        get => Require(_errorType);
        internal set => Set(ref _errorType, value);
    }

    private ConstructorBuilder? _errorCtor;
    public ConstructorBuilder ErrorCtor
    {
        get => Require(_errorCtor);
        internal set => Set(ref _errorCtor, value);
    }

    private MethodBuilder? _clone;
    public MethodBuilder Clone
    {
        get => Require(_clone);
        internal set => Set(ref _clone, value);
    }

    private MethodBuilder? _guestClone;
    /// <summary>Actual JavaScript-global entry; bridge-compatible Clone keeps its existing protocol.</summary>
    public MethodBuilder GuestClone
    {
        get => Require(_guestClone);
        internal set => Set(ref _guestClone, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"structured clone metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("structured clone metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ErrorType;
        _ = ErrorCtor;
        _ = Clone;
        _ = GuestClone;
        IsComplete = true;
    }
}
