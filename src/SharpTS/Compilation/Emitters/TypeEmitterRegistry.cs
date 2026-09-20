using SharpTS.TypeSystem;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Registry that maps TypeInfo types to their corresponding emitter strategies.
/// Provides type-first dispatch for method calls and property access.
/// </summary>
public sealed class TypeEmitterRegistry
{
    private readonly Dictionary<Type, ITypeEmitterStrategy> _instanceStrategies = new();
    private readonly Dictionary<string, IStaticTypeEmitterStrategy> _staticStrategies = new(StringComparer.Ordinal);

    /// <summary>Whether registrations have been completed for this compiler's strategies.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Creates the complete default dispatch table with compiler-owned strategy instances.</summary>
    public static TypeEmitterRegistry CreateDefault()
    {
        var registry = new TypeEmitterRegistry();
        // Instance type emitters
        var stringEmitter = new StringEmitter();
        registry.Register<TypeInfo.String>(stringEmitter);
        registry.Register<TypeInfo.StringLiteral>(stringEmitter);
        registry.Register<TypeInfo.Array>(new ArrayEmitter());
        registry.Register<TypeInfo.Tuple>(new ArrayEmitter());
        registry.Register<TypeInfo.Buffer>(new BufferEmitter());
        registry.Register<TypeInfo.EventEmitter>(new EventEmitterEmitter());
        registry.Register<TypeInfo.Date>(new DateEmitter());
        registry.Register<TypeInfo.Map>(new MapEmitter());
        registry.Register<TypeInfo.Set>(new SetEmitter());
        registry.Register<TypeInfo.WeakMap>(new WeakMapEmitter());
        registry.Register<TypeInfo.WeakSet>(new WeakSetEmitter());
        registry.Register<TypeInfo.WeakRef>(new WeakRefEmitter());
        registry.Register<TypeInfo.FinalizationRegistry>(new FinalizationRegistryEmitter());
        registry.Register<TypeInfo.RegExp>(new RegExpEmitter());
        registry.Register<TypeInfo.AsyncGenerator>(new AsyncGeneratorEmitter());
        registry.Register<TypeInfo.Error>(new ErrorEmitter());
        registry.Register<TypeInfo.SharedArrayBuffer>(new SharedArrayBufferEmitter());
        registry.Register<TypeInfo.ArrayBuffer>(new ArrayBufferEmitter());
        registry.Register<TypeInfo.DataView>(new DataViewEmitter());
        registry.Register<TypeInfo.AbortController>(new AbortControllerEmitter());
        registry.Register<TypeInfo.AbortSignal>(new AbortSignalEmitter());
        var iteratorEmitter = new IteratorEmitter();
        registry.Register<TypeInfo.Iterator>(iteratorEmitter);
        registry.Register<TypeInfo.Generator>(iteratorEmitter);

        // Static type emitters
        registry.RegisterStatic("Math", new MathStaticEmitter());
        registry.RegisterStatic("JSON", new JSONStaticEmitter());
        registry.RegisterStatic("Object", new ObjectStaticEmitter());
        registry.RegisterStatic("Array", new ArrayStaticEmitter());
        registry.RegisterStatic("Buffer", new BufferStaticEmitter());
        registry.RegisterStatic("Number", new NumberStaticEmitter());
        registry.RegisterStatic("Promise", new PromiseStaticEmitter());
        registry.RegisterStatic("Error", new ErrorStaticEmitter());
        registry.RegisterStatic("Symbol", new SymbolStaticEmitter());
        registry.RegisterStatic("Map", new MapStaticEmitter());
        registry.RegisterStatic("String", new StringStaticEmitter());
        registry.RegisterStatic("Boolean", new BooleanStaticEmitter());
        registry.RegisterStatic("process", new ProcessStaticEmitter());
        registry.RegisterStatic("globalThis", new GlobalThisStaticEmitter(registry));
        registry.RegisterStatic("Atomics", new AtomicsStaticEmitter());
        registry.RegisterStatic("ArrayBuffer", new ArrayBufferStaticEmitter());
        registry.RegisterStatic("Reflect", new ReflectStaticEmitter());
        registry.RegisterStatic("Proxy", new ProxyStaticEmitter());
        registry.RegisterStatic("AbortSignal", new AbortSignalStaticEmitter());
        registry.RegisterStatic("Response", new ResponseStaticEmitter());
        registry.RegisterStatic("Iterator", new IteratorStaticEmitter());
        registry.RegisterStatic("RegExp", new RegExpStaticEmitter());
        registry.RegisterStatic("Date", new DateStaticEmitter());
        registry.RegisterStatic("ReadableStream", new ReadableStreamStaticEmitter());
        registry.CompleteRegistration();
        return registry;
    }

    /// <summary>Completes registration while preserving access to existing strategies.</summary>
    public void CompleteRegistration()
    {
        EnsureRegistering();
        IsComplete = true;
    }

    private void EnsureRegistering()
    {
        if (IsComplete)
            throw new InvalidOperationException("Type emitter registration is complete.");
    }

    /// <summary>Registers an instance strategy, rejecting null values, duplicate keys and late writes.</summary>
    public void Register<TTypeInfo>(ITypeEmitterStrategy strategy) where TTypeInfo : TypeInfo
    {
        EnsureRegistering();
        ArgumentNullException.ThrowIfNull(strategy);
        if (!_instanceStrategies.TryAdd(typeof(TTypeInfo), strategy))
            throw new InvalidOperationException($"Type emitter '{typeof(TTypeInfo).Name}' is already registered.");
    }

    /// <summary>Registers an ordinal static name, rejecting invalid values, duplicate keys and late writes.</summary>
    public void RegisterStatic(string typeName, IStaticTypeEmitterStrategy strategy)
    {
        EnsureRegistering();
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(strategy);
        if (!_staticStrategies.TryAdd(typeName, strategy))
            throw new InvalidOperationException($"Static type emitter '{typeName}' is already registered.");
    }

    /// <summary>Gets an instance strategy, or null when the receiver uses fallback dispatch.</summary>
    public ITypeEmitterStrategy? GetStrategy(TypeInfo typeInfo)
    {
        // User-defined and external class instances use the existing class dispatch paths.
        if (typeInfo is TypeInfo.Instance)
            return null;
        return _instanceStrategies.GetValueOrDefault(typeInfo.GetType());
    }

    /// <summary>Gets a static strategy, or null when no strategy is registered under that name.</summary>
    public IStaticTypeEmitterStrategy? GetStaticStrategy(string typeName) =>
        _staticStrategies.GetValueOrDefault(typeName);
}
