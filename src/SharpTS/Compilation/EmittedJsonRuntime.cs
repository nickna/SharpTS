using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required JSON namespace declarations and optional serialization helpers for one compilation.</summary>
public sealed class EmittedJsonRuntime
{
    internal EmittedJsonRuntime() { }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _singletonField;
    public FieldBuilder SingletonField
    {
        get => Require(_singletonField);
        internal set => SetHandle(ref _singletonField, value);
    }

    private MethodBuilder? _singletonPopulateMethod;
    public MethodBuilder SingletonPopulateMethod
    {
        get => Require(_singletonPopulateMethod);
        internal set => SetHandle(ref _singletonPopulateMethod, value);
    }

    public EmittedJsonImplementation? Implementation { get; private set; }

    public EmittedJsonImplementation RequireImplementation() => Implementation
        ?? throw new InvalidOperationException("JSON implementation was not enabled for this compilation.");

    internal void BeginImplementationEmission()
    {
        EnsureMutable();
        if (Implementation is not null)
            throw new InvalidOperationException("JSON implementation emission has already started.");
        Implementation = new EmittedJsonImplementation();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"JSON runtime metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("JSON runtime metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = SingletonField;
        _ = SingletonPopulateMethod;
        Implementation?.CompleteEmission();
        IsComplete = true;
    }
}
