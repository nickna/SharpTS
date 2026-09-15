using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required MessageChannel declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedMessageChannelRuntime
{
    internal EmittedMessageChannelRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private ConstructorBuilder? _ctor;
    public ConstructorBuilder Ctor
    {
        get => Require(_ctor);
        internal set => Set(ref _ctor, value);
    }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => Set(ref _create, value);
    }

    public EmittedMessagePortRuntime Port { get; } = new();

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"MessageChannel metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("MessageChannel metadata emission is already complete.");
    }

    internal void ValidateDeclarations()
    {
        _ = Type;
        _ = Ctor;
        _ = Create;
        Port.ValidateDeclarations();
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        ValidateDeclarations();
        // Validate both owners before freezing either, so failed completion remains repairable.
        if (!Port.IsComplete) Port.CompleteEmission();
        IsComplete = true;
    }
}
