using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.TypeSystem;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a TypeScript class definition.
/// </summary>
/// <remarks>
/// Stores class metadata: name, superclass reference, instance methods, static methods,
/// static properties, and getter/setter accessors. Implements <see cref="ISharpTSCallable"/>
/// so that calling a class (e.g., <c>new MyClass()</c>) constructs a new <see cref="SharpTSInstance"/>.
/// Instance heritage is retained separately from the constructor's mutable prototype.
/// </remarks>
/// <seealso cref="SharpTSInstance"/>
/// <seealso cref="SharpTSFunction"/>
public partial class SharpTSClass(
    string name,
    SharpTSClass? superclass,
    Dictionary<string, ISharpTSCallable> methods,
    Dictionary<string, ISharpTSCallable> staticMethods,
    Dictionary<string, object?> staticProperties,
    Dictionary<string, SharpTSFunction>? getters = null,
    Dictionary<string, SharpTSFunction>? setters = null,
    bool isAbstract = false,
    List<Stmt.Field>? instanceFields = null,
    List<Stmt.Field>? instancePrivateFields = null,
    Dictionary<string, ISharpTSCallable>? privateMethods = null,
    Dictionary<string, object?>? staticPrivateFields = null,
    Dictionary<string, ISharpTSCallable>? staticPrivateMethods = null,
    List<Stmt.AutoAccessor>? instanceAutoAccessors = null,
    Dictionary<string, object?>? staticAutoAccessors = null,
    Dictionary<string, SharpTSFunction>? staticGetters = null,
    Dictionary<string, SharpTSFunction>? staticSetters = null) : ISharpTSCallable, ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Class;

    public string Name { get; } = name;
    private string? _inferredName;

    // Named evaluation changes the intrinsic guest metadata, not the class's
    // internal identity or any declared static member named "name".
    internal void SetInferredName(string? inferredName) => _inferredName = inferredName;

    public SharpTSClass? Superclass { get; } = superclass;
    internal bool HasExplicitPrototype { get; private set; }
    internal object? ExplicitPrototype { get; private set; }

    internal void SetExplicitPrototype(object? prototype)
    {
        ExplicitPrototype = prototype;
        HasExplicitPrototype = true;
    }

    internal bool CanReuseSuperAllocation(object? parent)
        => ReferenceEquals(parent, Superclass)
            || parent is SharpTSArrayGlobal && ReferenceEquals(Superclass, SharpTSArrayClass.ArrayBase)
            || parent is SharpTSBuiltInConstructor { Name: BuiltInNames.Promise }
                && ReferenceEquals(Superclass, SharpTSPromiseClass.PromiseBase);

    public bool IsAbstract { get; } = isAbstract;
    internal RuntimeEnvironment? InitializerEnvironment { get; set; }
    internal virtual bool RequiresNew => true;
    internal IReadOnlyList<Stmt.Field>? OrderedInstanceFields { get; set; }
    // Host-created classes have no source-order seed. Capture their own member
    // names before the lookup dictionaries are frozen; guest definitions replace
    // this fallback with their interleaved method/accessor order.
    internal IReadOnlyList<string> OwnInstancePropertyNames { get; } = methods.Keys
        .Concat(getters?.Keys.AsEnumerable() ?? [])
        .Concat(setters?.Keys.AsEnumerable() ?? [])
        .Concat(instanceAutoAccessors?.Select(accessor => accessor.Name.Lexeme) ?? [])
        .Distinct(StringComparer.Ordinal).ToArray();
    private readonly FrozenDictionary<string, ISharpTSCallable> _methods = methods.ToFrozenDictionary();
    private readonly FrozenDictionary<string, ISharpTSCallable> _staticMethods = staticMethods.ToFrozenDictionary();
    private readonly Dictionary<string, object?> _staticProperties = staticProperties;
    // Share field storage with the ordinary descriptor implementation. Allocate
    // descriptor metadata only when a class property is explicitly defined.
    private SharpTSObject? _staticPropertyObject;
    private readonly FrozenDictionary<string, SharpTSFunction> _getters = getters?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;
    private readonly FrozenDictionary<string, SharpTSFunction> _setters = setters?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;
    private readonly FrozenDictionary<string, SharpTSFunction> _staticGetters = staticGetters?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;
    private readonly FrozenDictionary<string, SharpTSFunction> _staticSetters = staticSetters?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;
    private readonly List<Stmt.Field> _instanceFields = instanceFields ?? [];

    /// <summary>Declared public instance fields, for subclasses that initialize them onto non-SharpTSInstance receivers (e.g. Array-backed instances).</summary>
    protected IReadOnlyList<Stmt.Field> InstanceFields => _instanceFields;

    // Method lookup cache - avoids repeated inheritance chain walks
    // Key: method name, Value: method (null means not found in entire chain)
    private readonly ConcurrentDictionary<string, ISharpTSCallable?> _methodCache = new();

    private SharpTSClassPrototype? _prototype;

    /// <summary>
    /// This class's <c>prototype</c> object. ECMA-262 gives every constructor exactly one,
    /// so it is created on first read and then reused — <c>TypeError.prototype ===
    /// TypeError.prototype</c> and <c>Object.getPrototypeOf(new TypeError()) ===
    /// TypeError.prototype</c> both depend on that identity holding.
    /// </summary>
    public SharpTSClassPrototype Prototype => _prototype ??= new SharpTSClassPrototype(this);

    // Cached instance lookups remain valid until a prototype's parent changes.
    // The original instance heritage remains fixed when constructor prototypes
    // change, so this only tracks mutations to the instance prototype chain.
    internal bool HasModifiedInstancePrototypeChain
        => _prototype?.HasExplicitPrototype == true
            || Superclass?.HasModifiedInstancePrototypeChain == true;
    private readonly ConcurrentDictionary<string, ISharpTSCallable?> _staticMethodCache = new();
    private readonly HashSet<string> _deletedStaticMethods = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SharpTSFunction?> _getterCache = new();
    private readonly ConcurrentDictionary<string, SharpTSFunction?> _setterCache = new();

    // ES2022 Private class elements
    // Instance private storage - ConditionalWeakTable for GC-friendly per-instance storage
    private readonly ConditionalWeakTable<object, Dictionary<string, object?>> _privateFieldStorage = new();
    private readonly List<Stmt.Field> _instancePrivateFields = instancePrivateFields ?? [];
    private readonly FrozenDictionary<string, ISharpTSCallable> _privateMethods = privateMethods?.ToFrozenDictionary() ?? FrozenDictionary<string, ISharpTSCallable>.Empty;
    private readonly Dictionary<string, object?> _staticPrivateFields = staticPrivateFields ?? [];
    private readonly HashSet<string> _installedStaticPrivateFields = new(StringComparer.Ordinal);
    private readonly FrozenDictionary<string, ISharpTSCallable> _staticPrivateMethods = staticPrivateMethods?.ToFrozenDictionary() ?? FrozenDictionary<string, ISharpTSCallable>.Empty;

    // Auto-accessor backing storage (TypeScript 4.9+)
    // Instance auto-accessor storage - ConditionalWeakTable for per-instance backing values
    private readonly ConditionalWeakTable<object, Dictionary<string, object?>> _autoAccessorStorage = new();
    private readonly List<Stmt.AutoAccessor> _instanceAutoAccessors = instanceAutoAccessors ?? [];
    // Static auto-accessor backing storage
    private readonly Dictionary<string, object?> _staticAutoAccessorStorage = staticAutoAccessors ?? [];

    public int Arity()
    {
        ISharpTSCallable? constructor = FindMethod("constructor");
        if (constructor != null) return constructor.Arity();
        return Superclass?.Arity() ?? 0;
    }

    // The callable ABI also constructs instances for `new`. Guest call paths
    // validate separately so construction can keep using that shared entry point.
    internal void ValidateCall()
    {
        if (RequiresNew)
            throw new ThrowException(new SharpTSTypeError($"Class constructor {Name} cannot be invoked without 'new'"));
    }

    public virtual object? Call(Interpreter interpreter, List<object?> arguments)
        => Construct(interpreter, arguments, this);

    internal virtual object? Construct(Interpreter interpreter, List<object?> arguments, object? newTarget)
        => ExecuteConstructorLevel(interpreter, AllocateInstance(interpreter), arguments, newTarget);

    internal virtual object AllocateInstance(Interpreter interpreter)
    {
        SharpTSInstance instance = new(this);
        instance.SetInterpreter(interpreter);
        return instance;
    }

    internal object? ExecuteConstructorLevel(
        Interpreter interpreter, object allocation, List<object?> arguments, object? newTarget)
    {
        if (ReferenceEquals(this, SharpTSArrayClass.ArrayBase)
            && allocation is SharpTSArraySubclassInstance array
            && !ReferenceEquals(newTarget, array.Klass))
            array.SetExplicitPrototype(interpreter.GetConstructorPrototype(newTarget));
        if (TryExecuteNativeConstructor(interpreter, allocation, arguments)) return allocation;

        var frame = new ClassConstructionFrame(this, allocation, newTarget);
        var constructor = FindOwnMethod("constructor");
        if (Superclass is null)
        {
            // OrdinaryCreateFromConstructor belongs to the base constructor's
            // entry, after any derived body code preceding super(). A derived
            // constructor that returns an object without super never observes
            // newTarget.prototype.
            if (allocation is SharpTSInstance instance
                && !ReferenceEquals(newTarget, instance.RuntimeClass))
                instance.SetExplicitPrototype(interpreter.GetConstructorPrototype(newTarget));
            frame.InitializeOwnElements(interpreter);
            return constructor is null ? allocation
                : ResolveOwnConstructorReturn(
                    InvokeConstructorBody(interpreter, constructor, allocation, arguments, newTarget, frame), frame);
        }

        if (constructor is null)
        {
            // An implicit derived constructor is still a class level: forward
            // first, then install only this owner's elements, preserving the
            // ancestor completion selected by existing constructor-return rules.
            object? parent = HasExplicitPrototype ? ObjectBuiltIns.PrototypeOf(interpreter, this) : Superclass;
            var parentResult = ExecuteSuperConstructor(
                interpreter, parent, allocation, arguments, newTarget, CanReuseSuperAllocation(parent));
            return frame.CompleteSuper(interpreter, parentResult);
        }

        return ResolveOwnConstructorReturn(
            InvokeConstructorBody(interpreter, constructor, allocation, arguments, newTarget, frame), frame);
    }

    internal static object? ExecuteSuperConstructor(
        Interpreter interpreter, object? parent, object allocation, List<object?> arguments, object? newTarget,
        bool reuseAllocation)
    {
        // A changed parent can select a different native backing (or an ordinary
        // object), so its own [[Construct]] must choose the allocation.
        if (parent is SharpTSArrayGlobal)
            parent = SharpTSArrayClass.ArrayBase;
        if (reuseAllocation && parent is SharpTSBuiltInConstructor { Name: BuiltInNames.Promise })
            parent = SharpTSPromiseClass.PromiseBase;
        // Super executes the selected class with the existing concrete newTarget;
        // it does not directly instantiate an abstract superclass.
        if (parent is SharpTSClass klass)
            return reuseAllocation
                ? klass.ExecuteConstructorLevel(interpreter, allocation, arguments, newTarget)
                : klass.Construct(interpreter, arguments, newTarget);
        return interpreter.Construct(parent, arguments, newTarget);
    }

    // Built-in-backed bridge roots initialize the existing native allocation;
    // user subclasses continue through the ordinary per-owner executor above.
    protected virtual bool TryExecuteNativeConstructor(
        Interpreter interpreter, object allocation, List<object?> arguments) => false;

    private object? ResolveOwnConstructorReturn(object? result, ClassConstructionFrame frame)
    {
        if (Interpreter.IsConstructorReturnObject(result)) return result;
        if (Superclass is not null && result is not SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError("Derived constructors may only return object or undefined"));
        frame.EnsureThisBound();
        return frame.Receiver;
    }

    internal static object? InvokeConstructorBody(
        Interpreter interpreter, ISharpTSCallable constructor, object receiver,
        List<object?> arguments, object? newTarget, ClassConstructionFrame? constructionFrame = null)
    {
        var bound = BindMethodToReceiver(constructor, receiver);
        return bound is SharpTSFunction function
            ? function.CallConstructed(interpreter, arguments, newTarget, constructionFrame)
            : bound.Call(interpreter, arguments);
    }

    protected object? ResolveConstructorReturn(object? result, object receiver)
    {
        if (Interpreter.IsConstructorReturnObject(result)) return result;
        // An implicit derived constructor forwards the ancestor's completion;
        // primitive handling follows the constructor that actually ran.
        var owner = this;
        while (!owner._methods.ContainsKey("constructor") && owner.Superclass != null)
            owner = owner.Superclass;
        if (owner.Superclass != null && result is not SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError("Derived constructors may only return object or undefined"));
        return receiver;
    }

    /// <summary>
    /// Binds a method to an instance, handling both sync and async function types.
    /// </summary>
    public static ISharpTSCallable BindMethod(ISharpTSCallable method, SharpTSInstance instance)
    {
        return method switch
        {
            SharpTSFunction func => func.Bind(instance),
            SharpTSAsyncFunction asyncFunc => asyncFunc.Bind(instance),
            SharpTSGeneratorFunction genFunc => genFunc.Bind(instance),
            SharpTSAsyncGeneratorFunction asyncGenFunc => asyncGenFunc.Bind(instance),
            IInstanceBindable bindable => bindable.BindTo(instance),
            _ => method // For other callables that don't need binding
        };
    }

    /// <summary>
    /// Binds a method to an arbitrary receiver. Receivers that are
    /// <see cref="SharpTSInstance"/> route through <see cref="BindMethod"/>;
    /// Other guest objects retain the same class receiver scope, including
    /// replacement objects returned by a parent constructor.
    /// </summary>
    public static ISharpTSCallable BindMethodToReceiver(ISharpTSCallable method, object receiver)
    {
        if (receiver is SharpTSInstance instance)
            return BindMethod(method, instance);
        return method switch
        {
            SharpTSFunction func => func.BindInstanceReceiver(receiver),
            SharpTSAsyncFunction asyncFunc => asyncFunc.BindThisValue(receiver),
            IReceiverBindable bindable => bindable.BindToReceiver(receiver),
            _ => method
        };
    }

    /// <summary>
    /// Binds a static method to its class (so <c>this</c> is the class), handling sync,
    /// async, generator and async-generator forms. Mirrors <see cref="BindMethod"/>.
    /// </summary>
    public static ISharpTSCallable BindStaticMethod(ISharpTSCallable method, SharpTSClass klass)
    {
        return method switch
        {
            SharpTSFunction func => func.BindStatic(klass),
            SharpTSAsyncFunction asyncFunc => asyncFunc.BindStatic(klass),
            SharpTSGeneratorFunction genFunc => genFunc.BindStatic(klass),
            SharpTSAsyncGeneratorFunction asyncGenFunc => asyncGenFunc.BindStatic(klass),
            _ => method
        };
    }

    protected void InitializeInstanceFields(Interpreter interpreter, SharpTSInstance instance)
    {
        // First initialize superclass fields
        Superclass?.InitializeInstanceFields(interpreter, instance);

        // Then initialize this class's fields
        foreach (var field in _instanceFields)
        {
            object? value = field.Initializer != null
                ? interpreter.EvaluateClassInitializer(this, instance, field.Initializer)
                : SharpTSUndefined.Instance;

            // Check if this is a computed property name
            if (field.ComputedKey != null)
            {
                object? key = interpreter.EvaluateClassInitializer(this, instance, field.ComputedKey);
                if (key is SharpTSSymbol symbol)
                {
                    // Symbol key - store in symbol fields
                    instance.SetBySymbol(symbol, value);
                }
                else
                {
                    // String key - store as regular field
                    string keyStr = key?.ToString() ?? "undefined";
                    instance.SetRawField(keyStr, value);
                }
            }
            else
            {
                // Regular field
                instance.SetRawField(field.Name.Lexeme, value);
            }
        }
    }

    internal void InitializeOwnInstanceElements(Interpreter interpreter, object receiver)
    {
        Dictionary<string, object?>? privateFields = null;
        if (_privateMethods.Count != 0 || _instancePrivateFields.Count != 0)
        {
            if (!_privateFieldStorage.TryGetValue(receiver, out privateFields))
            {
                privateFields = [];
                _privateFieldStorage.Add(receiver, privateFields);
            }
            else if (_privateMethods.Count != 0)
                throw new ThrowException(new SharpTSTypeError("Cannot initialize private methods twice on the same object"));
        }

        foreach (var field in OrderedInstanceFields ?? _instanceFields.Concat(_instancePrivateFields).ToList())
        {
            object? value = field.Initializer != null
                ? interpreter.EvaluateClassInitializer(this, receiver, field.Initializer)
                : SharpTSUndefined.Instance;
            if (field.IsPrivate)
            {
                if (!privateFields!.TryAdd(field.Name.Lexeme, value))
                    throw new ThrowException(new SharpTSTypeError($"Cannot initialize private field {field.Name.Lexeme} twice on the same object"));
            }
            else
            {
                var key = field.ComputedKey is { } computedKey
                    ? interpreter.EvaluateClassInitializer(this, receiver, computedKey) : field.Name.Lexeme;
                ObjectBuiltIns.CreateDataPropertyOrThrow(interpreter, receiver, key, value);
            }
        }
        InitializeOwnAutoAccessors(interpreter, receiver);
    }

    internal ISharpTSCallable? FindOwnMethod(string name)
        => _methods.GetValueOrDefault(name);

    public ISharpTSCallable? FindMethod(string name)
    {
        // Check cache first
        if (_methodCache.TryGetValue(name, out ISharpTSCallable? cached))
        {
            return cached;
        }

        // Look up in this class's methods
        if (_methods.TryGetValue(name, out ISharpTSCallable? method))
        {
            _methodCache[name] = method;
            return method;
        }

        // Look up in superclass chain
        var result = Superclass?.FindMethod(name);
        _methodCache[name] = result;
        return result;
    }

    public ISharpTSCallable? FindStaticMethod(string name)
    {
        // Check cache first
        if (_staticMethodCache.TryGetValue(name, out ISharpTSCallable? cached))
        {
            return cached;
        }

        // Look up in this class's static methods
        if (!_deletedStaticMethods.Contains(name)
            && _staticMethods.TryGetValue(name, out ISharpTSCallable? method))
        {
            _staticMethodCache[name] = method;
            return method;
        }

        // Look up in superclass chain
        var result = Superclass?.FindStaticMethod(name);
        _staticMethodCache[name] = result;
        return result;
    }

    public object? GetStaticProperty(string name)
    {
        if (name == "prototype") return Prototype;
        if (_staticProperties.TryGetValue(name, out var value))
        {
            return value;
        }

        return Superclass?.GetStaticProperty(name);
    }

    public bool HasStaticProperty(string name)
    {
        if (name == "prototype") return true;
        if (_staticProperties.ContainsKey(name))
        {
            return true;
        }

        return Superclass?.HasStaticProperty(name) ?? false;
    }

    public void SetStaticProperty(string name, object? value, bool strictMode = false)
    {
        if (GetOwnStaticPropertyDescriptor(name) is { HasWritable: true, Writable: false }
            || !Compilation.PropertyDescriptorStore.IsWritable(this, name))
        {
            if (strictMode)
                throw StrictModeErrors.TypeError($"Cannot assign to read only property '{name}'");
            return;
        }
        if (!HasOwnStaticMember(name) && !Compilation.PropertyDescriptorStore.IsExtensible(this))
        {
            if (strictMode)
                throw StrictModeErrors.TypeError($"Cannot add property '{name}' to a non-extensible object");
            return;
        }
        if (_staticMethods.ContainsKey(name))
            _deletedStaticMethods.Add(name);
        _staticMethodCache.TryRemove(name, out _);
        if (_staticPropertyObject is not null)
            _staticPropertyObject.SetPropertyStrict(name, value, strictMode);
        else
            _staticProperties[name] = value;
        if (HasOwnStaticMember(name)) RecordOwnStaticKey(name);
    }

    internal void InitializeStaticProperty(string name, object? value)
    {
        // Field definitions replace configurable intrinsic metadata instead of
        // assigning through its read-only data descriptor.
        if (!DefineStaticProperty(name, new SharpTSPropertyDescriptor(value,
                writable: true, enumerable: true, configurable: true) { HasValue = true }))
            throw StrictModeErrors.TypeError($"Cannot define property '{name}' on class constructor");
    }

    internal SharpTSPropertyDescriptor? GetOwnStaticPropertyDescriptor(string name)
    {
        MaterializeStaticAccessors();
        if (name == "prototype")
            return new SharpTSPropertyDescriptor(Prototype, writable: false, enumerable: false, configurable: false)
                { HasValue = true };
        if (_staticPropertyObject?.GetOwnPropertyDescriptor(name) is { } descriptor)
            return descriptor;
        if (_staticProperties.TryGetValue(name, out var value))
            return new SharpTSPropertyDescriptor(value, writable: true, enumerable: true, configurable: true)
                { HasValue = true };
        if (_deletedStaticMethods.Contains(name)) return null;
        if (_staticMethods.TryGetValue(name, out var method))
            return SharpTSPropertyDescriptor.ForMethod(method);
        // Keep intrinsic metadata virtual until a definition or integrity
        // operation needs ordinary storage. Tombstones prevent resurrection.
        return name is "name" or "length" && !_staticAutoAccessorStorage.ContainsKey(name)
            ? new SharpTSPropertyDescriptor(name == "name" ? _inferredName ?? Name : (object)(double)Arity(),
                writable: false, enumerable: false, configurable: true) { HasValue = true }
            : null;
    }

    internal SharpTSPropertyDescriptor? FindStaticPropertyDescriptor(string name)
        => name == "prototype"
            ? GetOwnStaticPropertyDescriptor(name)
            : HasOwnStaticMember(name)
                ? GetOwnStaticPropertyDescriptor(name)
                : Superclass?.FindStaticPropertyDescriptor(name);

    internal bool HasOwnStaticMember(string name)
        => name == "prototype"
            || name is "name" or "length" && !_deletedStaticMethods.Contains(name)
            || _staticProperties.ContainsKey(name)
            || _staticPropertyObject?.GetOwnPropertyDescriptor(name) is not null
            || !_deletedStaticMethods.Contains(name) && _staticMethods.ContainsKey(name)
            || !_staticAccessorsMaterialized && (_staticGetters.ContainsKey(name) || _staticSetters.ContainsKey(name))
            || _staticAutoAccessorStorage.ContainsKey(name);

    internal bool DeleteStaticProperty(string name)
    {
        if (name == "prototype") return false;
        if (GetOwnStaticPropertyDescriptor(name) is null) return true;
        if (_staticPropertyObject is not null && !_staticPropertyObject.DeleteProperty(name))
            return false;
        _staticProperties.Remove(name);
        if (_staticMethods.ContainsKey(name) || name is "name" or "length")
            _deletedStaticMethods.Add(name);
        _staticMethodCache.TryRemove(name, out _);
        _ownStaticPropertyOrder.Remove(name);
        return true;
    }

    internal bool DefineStaticProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        // The intrinsic slot is separate from mutable statics. Compatible
        // redefinitions preserve its identity without creating an overlay.
        if (name == "prototype")
            return !descriptor.HasGet && !descriptor.HasSet
                && !(descriptor.HasWritable && descriptor.Writable)
                && !(descriptor.HasEnumerable && descriptor.Enumerable)
                && !(descriptor.HasConfigurable && descriptor.Configurable)
                && (!descriptor.HasValue || PropertySemantics.SameValue(descriptor.Value, Prototype));
        MaterializeStaticAccessors();
        if (!HasOwnStaticMember(name) && !Compilation.PropertyDescriptorStore.IsExtensible(this))
            return false;
        if (!MaterializeOwnStaticProperty(name).DefineProperty(name, descriptor))
            return false;
        if (_staticMethods.ContainsKey(name))
            _deletedStaticMethods.Add(name);
        _staticMethodCache.TryRemove(name, out _);
        RecordOwnStaticKey(name);
        return true;
    }

    // Symbol-keyed expando statics (`(C as any)[Symbol.species] = V`). Node allows
    // arbitrary statics on class constructors; string keys reuse _staticProperties.
    // Lazy — most classes never receive symbol-keyed statics.
    private Dictionary<SharpTSSymbol, object?>? _staticSymbolProperties;

    public void SetStaticBySymbol(SharpTSSymbol symbol, object? value)
        => SetStaticBySymbol(symbol, value, strictMode: false);

    internal void SetStaticBySymbol(SharpTSSymbol symbol, object? value, bool strictMode)
    {
        if (GetOwnStaticSymbolPropertyDescriptor(symbol) is null
            && !Compilation.PropertyDescriptorStore.IsExtensible(this))
        {
            if (strictMode)
                throw StrictModeErrors.TypeError($"Cannot add property '{symbol}' to a non-extensible object");
            return;
        }
        // Assignment changes a method's value without changing its attributes.
        if (_staticSymbolAccessorObject?.GetOwnPropertyDescriptor(symbol) is null
            && _staticSymbolMethods?.ContainsKey(symbol) == true)
            MaterializeOwnStaticProperty(symbol);
        if (_staticSymbolAccessorObject?.GetOwnPropertyDescriptor(symbol) is not null)
        {
            _staticSymbolAccessorObject.SetBySymbol(symbol, value);
            RecordOwnStaticKey(symbol);
            return;
        }
        (_staticSymbolProperties ??= [])[symbol] = value;
        RecordOwnStaticKey(symbol);
    }

    /// <summary>
    /// Looks up a symbol-keyed static, walking the superclass chain — class
    /// constructors inherit from the parent constructor per ECMA-262, so
    /// e.g. a Symbol.species set on a base class is visible on subclasses.
    /// </summary>
    public bool TryGetStaticBySymbol(SharpTSSymbol symbol, out object? value)
    {
        if (_staticSymbolProperties != null && _staticSymbolProperties.TryGetValue(symbol, out value))
        {
            return true;
        }
        if (Superclass != null)
        {
            return Superclass.TryGetStaticBySymbol(symbol, out value);
        }
        value = null;
        return false;
    }

    // Symbol-keyed declared accessors (`get [Symbol.toStringTag]() {...}`,
    // `static get [Symbol.species]() {...}`). Computed accessor keys are
    // evaluated at class-definition time; symbol-valued keys land here,
    // string-valued keys go into the regular getter/setter dictionaries.
    // Lazy — most classes declare none.
    private Dictionary<SharpTSSymbol, SharpTSFunction>? _symbolGetters;
    private Dictionary<SharpTSSymbol, SharpTSFunction>? _symbolSetters;
    private Dictionary<SharpTSSymbol, SharpTSFunction>? _staticSymbolGetters;
    private Dictionary<SharpTSSymbol, SharpTSFunction>? _staticSymbolSetters;

    public void AddSymbolAccessor(SharpTSSymbol symbol, SharpTSFunction func, bool isStatic, bool isGetter)
    {
        BindPrivateMemberOwner(func);
        // Name only the new declaration. Rebuilding a paired descriptor or
        // materializing the prototype must not restore a mutated accessor name.
        string symbolName = symbol.Description is { } description ? $"[{description}]" : "";
        NameAccessor(func, (isGetter ? "get " : "set ") + symbolName);
        switch (isStatic, isGetter)
        {
            case (true, true): (_staticSymbolGetters ??= [])[symbol] = func; break;
            case (true, false): (_staticSymbolSetters ??= [])[symbol] = func; break;
            case (false, true): (_symbolGetters ??= [])[symbol] = func; break;
            case (false, false): (_symbolSetters ??= [])[symbol] = func; break;
        }
        var descriptor = OwnSymbolAccessorDescriptor(symbol, isStatic);
        if (isStatic)
        {
            (_staticSymbolAccessorObject ??= new SharpTSObject([])).DefineProperty(symbol, descriptor!);
            RecordOwnStaticKey(symbol);
        }
        else
            _prototype?.DefineDeclaredSymbolAccessor(symbol, descriptor!);
    }

    public SharpTSFunction? FindSymbolGetter(SharpTSSymbol symbol)
        => _symbolGetters?.GetValueOrDefault(symbol) ?? Superclass?.FindSymbolGetter(symbol);

    public SharpTSFunction? FindSymbolSetter(SharpTSSymbol symbol)
        => _symbolSetters?.GetValueOrDefault(symbol) ?? Superclass?.FindSymbolSetter(symbol);

    public SharpTSFunction? FindStaticSymbolGetter(SharpTSSymbol symbol)
        => FindStaticSymbolAccessorDescriptor(symbol) is { } descriptor
            ? descriptor.Get as SharpTSFunction : null;

    public SharpTSFunction? FindStaticSymbolSetter(SharpTSSymbol symbol)
        => FindStaticSymbolAccessorDescriptor(symbol) is { } descriptor
            ? descriptor.Set as SharpTSFunction : null;

    // Symbol-keyed declared methods (`[Symbol.iterator]() {...}`, the generator
    // `*[Symbol.iterator]()` / async `async *[Symbol.asyncIterator]()` forms, and the
    // static variants). Computed method keys are evaluated at class-definition time;
    // symbol-valued keys land here, string-valued keys go into the regular method
    // dictionaries. Stored as ISharpTSCallable so generator/async functions are held
    // unboxed and bound via BindMethod. Lazy — most classes declare none.
    private Dictionary<SharpTSSymbol, ISharpTSCallable>? _symbolMethods;
    private Dictionary<SharpTSSymbol, ISharpTSCallable>? _staticSymbolMethods;

    public void AddSymbolMethod(SharpTSSymbol symbol, ISharpTSCallable func, bool isStatic)
    {
        string name = symbol.Description is { } description ? $"[{description}]" : "";
        SetComputedMethodName(func, name);
        BindPrivateMemberOwner(func);
        if (isStatic)
        {
            (_staticSymbolMethods ??= [])[symbol] = func;
            RecordOwnStaticKey(symbol);
        }
        else
        {
            (_symbolMethods ??= [])[symbol] = func;
            _prototype?.DefineDeclaredSymbolMethod(symbol, func);
        }
    }

    internal static void SetComputedMethodName(ISharpTSCallable func, string name)
    {
        switch (func)
        {
            case SharpTSFunction function:
                function.DefineProperty("name", new SharpTSPropertyDescriptor(value: name, configurable: true));
                break;
            case SharpTSAsyncFunction function:
                function.DefineProperty("name", new SharpTSPropertyDescriptor(value: name, configurable: true));
                break;
            case SharpTSGeneratorFunction function:
                function.ComputedName = name;
                break;
            case SharpTSAsyncGeneratorFunction function:
                function.ComputedName = name;
                break;
        }
    }

    internal IEnumerable<(SharpTSSymbol Key, ISharpTSCallable Method)> OwnInstanceSymbolMethods()
    {
        foreach (var (key, method) in _symbolMethods ?? [])
            yield return (key, method);
    }

    public ISharpTSCallable? FindSymbolMethod(SharpTSSymbol symbol)
        => FindInstanceSymbolPropertyDescriptor(symbol) is { HasValue: true, Value: ISharpTSCallable method }
            ? method : null;

    public ISharpTSCallable? FindStaticSymbolMethod(SharpTSSymbol symbol)
        => _staticSymbolMethods?.GetValueOrDefault(symbol) ?? Superclass?.FindStaticSymbolMethod(symbol);

    public SharpTSFunction? FindGetter(string name)
    {
        // Check cache first
        if (_getterCache.TryGetValue(name, out SharpTSFunction? cached))
        {
            return cached;
        }

        // Look up in this class's getters
        if (_getters.TryGetValue(name, out SharpTSFunction? getter))
        {
            _getterCache[name] = getter;
            return getter;
        }

        // Look up in superclass chain
        var result = Superclass?.FindGetter(name);
        _getterCache[name] = result;
        return result;
    }

    public SharpTSFunction? FindSetter(string name)
    {
        // Check cache first
        if (_setterCache.TryGetValue(name, out SharpTSFunction? cached))
        {
            return cached;
        }

        // Look up in this class's setters
        if (_setters.TryGetValue(name, out SharpTSFunction? setter))
        {
            _setterCache[name] = setter;
            return setter;
        }

        // Look up in superclass chain
        var result = Superclass?.FindSetter(name);
        _setterCache[name] = result;
        return result;
    }

    public bool HasGetter(string name) => _getters.ContainsKey(name) || (Superclass?.HasGetter(name) ?? false);
    public bool HasSetter(string name) => _setters.ContainsKey(name) || (Superclass?.HasSetter(name) ?? false);

    public SharpTSFunction? FindStaticGetter(string name)
    {
        MaterializeStaticAccessors();
        if (_staticPropertyObject?.GetOwnPropertyDescriptor(name) is { } descriptor)
            return descriptor.Get as SharpTSFunction;
        return Superclass?.FindStaticGetter(name);
    }

    public SharpTSFunction? FindStaticSetter(string name)
    {
        MaterializeStaticAccessors();
        if (_staticPropertyObject?.GetOwnPropertyDescriptor(name) is { } descriptor)
            return descriptor.Set as SharpTSFunction;
        return Superclass?.FindStaticSetter(name);
    }

    public bool HasStaticGetter(string name) => FindStaticGetter(name) != null;
    public bool HasStaticSetter(string name) => FindStaticSetter(name) != null;

    #region ES2022 Private Class Elements

    internal void BindPrivateMemberOwners()
    {
        foreach (var method in _methods.Values.Concat(_staticMethods.Values)
            .Concat(_privateMethods.Values).Concat(_staticPrivateMethods.Values)
            .Concat(_getters.Values).Concat(_setters.Values)
            .Concat(_staticGetters.Values).Concat(_staticSetters.Values))
            BindPrivateMemberOwner(method);
    }

    internal void BindPrivateMemberOwner(ISharpTSCallable method)
    {
        switch (method)
        {
            case SharpTSFunction function: function.PrivateOwner = this; break;
            case SharpTSAsyncFunction function: function.PrivateOwner = this; break;
            case SharpTSGeneratorFunction function: function.PrivateOwner = this; break;
            case SharpTSAsyncGeneratorFunction function: function.PrivateOwner = this; break;
        }
    }

    internal bool HasPrivateBrand(object receiver) => _privateFieldStorage.TryGetValue(receiver, out _);

    internal bool DeclaresPrivateName(string name)
        => _instancePrivateFields.Any(field => field.Name.Lexeme == name)
            || _privateMethods.ContainsKey(name) || _staticPrivateFields.ContainsKey(name)
            || _staticPrivateMethods.ContainsKey(name);

    internal bool HasInstalledPrivateField(object receiver, string name)
        => _privateFieldStorage.TryGetValue(receiver, out var fields) && fields.ContainsKey(name);

    internal bool HasInstalledStaticPrivateField(string name) => _installedStaticPrivateFields.Contains(name);

    /// <summary>
    /// Initializes private fields for an instance. Each class in the hierarchy
    /// has its own private field storage - private fields are NOT inherited.
    /// </summary>
    protected void InitializePrivateFields(Interpreter interpreter, SharpTSInstance instance)
    {
        // A derived instance carries each ancestor's independent private brand.
        // Initialize every owner's storage on this same receiver.
        Superclass?.InitializePrivateFields(interpreter, instance);

        var fields = new Dictionary<string, object?>();
        _privateFieldStorage.Add(instance, fields);
        foreach (var field in _instancePrivateFields)
        {
            object? value = field.Initializer != null
                ? interpreter.EvaluateClassInitializer(this, instance, field.Initializer)
                : null;
            fields[field.Name.Lexeme] = value;
        }

    }

    /// <summary>
    /// Gets a private field value from an instance.
    /// Throws if the instance doesn't have this class's private fields (brand check).
    /// </summary>
    public object? GetPrivateField(object instance, string name)
    {
        if (!_privateFieldStorage.TryGetValue(instance, out var fields))
        {
            throw new Exception($"TypeError: Cannot read private member {name} from an object whose class did not declare it");
        }

        if (!fields.TryGetValue(name, out var value))
        {
            throw new Exception($"TypeError: Private field {name} not found");
        }

        return value;
    }

    /// <summary>
    /// Sets a private field value on an instance.
    /// Throws if the instance doesn't have this class's private fields (brand check).
    /// </summary>
    public void SetPrivateField(object instance, string name, object? value)
    {
        if (!_privateFieldStorage.TryGetValue(instance, out var fields))
        {
            throw new Exception($"TypeError: Cannot write private member {name} to an object whose class did not declare it");
        }

        fields[name] = value;
    }

    /// <summary>
    /// Gets a private instance method. Private methods are NOT inherited.
    /// </summary>
    public ISharpTSCallable? GetPrivateMethod(string name)
    {
        return _privateMethods.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets a static private field value.
    /// </summary>
    public object? GetStaticPrivateField(string name)
    {
        if (!_staticPrivateFields.TryGetValue(name, out var value))
        {
            throw new Exception($"TypeError: Static private field {name} not found");
        }
        return value;
    }

    /// <summary>
    /// Sets a static private field value.
    /// </summary>
    public void SetStaticPrivateField(string name, object? value)
    {
        if (!_installedStaticPrivateFields.Contains(name))
            throw new ThrowException(new SharpTSTypeError($"Cannot access private member {name} before initialization"));
        _staticPrivateFields[name] = value;
    }

    internal void InstallStaticPrivateField(string name, object? value)
    {
        _staticPrivateFields[name] = value;
        _installedStaticPrivateFields.Add(name);
    }

    /// <summary>
    /// Gets a static private method. Static private methods are NOT inherited.
    /// </summary>
    public ISharpTSCallable? GetStaticPrivateMethod(string name)
    {
        return _staticPrivateMethods.GetValueOrDefault(name);
    }

    /// <summary>
    /// Checks if this class has a static private field with the given name.
    /// </summary>
    public bool HasStaticPrivateField(string name)
    {
        return _staticPrivateFields.ContainsKey(name);
    }

    #endregion

    #region Auto-Accessor Support (TypeScript 4.9+)

    /// <summary>
    /// Initializes auto-accessor backing storage for an instance.
    /// Auto-accessors are inherited through the superclass chain.
    /// </summary>
    protected void InitializeAutoAccessors(Interpreter interpreter, SharpTSInstance instance)
    {
        // First initialize superclass auto-accessors
        Superclass?.InitializeAutoAccessors(interpreter, instance);

        InitializeOwnAutoAccessors(interpreter, instance);
    }

    private void InitializeOwnAutoAccessors(Interpreter interpreter, object instance)
    {
        // Then initialize this class's auto-accessors
        if (_instanceAutoAccessors.Count == 0)
            return;

        var storage = _autoAccessorStorage.GetValue(instance, static _ => []);
        foreach (var autoAccessor in _instanceAutoAccessors)
        {
            object? value = autoAccessor.Initializer != null
                ? interpreter.EvaluateClassInitializer(this, instance, autoAccessor.Initializer)
                : null;
            if (!storage.TryAdd(autoAccessor.Name.Lexeme, value))
                throw new ThrowException(new SharpTSTypeError($"Cannot initialize auto-accessor {autoAccessor.Name.Lexeme} twice on the same object"));
        }
    }

    /// <summary>
    /// Gets an auto-accessor backing value for an instance.
    /// </summary>
    public object? GetAutoAccessorValue(object instance, string name)
    {
        if (_autoAccessorStorage.TryGetValue(instance, out var storage) &&
            storage.TryGetValue(name, out var value))
        {
            return value;
        }

        // Check superclass
        return Superclass?.GetAutoAccessorValue(instance, name);
    }

    /// <summary>
    /// Sets an auto-accessor backing value for an instance.
    /// </summary>
    public void SetAutoAccessorValue(object instance, string name, object? value)
    {
        if (_autoAccessorStorage.TryGetValue(instance, out var storage) &&
            storage.ContainsKey(name))
        {
            storage[name] = value;
            return;
        }

        // Check superclass
        Superclass?.SetAutoAccessorValue(instance, name, value);
    }

    /// <summary>
    /// Checks if this class has an instance auto-accessor with the given name.
    /// </summary>
    public bool HasInstanceAutoAccessor(string name)
    {
        return _instanceAutoAccessors.Any(a => a.Name.Lexeme == name) ||
               (Superclass?.HasInstanceAutoAccessor(name) ?? false);
    }

    /// <summary>
    /// Gets the auto-accessor declaration for readonly checking.
    /// </summary>
    public Stmt.AutoAccessor? GetAutoAccessorDeclaration(string name)
    {
        var accessor = _instanceAutoAccessors.FirstOrDefault(a => a.Name.Lexeme == name);
        if (accessor != null) return accessor;
        return Superclass?.GetAutoAccessorDeclaration(name);
    }

    /// <summary>
    /// Gets a static auto-accessor backing value.
    /// </summary>
    public object? GetStaticAutoAccessorValue(string name)
    {
        if (_staticAutoAccessorStorage.TryGetValue(name, out var value))
        {
            return value;
        }
        return Superclass?.GetStaticAutoAccessorValue(name);
    }

    /// <summary>
    /// Sets a static auto-accessor backing value.
    /// </summary>
    public void SetStaticAutoAccessorValue(string name, object? value)
    {
        if (_staticAutoAccessorStorage.ContainsKey(name))
        {
            _staticAutoAccessorStorage[name] = value;
            return;
        }
        Superclass?.SetStaticAutoAccessorValue(name, value);
    }

    /// <summary>
    /// Checks if this class has a static auto-accessor with the given name.
    /// </summary>
    public bool HasStaticAutoAccessor(string name)
    {
        return _staticAutoAccessorStorage.ContainsKey(name) ||
               (Superclass?.HasStaticAutoAccessor(name) ?? false);
    }

    internal bool HasOwnStaticAutoAccessor(string name) => _staticAutoAccessorStorage.ContainsKey(name);

    #endregion

    #region RuntimeValue Overloads

    public RuntimeValue CallRV(Interpreter interpreter, List<object?> arguments)
        => RuntimeValue.FromBoxed(Call(interpreter, arguments));

    public RuntimeValue GetStaticPrivateFieldRV(string name)
        => RuntimeValue.FromBoxed(GetStaticPrivateField(name));

    public RuntimeValue GetPrivateFieldRV(object instance, string name)
        => RuntimeValue.FromBoxed(GetPrivateField(instance, name));

    #endregion

    public override string ToString() => Name;
}
