using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required symbol accessor declarations for one compilation.</summary>
public sealed class EmittedSymbolAccessorRuntime
{
    internal EmittedSymbolAccessorRuntime() { }

    public bool IsComplete { get; private set; }

    private bool _initializerEmitted;
    private bool _bodiesEmitted;

    private FieldBuilder? _registry;
    public FieldBuilder Registry
    {
        get => Require(_registry);
        internal set => SetHandle(ref _registry, value);
    }

    private MethodBuilder? _registerAccessor;
    public MethodBuilder RegisterAccessor
    {
        get => Require(_registerAccessor);
        internal set => SetHandle(ref _registerAccessor, value);
    }

    private MethodBuilder? _findGetter;
    public MethodBuilder FindGetter
    {
        get => Require(_findGetter);
        internal set => SetHandle(ref _findGetter, value);
    }

    private MethodBuilder? _findSetter;
    public MethodBuilder FindSetter
    {
        get => Require(_findSetter);
        internal set => SetHandle(ref _findSetter, value);
    }

    private MethodBuilder? _registerMethod;
    public MethodBuilder RegisterMethod
    {
        get => Require(_registerMethod);
        internal set => SetHandle(ref _registerMethod, value);
    }

    private MethodBuilder? _findMethod;
    public MethodBuilder FindMethod
    {
        get => Require(_findMethod);
        internal set => SetHandle(ref _findMethod, value);
    }

    private MethodBuilder? _registryKey;
    public MethodBuilder RegistryKey
    {
        get => Require(_registryKey);
        internal set => SetHandle(ref _registryKey, value);
    }

    private MethodBuilder? _closedOwner;
    public MethodBuilder ClosedOwner
    {
        get => Require(_closedOwner);
        internal set => SetHandle(ref _closedOwner, value);
    }

    private MethodBuilder? _closeAccessor;
    public MethodBuilder CloseAccessor
    {
        get => Require(_closeAccessor);
        internal set => SetHandle(ref _closeAccessor, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Symbol accessor metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Symbol accessor metadata emission is already complete.");
    }

    internal void MarkInitializerEmitted()
    {
        EnsureMutable();
        if (_initializerEmitted)
            throw new InvalidOperationException("Symbol accessor initializer emission is already complete.");
        _initializerEmitted = true;
    }

    internal void MarkBodiesEmitted()
    {
        EnsureMutable();
        if (_bodiesEmitted)
            throw new InvalidOperationException("Symbol accessor bodies emission is already complete.");
        _bodiesEmitted = true;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Registry;
        _ = RegisterAccessor;
        _ = FindGetter;
        _ = FindSetter;
        _ = RegisterMethod;
        _ = FindMethod;
        _ = RegistryKey;
        _ = ClosedOwner;
        _ = CloseAccessor;
        if (!_initializerEmitted)
            throw new InvalidOperationException("Symbol accessor registry initialization has not been emitted.");
        if (!_bodiesEmitted)
            throw new InvalidOperationException("Symbol accessor method bodies have not been emitted.");
        IsComplete = true;
    }
}
