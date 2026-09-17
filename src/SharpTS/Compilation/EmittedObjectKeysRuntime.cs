using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required own-property key enumeration and Proxy key callbacks for one compilation.</summary>
public sealed class EmittedObjectKeysRuntime
{
    internal EmittedObjectKeysRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _proxyCallbackBodiesEmitted;

    private MethodBuilder? _keys;
    public MethodBuilder Keys
    {
        get => Require(_keys);
        internal set => SetHandle(ref _keys, value);
    }

    private MethodBuilder? _normalize;
    public MethodBuilder Normalize
    {
        get => Require(_normalize);
        internal set => SetHandle(ref _normalize, value);
    }

    private MethodBuilder? _names;
    public MethodBuilder Names
    {
        get => Require(_names);
        internal set => SetHandle(ref _names, value);
    }

    private MethodBuilder? _ordinary;
    public MethodBuilder Ordinary
    {
        get => Require(_ordinary);
        internal set => SetHandle(ref _ordinary, value);
    }

    private MethodBuilder? _createProxyList;
    public MethodBuilder CreateProxyList
    {
        get => Require(_createProxyList);
        internal set => SetHandle(ref _createProxyList, value);
    }

    private MethodBuilder? _symbols;
    public MethodBuilder Symbols
    {
        get => Require(_symbols);
        internal set => SetHandle(ref _symbols, value);
    }

    internal void MarkProxyCallbackBodiesEmitted()
    {
        EnsureMutable();
        if (_proxyCallbackBodiesEmitted)
            throw new InvalidOperationException("Object key callback body emission is already complete.");
        _proxyCallbackBodiesEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Object key metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Object key metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Object key metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Keys;
        _ = Normalize;
        _ = Names;
        _ = Ordinary;
        _ = CreateProxyList;
        _ = Symbols;
        if (!_proxyCallbackBodiesEmitted)
            throw new InvalidOperationException("Object key callback bodies have not been emitted.");
        IsComplete = true;
    }
}
