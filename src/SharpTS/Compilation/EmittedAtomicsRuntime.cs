using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional Atomics declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedAtomicsRuntime
{
    internal EmittedAtomicsRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _load;
    public MethodBuilder Load
    {
        get => Require(_load);
        internal set => Set(ref _load, value);
    }

    private MethodBuilder? _store;
    public MethodBuilder Store
    {
        get => Require(_store);
        internal set => Set(ref _store, value);
    }

    private MethodBuilder? _add;
    public MethodBuilder Add
    {
        get => Require(_add);
        internal set => Set(ref _add, value);
    }

    private MethodBuilder? _addInt32;
    public MethodBuilder AddInt32
    {
        get => Require(_addInt32);
        internal set => Set(ref _addInt32, value);
    }

    private MethodBuilder? _incrementInt32Discarded;
    public MethodBuilder IncrementInt32Discarded
    {
        get => Require(_incrementInt32Discarded);
        internal set => Set(ref _incrementInt32Discarded, value);
    }

    private MethodBuilder? _sub;
    public MethodBuilder Sub
    {
        get => Require(_sub);
        internal set => Set(ref _sub, value);
    }

    private MethodBuilder? _and;
    public MethodBuilder And
    {
        get => Require(_and);
        internal set => Set(ref _and, value);
    }

    private MethodBuilder? _or;
    public MethodBuilder Or
    {
        get => Require(_or);
        internal set => Set(ref _or, value);
    }

    private MethodBuilder? _xor;
    public MethodBuilder Xor
    {
        get => Require(_xor);
        internal set => Set(ref _xor, value);
    }

    private MethodBuilder? _exchange;
    public MethodBuilder Exchange
    {
        get => Require(_exchange);
        internal set => Set(ref _exchange, value);
    }

    private MethodBuilder? _compareExchange;
    public MethodBuilder CompareExchange
    {
        get => Require(_compareExchange);
        internal set => Set(ref _compareExchange, value);
    }

    private MethodBuilder? _wait;
    public MethodBuilder Wait
    {
        get => Require(_wait);
        internal set => Set(ref _wait, value);
    }

    private MethodBuilder? _notify;
    public MethodBuilder Notify
    {
        get => Require(_notify);
        internal set => Set(ref _notify, value);
    }

    private MethodBuilder? _isLockFree;
    public MethodBuilder IsLockFree
    {
        get => Require(_isLockFree);
        internal set => Set(ref _isLockFree, value);
    }

    private MethodBuilder? _pause;
    public MethodBuilder Pause
    {
        get => Require(_pause);
        internal set => Set(ref _pause, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Atomics metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Atomics metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Load;
        _ = Store;
        _ = Add;
        _ = AddInt32;
        _ = IncrementInt32Discarded;
        _ = Sub;
        _ = And;
        _ = Or;
        _ = Xor;
        _ = Exchange;
        _ = CompareExchange;
        _ = Wait;
        _ = Notify;
        _ = IsLockFree;
        _ = Pause;
        IsComplete = true;
    }
}
