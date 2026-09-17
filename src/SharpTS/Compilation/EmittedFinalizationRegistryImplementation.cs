using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional FinalizationRegistryImplementation declarations for one compilation.</summary>
public sealed class EmittedFinalizationRegistryImplementation
{
    internal EmittedFinalizationRegistryImplementation() { }

    public bool IsComplete { get; private set; }

    private Type? _entryType;
    public Type EntryType
    {
        get => Require(_entryType);
        internal set => SetHandle(ref _entryType, value);
    }

    private ConstructorBuilder? _entryConstructor;
    public ConstructorBuilder EntryConstructor
    {
        get => Require(_entryConstructor);
        internal set => SetHandle(ref _entryConstructor, value);
    }

    private MethodBuilder? _suppressEntry;
    public MethodBuilder SuppressEntry
    {
        get => Require(_suppressEntry);
        internal set => SetHandle(ref _suppressEntry, value);
    }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _register;
    public MethodBuilder Register
    {
        get => Require(_register);
        internal set => SetHandle(ref _register, value);
    }

    private MethodBuilder? _unregister;
    public MethodBuilder Unregister
    {
        get => Require(_unregister);
        internal set => SetHandle(ref _unregister, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("FinalizationRegistryImplementation metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("FinalizationRegistryImplementation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = EntryType;
        _ = EntryConstructor;
        _ = SuppressEntry;
        _ = Create;
        _ = Register;
        _ = Unregister;
        IsComplete = true;
    }
}
