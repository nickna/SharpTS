using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Central registry for all built-in namespaces, static methods, and instance members.
/// Provides unified dispatch for the interpreter while wrapping existing built-in implementations.
/// </summary>
public sealed class BuiltInRegistry
{
    /// <summary>
    /// The singleton instance of the registry with all built-ins registered.
    /// </summary>
    public static BuiltInRegistry Instance { get; } = CreateDefault();

    private readonly Dictionary<string, BuiltInNamespace> _namespaces = new();
    private readonly Dictionary<Type, Func<object, string, object?>> _instanceTypes = new();

    /// <summary>
    /// TypeCategory-indexed dispatch array for fast built-in member lookup.
    /// Eliminates GetType() + Dictionary hash lookup by using a direct array index.
    /// </summary>
    private readonly Func<object, string, object?>?[] _categoryTypes = new Func<object, string, object?>?[64];

    private BuiltInRegistry() { }

    /// <summary>
    /// Tries to get a built-in namespace by name.
    /// </summary>
    /// <param name="name">The namespace name (e.g., "Math", "Object", "JSON")</param>
    /// <param name="ns">The namespace info if found</param>
    /// <returns>True if the namespace exists</returns>
    public bool TryGetNamespace(string name, out BuiltInNamespace? ns)
    {
        return _namespaces.TryGetValue(name, out ns);
    }

    /// <summary>
    /// Gets the singleton instance for a namespace (e.g., Math returns SharpTSMath.Instance).
    /// </summary>
    /// <param name="name">The namespace name</param>
    /// <returns>The singleton instance, or null if not a singleton namespace</returns>
    public object? GetSingleton(string name)
    {
        if (_namespaces.TryGetValue(name, out var ns) && ns.IsSingleton && ns.SingletonFactory != null)
        {
            return ns.SingletonFactory();
        }
        return null;
    }

    /// <summary>
    /// Gets a static method from a namespace (e.g., Object.keys, JSON.parse, Promise.all).
    /// </summary>
    /// <param name="namespaceName">The namespace name</param>
    /// <param name="methodName">The method name</param>
    /// <returns>The callable method (BuiltInMethod or BuiltInAsyncMethod), or null if not found</returns>
    public ISharpTSCallable? GetStaticMethod(string namespaceName, string methodName)
    {
        if (_namespaces.TryGetValue(namespaceName, out var ns))
        {
            return ns.GetMethod(methodName);
        }
        return null;
    }

    /// <summary>
    /// Gets an instance member (property or method) for a runtime object.
    /// Handles strings, arrays, and Math singleton.
    /// </summary>
    /// <param name="instance">The runtime object</param>
    /// <param name="memberName">The member name</param>
    /// <returns>The member value (property) or BuiltInMethod, or null if not found</returns>
    public object? GetInstanceMember(object instance, string memberName)
    {
        if (_instanceTypes.TryGetValue(instance.GetType(), out var getMember))
        {
            return getMember(instance, memberName);
        }
        return null;
    }

    /// <summary>
    /// Gets an instance member and whether the type is a known built-in type in a single lookup.
    /// Avoids the need for separate GetInstanceMember + HasInstanceMembers calls.
    /// </summary>
    /// <param name="instance">The runtime object</param>
    /// <param name="memberName">The member name</param>
    /// <returns>Tuple of (member or null, isBuiltInType)</returns>
    public (object? Member, bool IsBuiltInType) TryGetInstanceMember(object instance, string memberName)
    {
        if (_instanceTypes.TryGetValue(instance.GetType(), out var getMember))
        {
            return (getMember(instance, memberName), true);
        }
        return (null, false);
    }

    /// <summary>
    /// Checks if a type has built-in instance members registered.
    /// </summary>
    public bool HasInstanceMembers(object instance)
    {
        return _instanceTypes.ContainsKey(instance.GetType());
    }

    /// <summary>
    /// Registers a built-in namespace.
    /// </summary>
    public void RegisterNamespace(BuiltInNamespace ns)
    {
        _namespaces[ns.Name] = ns;
    }

    /// <summary>
    /// Gets a built-in member using a TypeCategory-indexed array for fast dispatch.
    /// Avoids GetType() + Dictionary hash lookup by using a direct array index.
    /// </summary>
    public object? GetMemberByCategory(TypeCategory category, object instance, string memberName)
    {
        var getMember = _categoryTypes[(int)category];
        return getMember?.Invoke(instance, memberName);
    }

    /// <summary>
    /// Checks if a TypeCategory has a registered fast-dispatch handler.
    /// </summary>
    public bool HasCategoryType(TypeCategory category)
    {
        return _categoryTypes[(int)category] != null;
    }

    /// <summary>
    /// Registers instance member lookup for a type.
    /// </summary>
    /// <param name="type">The runtime type (e.g., typeof(string))</param>
    /// <param name="getMember">Function to look up a member by name</param>
    public void RegisterInstanceType(Type type, Func<object, string, object?> getMember)
    {
        _instanceTypes[type] = getMember;
    }

    /// <summary>
    /// Registers fast category-indexed dispatch for a built-in type.
    /// Use alongside RegisterInstanceType for types that have a TypeCategory mapping.
    /// </summary>
    public void RegisterCategoryType(TypeCategory category, Func<object, string, object?> getMember)
    {
        _categoryTypes[(int)category] = getMember;
    }

    /// <summary>
    /// Creates the default registry with all built-in registrations.
    /// </summary>
    private static BuiltInRegistry CreateDefault()
    {
        var registry = new BuiltInRegistry();

        // Register namespaces
        RegisterMathNamespace(registry);
        RegisterObjectNamespace(registry);
        RegisterArrayNamespace(registry);
        RegisterJSONNamespace(registry);
        RegisterConsoleNamespace(registry);
        RegisterPromiseNamespace(registry);
        RegisterNumberNamespace(registry);
        RegisterStringNamespace(registry);
        RegisterBooleanNamespace(registry);
        RegisterDateNamespace(registry);
        RegisterReflectNamespace(registry);
        RegisterMapNamespace(registry);
        RegisterSymbolNamespace(registry);
        RegisterProxyNamespace(registry);
        RegisterProcessNamespace(registry);
        RegisterGlobalThisNamespace(registry);
        RegisterIteratorNamespace(registry);
        RegisterRegExpNamespace(registry);

        // Register instance types
        RegisterStringType(registry);
        RegisterArrayType(registry);
        RegisterMathType(registry);
        RegisterObjectType(registry);
        RegisterPromiseType(registry);
        RegisterDoubleType(registry);
        RegisterDateType(registry);
        RegisterRegExpType(registry);
        RegisterMapType(registry);
        RegisterSetType(registry);
        RegisterWeakMapType(registry);
        RegisterWeakSetType(registry);
        RegisterWeakRefType(registry);
        RegisterFinalizationRegistryType(registry);
        RegisterIteratorType(registry);
        RegisterGeneratorType(registry);
        RegisterAsyncGeneratorType(registry);
        RegisterProcessType(registry);
        RegisterStdinType(registry);
        RegisterStdoutType(registry);
        RegisterStderrType(registry);
        RegisterHashType(registry);
        RegisterHmacType(registry);
        RegisterCipherType(registry);
        RegisterDecipherType(registry);
        RegisterSignType(registry);
        RegisterVerifyType(registry);
        RegisterDiffieHellmanType(registry);
        RegisterECDHType(registry);
        RegisterErrorTypes(registry);
        RegisterReadlineInterfaceType(registry);
        RegisterGlobalThisType(registry);
        RegisterTimeoutType(registry);
        RegisterFunctionTypes(registry);
        RegisterBufferNamespace(registry);
        RegisterBufferType(registry);
        RegisterEventEmitterType(registry);
        // StringDecoder migrated to stdlib/node/string_decoder.ts — TS class uses standard dispatch.
        RegisterStreamTypes(registry);
        RegisterChildProcessType(registry);
        RegisterHttpTypes(registry);
        RegisterWorkerTypes(registry);
        RegisterSymbolType(registry);
        RegisterAbortSignalNamespace(registry);
        RegisterAbortControllerType(registry);
        RegisterAbortSignalType(registry);
        // URL / URLSearchParams — migrated to stdlib/node/url.ts; no C# instance types.
        RegisterRequestResponseTypes(registry);
        RegisterResponseNamespace(registry);
        RegisterIntlNamespace(registry);
        RegisterIntlNumberFormatType(registry);
        RegisterIntlDateTimeFormatType(registry);
        RegisterIntlCollatorType(registry);
        RegisterIntlPluralRulesType(registry);
        RegisterIntlRelativeTimeFormatType(registry);
        RegisterIntlListFormatType(registry);
        RegisterIntlSegmenterType(registry);
        RegisterIntlDisplayNamesType(registry);
        RegisterAsyncLocalStorageType(registry);

        // Register fast category-indexed dispatch for built-in types.
        // These use the already-computed TypeCategory from ClassifyRuntime() to skip
        // GetType() + Dictionary lookup in the hot path.
        RegisterCategoryDispatch(registry);

        return registry;
    }

    private static void RegisterCategoryDispatch(BuiltInRegistry registry)
    {
        registry.RegisterCategoryType(TypeCategory.String, (instance, name) =>
            StringBuiltIns.GetMember((string)instance, name));

        registry.RegisterCategoryType(TypeCategory.Number, (instance, name) =>
            NumberBuiltIns.GetInstanceMember((double)instance, name));

        // Array category is shared by SharpTSArray and SharpTSTypedArray
        registry.RegisterCategoryType(TypeCategory.Array, (instance, name) => instance switch
        {
            SharpTSArray arr => ArrayBuiltIns.GetMember(arr, name),
            SharpTSTypedArray ta => ta.GetMember(name),
            _ => null
        });

        registry.RegisterCategoryType(TypeCategory.Map, (instance, name) =>
            MapBuiltIns.GetMember((SharpTSMap)instance, name));

        registry.RegisterCategoryType(TypeCategory.Set, (instance, name) =>
            SetBuiltIns.GetMember((SharpTSSet)instance, name));

        registry.RegisterCategoryType(TypeCategory.Date, (instance, name) =>
            DateBuiltIns.GetMember((SharpTSDate)instance, name));

        registry.RegisterCategoryType(TypeCategory.RegExp, (instance, name) =>
            RegExpBuiltIns.GetMember((SharpTSRegExp)instance, name));

        Func<object, string, object?> errorMember = (instance, name) =>
            ErrorBuiltIns.GetMember((SharpTSError)instance, name);
        registry.RegisterCategoryType(TypeCategory.Error, errorMember);

        registry.RegisterCategoryType(TypeCategory.Promise, (instance, name) =>
            PromiseBuiltIns.GetMember((SharpTSPromise)instance, name));

        // Buffer category is NOT registered here — it's shared by SharpTSBuffer,
        // SharpTSArrayBuffer, SharpTSDataView, SharpTSSharedArrayBuffer which each have
        // their own member resolution. These fall through to _instanceTypes lookup.

        registry.RegisterCategoryType(TypeCategory.EventEmitter, (instance, name) =>
            ((SharpTSEventEmitter)instance).GetMember(name));

        registry.RegisterCategoryType(TypeCategory.WeakMap, (instance, name) =>
            WeakMapBuiltIns.GetMember((SharpTSWeakMap)instance, name));

        registry.RegisterCategoryType(TypeCategory.WeakSet, (instance, name) =>
            WeakSetBuiltIns.GetMember((SharpTSWeakSet)instance, name));

        registry.RegisterCategoryType(TypeCategory.WeakRef, (instance, name) =>
            WeakRefBuiltIns.GetMember((SharpTSWeakRef)instance, name));

        registry.RegisterCategoryType(TypeCategory.FinalizationRegistry, (instance, name) =>
            FinalizationRegistryBuiltIns.GetMember((SharpTSFinalizationRegistry)instance, name));

        registry.RegisterCategoryType(TypeCategory.Iterator, (instance, name) =>
            IteratorBuiltIns.GetMember((SharpTSIterator)instance, name));

        registry.RegisterCategoryType(TypeCategory.Generator, (instance, name) =>
            GeneratorBuiltIns.GetMember((SharpTSGenerator)instance, name));

        registry.RegisterCategoryType(TypeCategory.AsyncGenerator, (instance, name) =>
            AsyncGeneratorBuiltIns.GetMember((SharpTSAsyncGenerator)instance, name));

        registry.RegisterCategoryType(TypeCategory.Timeout, (instance, name) =>
            TimerBuiltIns.GetMember((SharpTSTimeout)instance, name));

        registry.RegisterCategoryType(TypeCategory.AbortController, (instance, name) =>
            AbortControllerBuiltIns.GetMember((SharpTSAbortController)instance, name));

        registry.RegisterCategoryType(TypeCategory.AbortSignal, (instance, name) =>
            AbortSignalBuiltIns.GetMember((SharpTSAbortSignal)instance, name));

        registry.RegisterCategoryType(TypeCategory.Function, (instance, name) =>
            FunctionBuiltIns.GetMember((ISharpTSCallable)instance, name));

        registry.RegisterCategoryType(TypeCategory.Symbol, (instance, name) =>
            SymbolBuiltIns.GetInstanceMember((SharpTSSymbol)instance, name));
    }

    private static void RegisterMathNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Math",
            IsSingleton: true,
            SingletonFactory: () => SharpTSMath.Instance,
            // Bind to the singleton so the namespace path (`Math.max`) returns
            // the SAME receiver-cached method as the instance path (`m.max`,
            // where m holds Math). Without this they yield distinct wrappers and
            // `Math.max === Math.max` via different access forms is false. Bind
            // caches by receiver, so identity is stable per spec (#288).
            GetMethod: name => (MathBuiltIns.GetMember(name) as BuiltInMethod)?.Bind(SharpTSMath.Instance)
        ));
    }

    private static void RegisterObjectNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Object",
            IsSingleton: true,
            SingletonFactory: () => Types.SharpTSObjectNamespace.Instance,
            GetMethod: name => ObjectBuiltIns.GetStaticMethod(name) as BuiltInMethod
        ));
    }

    private static void RegisterArrayNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Array",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => ArrayStaticBuiltIns.GetStaticMethod(name) as BuiltInMethod
        ));
    }

    private static void RegisterJSONNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "JSON",
            // ECMA-262 25.5: JSON is an ordinary (non-callable, non-
            // constructable) object. Expose a singleton so bare-reference
            // patterns like `var o = JSON` resolve to a real value (typeof
            // === "object"), and so `JSON()` / `new JSON()` route through
            // the interpreter's not-a-function / not-a-constructor paths.
            IsSingleton: true,
            SingletonFactory: () => Types.SharpTSJSON.Instance,
            GetMethod: name => JSONBuiltIns.GetStaticMethod(name) as BuiltInMethod
        ));
        registry.RegisterInstanceType(typeof(Types.SharpTSJSON), (_, name) =>
            JSONBuiltIns.GetStaticMethod(name));
    }

    private static void RegisterConsoleNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "console",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => ConsoleBuiltIns.GetMember(name) as BuiltInMethod
        ));
    }

    private static void RegisterStringType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(string), (instance, name) =>
            StringBuiltIns.GetMember((string)instance, name));
    }

    private static void RegisterArrayType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSArray), (instance, name) =>
            ArrayBuiltIns.GetMember((SharpTSArray)instance, name));
        // SharpTSTemplateStringsArray extends SharpTSArray so needs same methods
        registry.RegisterInstanceType(typeof(SharpTSTemplateStringsArray), (instance, name) =>
            ArrayBuiltIns.GetMember((SharpTSArray)instance, name));
    }

    private static void RegisterMathType(BuiltInRegistry registry)
    {
        // Math members accessed via property access (Math.PI, Math.abs).
        // User-assigned extras (Math[0] = x, Math.length = n — allowed per
        // ECMA-262 since Math is an extensible object) take precedence over
        // built-ins on read so assignment round-trips properly.
        registry.RegisterInstanceType(typeof(SharpTSMath), (instance, name) =>
        {
            var math = (SharpTSMath)instance;
            if (math.HasExtra(name)) return math.TryGetExtra(name);
            return MathBuiltIns.GetMember(name);
        });
    }

    private static void RegisterObjectType(BuiltInRegistry registry)
    {
        // Object members accessed via property access (Object.keys, Object.values).
        // `prototype` resolves to SharpTSObjectPrototype so that CJS packages
        // (lodash) can dereference `Object.prototype.hasOwnProperty` etc.
        registry.RegisterInstanceType(typeof(Types.SharpTSObjectNamespace), (_, name) =>
        {
            if (name == "prototype") return Types.SharpTSObjectPrototype.Instance;
            return ObjectBuiltIns.GetStaticMethod(name);
        });
        registry.RegisterInstanceType(typeof(Types.SharpTSObjectPrototype), (instance, name) =>
            ((Types.SharpTSObjectPrototype)instance).GetMember(name));
    }

    private static void RegisterPromiseNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Promise",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => PromiseBuiltIns.GetStaticMethod(name)
        ));
    }

    private static void RegisterPromiseType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSPromise), (instance, name) =>
            PromiseBuiltIns.GetMember((SharpTSPromise)instance, name));
    }

    private static void RegisterRegExpNamespace(BuiltInRegistry registry)
    {
        // RegExp is registered as a SharpTSBuiltInConstructor, not a singleton
        // object; its GetMember routes static lookups (RegExp.escape) here via
        // GetStaticMethod("RegExp", name). Only the method table is needed.
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "RegExp",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: RegExpBuiltIns.GetStaticMember
        ));
    }

    private static void RegisterNumberNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Number",
            IsSingleton: true,
            SingletonFactory: () => Types.SharpTSNumberNamespace.Instance,
            GetMethod: name => NumberBuiltIns.GetStaticMember(name) as BuiltInMethod
        ));
        // Property access on the Number identifier — `Number.prototype`,
        // `Number.MAX_VALUE`, etc. — and the prototype's own method bag.
        registry.RegisterInstanceType(typeof(Types.SharpTSNumberNamespace), (instance, name) =>
            ((Types.SharpTSNumberNamespace)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(Types.SharpTSNumberPrototype), (instance, name) =>
            ((Types.SharpTSNumberPrototype)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(Types.NumberPrototypeMethodWrapper), (instance, name) =>
            FunctionBuiltIns.GetMember((ISharpTSCallable)instance, name));
    }

    private static void RegisterStringNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "String",
            IsSingleton: true,
            SingletonFactory: () => Types.SharpTSStringNamespace.Instance,
            GetMethod: name => StringBuiltIns.GetStaticMember(name) as BuiltInMethod
        ));
        // Property access on the String identifier — forwards `String.prototype`
        // to the prototype object and static methods to StringBuiltIns.
        registry.RegisterInstanceType(typeof(Types.SharpTSStringNamespace), (instance, name) =>
            ((Types.SharpTSStringNamespace)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(Types.SharpTSStringPrototype), (instance, name) =>
            ((Types.SharpTSStringPrototype)instance).GetMember(name));
        // Allow StringPrototypeMethodWrapper to expose bind/call/apply like
        // every other callable — mirror ArrayPrototypeMethodWrapper below.
        registry.RegisterInstanceType(typeof(Types.StringPrototypeMethodWrapper), (instance, name) =>
            FunctionBuiltIns.GetMember((ISharpTSCallable)instance, name));
    }

    private static void RegisterBooleanNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Boolean",
            IsSingleton: true,
            SingletonFactory: () => Types.SharpTSBooleanNamespace.Instance,
            GetMethod: _ => null
        ));
        // Property access on Boolean / Boolean.prototype / wrapper.
        registry.RegisterInstanceType(typeof(Types.SharpTSBooleanNamespace), (instance, name) =>
            ((Types.SharpTSBooleanNamespace)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(Types.SharpTSBooleanPrototype), (instance, name) =>
            ((Types.SharpTSBooleanPrototype)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(Types.BooleanPrototypeMethodWrapper), (instance, name) =>
            FunctionBuiltIns.GetMember((ISharpTSCallable)instance, name));
    }

    private static void RegisterDoubleType(BuiltInRegistry registry)
    {
        // Handle instance methods on boxed doubles: (123).toFixed(2)
        registry.RegisterInstanceType(typeof(double), (instance, name) =>
            NumberBuiltIns.GetInstanceMember((double)instance, name));
        // Handle instance methods on boxed booleans: (true).toString().
        // Boolean has only two methods, so dispatch inline rather than
        // building a separate BuiltInTypeMemberLookup<bool>.
        registry.RegisterInstanceType(typeof(bool), (instance, name) => name switch
        {
            "toString" => Types.BooleanPrototypeMethodWrapper.ToStringInstance.Bind(instance),
            "valueOf" => Types.BooleanPrototypeMethodWrapper.ValueOfInstance.Bind(instance),
            _ => null
        });
    }

    private static void RegisterDateNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Date",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => DateBuiltIns.GetStaticMethod(name)
        ));
    }

    private static void RegisterReflectNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Reflect",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => ReflectBuiltIns.GetStaticMethod(name)
        ));
    }

    private static void RegisterMapNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Map",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => MapBuiltIns.GetStaticMethod(name) as BuiltInMethod
        ));
    }

    private static void RegisterSymbolNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Symbol",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => SymbolBuiltIns.GetStaticMember(name) as BuiltInMethod
        ));
    }

    private static void RegisterProxyNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Proxy",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => name switch
            {
                "revocable" => BuiltInMethod.CreateV2("revocable", 2, static (_, _, args) =>
                {
                    if (args.Length < 2)
                        throw new Exception("Runtime Error: Proxy.revocable requires exactly 2 arguments (target, handler).");
                    var proxy = new SharpTSProxy(args[0].ToObject()!, args[1].ToObject()!);
                    var revoke = BuiltInMethod.CreateV2("revoke", 0, (_, _, _) =>
                    {
                        proxy.Revoke();
                        return RuntimeValue.Undefined;
                    });
                    var result = new SharpTSObject(new Dictionary<string, object?>
                    {
                        ["proxy"] = proxy,
                        ["revoke"] = revoke
                    });
                    return RuntimeValue.FromObject(result);
                }),
                _ => null
            }
        ));
    }

    private static void RegisterDateType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSDate), (instance, name) =>
            DateBuiltIns.GetMember((SharpTSDate)instance, name));
    }

    private static void RegisterRegExpType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSRegExp), (instance, name) =>
            RegExpBuiltIns.GetMember((SharpTSRegExp)instance, name));
    }

    private static void RegisterMapType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSMap), (instance, name) =>
            MapBuiltIns.GetMember((SharpTSMap)instance, name));
    }

    private static void RegisterSetType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSSet), (instance, name) =>
            SetBuiltIns.GetMember((SharpTSSet)instance, name));
    }

    private static void RegisterWeakMapType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSWeakMap), (instance, name) =>
            WeakMapBuiltIns.GetMember((SharpTSWeakMap)instance, name));
    }

    private static void RegisterWeakSetType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSWeakSet), (instance, name) =>
            WeakSetBuiltIns.GetMember((SharpTSWeakSet)instance, name));
    }

    private static void RegisterWeakRefType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSWeakRef), (instance, name) =>
            WeakRefBuiltIns.GetMember((SharpTSWeakRef)instance, name));
    }

    private static void RegisterFinalizationRegistryType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSFinalizationRegistry), (instance, name) =>
            FinalizationRegistryBuiltIns.GetMember((SharpTSFinalizationRegistry)instance, name));
    }

    private static void RegisterIteratorNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Iterator",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => IteratorStaticBuiltIns.GetStaticMethod(name) as BuiltInMethod
        ));
    }

    private static void RegisterIteratorType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSIterator), (instance, name) =>
            IteratorBuiltIns.GetMember((SharpTSIterator)instance, name));
    }

    private static void RegisterGeneratorType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSGenerator), (instance, name) =>
            GeneratorBuiltIns.GetMember((SharpTSGenerator)instance, name));
    }

    private static void RegisterAsyncGeneratorType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSAsyncGenerator), (instance, name) =>
            AsyncGeneratorBuiltIns.GetMember((SharpTSAsyncGenerator)instance, name));
    }

    private static void RegisterProcessNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "process",
            IsSingleton: true,
            SingletonFactory: () => SharpTSProcess.Instance,
            GetMethod: name => ProcessBuiltIns.GetMember(name) as BuiltInMethod
        ));
    }

    private static void RegisterProcessType(BuiltInRegistry registry)
    {
        // Process members accessed via property access (process.env, process.cwd)
        registry.RegisterInstanceType(typeof(SharpTSProcess), (_, name) =>
            ProcessBuiltIns.GetMember(name));
    }

    private static void RegisterStdinType(BuiltInRegistry registry)
    {
        // Stdin members accessed via property access (process.stdin.read)
        registry.RegisterInstanceType(typeof(SharpTSStdin), (instance, name) =>
            StdinBuiltIns.GetMember((SharpTSStdin)instance, name));
    }

    private static void RegisterStdoutType(BuiltInRegistry registry)
    {
        // Stdout members accessed via property access (process.stdout.write)
        registry.RegisterInstanceType(typeof(SharpTSStdout), (instance, name) =>
            StdoutBuiltIns.GetMember((SharpTSStdout)instance, name));
    }

    private static void RegisterStderrType(BuiltInRegistry registry)
    {
        // Stderr members accessed via property access (process.stderr.write)
        registry.RegisterInstanceType(typeof(SharpTSStderr), (instance, name) =>
            StderrBuiltIns.GetMember((SharpTSStderr)instance, name));
    }

    private static void RegisterHashType(BuiltInRegistry registry)
    {
        // Hash members accessed via property access (hash.update, hash.digest)
        registry.RegisterInstanceType(typeof(SharpTSHash), (instance, name) =>
            ((SharpTSHash)instance).GetMember(name));
    }

    private static void RegisterHmacType(BuiltInRegistry registry)
    {
        // Hmac members accessed via property access (hmac.update, hmac.digest)
        registry.RegisterInstanceType(typeof(SharpTSHmac), (instance, name) =>
            ((SharpTSHmac)instance).GetMember(name));
    }

    private static void RegisterCipherType(BuiltInRegistry registry)
    {
        // Cipher members accessed via property access (cipher.update, cipher.final, etc.)
        registry.RegisterInstanceType(typeof(SharpTSCipher), (instance, name) =>
            ((SharpTSCipher)instance).GetMember(name));
    }

    private static void RegisterDecipherType(BuiltInRegistry registry)
    {
        // Decipher members accessed via property access (decipher.update, decipher.final, etc.)
        registry.RegisterInstanceType(typeof(SharpTSDecipher), (instance, name) =>
            ((SharpTSDecipher)instance).GetMember(name));
    }

    private static void RegisterSignType(BuiltInRegistry registry)
    {
        // Sign members accessed via property access (sign.update, sign.sign)
        registry.RegisterInstanceType(typeof(SharpTSSign), (instance, name) =>
            ((SharpTSSign)instance).GetMember(name));
    }

    private static void RegisterVerifyType(BuiltInRegistry registry)
    {
        // Verify members accessed via property access (verify.update, verify.verify)
        registry.RegisterInstanceType(typeof(SharpTSVerify), (instance, name) =>
            ((SharpTSVerify)instance).GetMember(name));
    }

    private static void RegisterReadlineInterfaceType(BuiltInRegistry registry)
    {
        // Readline Interface members accessed via property access (rl.question, rl.close, rl.prompt)
        registry.RegisterInstanceType(typeof(SharpTSReadlineInterface), (instance, name) =>
            ((SharpTSReadlineInterface)instance).GetMember(name));
    }

    private static void RegisterErrorTypes(BuiltInRegistry registry)
    {
        // Register all Error types - they share the same member lookup
        Func<object, string, object?> getMember = (instance, name) =>
            ErrorBuiltIns.GetMember((SharpTSError)instance, name);

        registry.RegisterInstanceType(typeof(SharpTSError), getMember);
        registry.RegisterInstanceType(typeof(SharpTSTypeError), getMember);
        registry.RegisterInstanceType(typeof(SharpTSRangeError), getMember);
        registry.RegisterInstanceType(typeof(SharpTSReferenceError), getMember);
        registry.RegisterInstanceType(typeof(SharpTSSyntaxError), getMember);
        registry.RegisterInstanceType(typeof(SharpTSURIError), getMember);
        registry.RegisterInstanceType(typeof(SharpTSEvalError), getMember);
        registry.RegisterInstanceType(typeof(SharpTSAggregateError), getMember);

        // Class.prototype is exposed as a SharpTSClassPrototype wrapper that
        // resolves member access through SharpTSClass.FindMethod. Property
        // access on the wrapper itself goes through here.
        registry.RegisterInstanceType(typeof(SharpTSClassPrototype), (instance, name) =>
            ((SharpTSClassPrototype)instance).GetMember(name));
    }

    private static void RegisterGlobalThisNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "globalThis",
            IsSingleton: true,
            SingletonFactory: () => SharpTSGlobalThis.Instance,
            GetMethod: name => null // Methods are accessed through GetProperty delegation
        ));
    }

    private static void RegisterGlobalThisType(BuiltInRegistry registry)
    {
        // globalThis members accessed via property access
        registry.RegisterInstanceType(typeof(SharpTSGlobalThis), (instance, name) =>
            ((SharpTSGlobalThis)instance).GetProperty(name));
    }

    private static void RegisterTimeoutType(BuiltInRegistry registry)
    {
        // Timeout members accessed via property access (ref, unref, hasRef)
        registry.RegisterInstanceType(typeof(SharpTSTimeout), (instance, name) =>
            TimerBuiltIns.GetMember((SharpTSTimeout)instance, name));
    }

    private static void RegisterFunctionTypes(BuiltInRegistry registry)
    {
        // Register function types for bind/call/apply
        registry.RegisterInstanceType(typeof(SharpTSFunction), (instance, name) =>
            FunctionBuiltIns.GetMember((ISharpTSCallable)instance, name));
        registry.RegisterInstanceType(typeof(SharpTSArrowFunction), (instance, name) =>
            FunctionBuiltIns.GetMember((ISharpTSCallable)instance, name));
        registry.RegisterInstanceType(typeof(BoundFunction), (instance, name) =>
            FunctionBuiltIns.GetMember((ISharpTSCallable)instance, name));
        registry.RegisterInstanceType(typeof(BuiltInMethod), (instance, name) =>
            FunctionBuiltIns.GetMember((ISharpTSCallable)instance, name));
        registry.RegisterInstanceType(typeof(ArrayPrototypeMethodWrapper), (instance, name) =>
            FunctionBuiltIns.GetMember((ISharpTSCallable)instance, name));
    }

    private static void RegisterBufferNamespace(BuiltInRegistry registry)
    {
        // Buffer is both a global namespace and a constructor
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Buffer",
            IsSingleton: true,
            SingletonFactory: () => SharpTSBufferConstructor.Instance,
            GetMethod: name => SharpTSBufferConstructor.Instance.GetProperty(name) as BuiltInMethod
        ));
    }

    private static void RegisterBufferType(BuiltInRegistry registry)
    {
        // Register Buffer instance member lookup
        registry.RegisterInstanceType(typeof(SharpTSBuffer), (instance, name) =>
            ((SharpTSBuffer)instance).GetMember(name));

        // Register Buffer constructor member lookup
        registry.RegisterInstanceType(typeof(SharpTSBufferConstructor), (instance, name) =>
            ((SharpTSBufferConstructor)instance).GetProperty(name));
    }

    private static void RegisterEventEmitterType(BuiltInRegistry registry)
    {
        // Register EventEmitter instance member lookup (on, off, emit, etc.)
        registry.RegisterInstanceType(typeof(SharpTSEventEmitter), (instance, name) =>
            ((SharpTSEventEmitter)instance).GetMember(name));

        // Register EventEmitter constructor member lookup (defaultMaxListeners)
        registry.RegisterInstanceType(typeof(SharpTSEventEmitterConstructor), (instance, name) =>
            ((SharpTSEventEmitterConstructor)instance).GetProperty(name));
    }

    private static void RegisterDiffieHellmanType(BuiltInRegistry registry)
    {
        // DiffieHellman members accessed via property access (dh.generateKeys, dh.computeSecret, etc.)
        registry.RegisterInstanceType(typeof(SharpTSDiffieHellman), (instance, name) =>
            ((SharpTSDiffieHellman)instance).GetMember(name));
    }

    private static void RegisterECDHType(BuiltInRegistry registry)
    {
        // ECDH members accessed via property access (ecdh.generateKeys, ecdh.computeSecret, etc.)
        registry.RegisterInstanceType(typeof(SharpTSECDH), (instance, name) =>
            ((SharpTSECDH)instance).GetMember(name));
    }

    private static void RegisterStreamTypes(BuiltInRegistry registry)
    {
        // Readable stream members accessed via property access (read, push, pipe, on, emit, etc.)
        registry.RegisterInstanceType(typeof(SharpTSReadable), (instance, name) =>
            ((SharpTSReadable)instance).GetMember(name));

        // Writable stream members accessed via property access (write, end, on, emit, etc.)
        registry.RegisterInstanceType(typeof(SharpTSWritable), (instance, name) =>
            ((SharpTSWritable)instance).GetMember(name));

        // Duplex stream members (combines Readable and Writable)
        registry.RegisterInstanceType(typeof(SharpTSDuplex), (instance, name) =>
            ((SharpTSDuplex)instance).GetMember(name));

        // Transform stream members (extends Duplex)
        registry.RegisterInstanceType(typeof(SharpTSTransform), (instance, name) =>
            ((SharpTSTransform)instance).GetMember(name));

        // ZlibTransform stream members (extends Transform)
        registry.RegisterInstanceType(typeof(SharpTSZlibTransform), (instance, name) =>
            ((SharpTSZlibTransform)instance).GetMember(name));

        // PassThrough stream members (extends Transform)
        registry.RegisterInstanceType(typeof(SharpTSPassThrough), (instance, name) =>
            ((SharpTSPassThrough)instance).GetMember(name));

        // ReadStream members (extends Readable, for fs.createReadStream)
        registry.RegisterInstanceType(typeof(SharpTSReadStream), (instance, name) =>
            ((SharpTSReadStream)instance).GetMember(name));

        // WriteStream members (extends Writable, for fs.createWriteStream)
        registry.RegisterInstanceType(typeof(SharpTSWriteStream), (instance, name) =>
            ((SharpTSWriteStream)instance).GetMember(name));

        // Stream constructors
        registry.RegisterInstanceType(typeof(SharpTSReadableConstructor), (instance, name) =>
            ((SharpTSReadableConstructor)instance).GetProperty(name));
        registry.RegisterInstanceType(typeof(SharpTSWritableConstructor), (instance, name) =>
            ((SharpTSWritableConstructor)instance).GetProperty(name));
        registry.RegisterInstanceType(typeof(SharpTSDuplexConstructor), (instance, name) =>
            ((SharpTSDuplexConstructor)instance).GetProperty(name));
        registry.RegisterInstanceType(typeof(SharpTSTransformConstructor), (instance, name) =>
            ((SharpTSTransformConstructor)instance).GetProperty(name));
        registry.RegisterInstanceType(typeof(SharpTSPassThroughConstructor), (instance, name) =>
            ((SharpTSPassThroughConstructor)instance).GetProperty(name));
    }

    private static void RegisterChildProcessType(BuiltInRegistry registry)
    {
        // ChildProcess instance member lookup (pid, exitCode, stdout, stderr, kill, on, emit, etc.)
        registry.RegisterInstanceType(typeof(SharpTSChildProcess), (instance, name) =>
            ((SharpTSChildProcess)instance).GetMember(name));
    }

    private static void RegisterHttpTypes(BuiltInRegistry registry)
    {
        // Register fetch Response member lookup
        registry.RegisterInstanceType(typeof(SharpTSFetchResponse), (instance, name) =>
            ((SharpTSFetchResponse)instance).GetMember(name));

        // Register Headers member lookup
        registry.RegisterInstanceType(typeof(SharpTSHeaders), (instance, name) =>
            HeadersBuiltIns.GetMember((SharpTSHeaders)instance, name));

        // Register HTTP server types
        registry.RegisterInstanceType(typeof(SharpTSHttpServer), (instance, name) =>
            ((SharpTSHttpServer)instance).GetMember(name));

        registry.RegisterInstanceType(typeof(SharpTSHttpRequest), (instance, name) =>
            ((SharpTSHttpRequest)instance).GetMember(name));

        registry.RegisterInstanceType(typeof(SharpTSHttpResponse), (instance, name) =>
            ((SharpTSHttpResponse)instance).GetMember(name));

        // Register http.Agent type
        registry.RegisterInstanceType(typeof(SharpTSAgent), (instance, name) =>
            ((SharpTSAgent)instance).GetMember(name));

        // Register net module types
        registry.RegisterInstanceType(typeof(SharpTSNetServer), (instance, name) =>
            ((SharpTSNetServer)instance).GetMember(name));

        // TLS types must be registered before their base types (subclass before superclass)
        registry.RegisterInstanceType(typeof(SharpTSTlsSocket), (instance, name) =>
            ((SharpTSTlsSocket)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSTlsServer), (instance, name) =>
            ((SharpTSTlsServer)instance).GetMember(name));

        registry.RegisterInstanceType(typeof(SharpTSSocket), (instance, name) =>
            ((SharpTSSocket)instance).GetMember(name));

        // Register dgram types
        registry.RegisterInstanceType(typeof(SharpTSDatagramSocket), (instance, name) =>
            ((SharpTSDatagramSocket)instance).GetMember(name));

        // Register file watcher types
        registry.RegisterInstanceType(typeof(SharpTSFSWatcher), (instance, name) =>
            ((SharpTSFSWatcher)instance).GetMember(name));

        registry.RegisterInstanceType(typeof(SharpTSStatWatcher), (instance, name) =>
            ((SharpTSStatWatcher)instance).GetMember(name));

        // Register Web Streams types
        registry.RegisterInstanceType(typeof(SharpTSReadableStream), (instance, name) =>
            ((SharpTSReadableStream)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSReadableStreamDefaultController), (instance, name) =>
            ((SharpTSReadableStreamDefaultController)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSReadableStreamDefaultReader), (instance, name) =>
            ((SharpTSReadableStreamDefaultReader)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSWritableStream), (instance, name) =>
            ((SharpTSWritableStream)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSWritableStreamDefaultController), (instance, name) =>
            ((SharpTSWritableStreamDefaultController)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSWritableStreamDefaultWriter), (instance, name) =>
            ((SharpTSWritableStreamDefaultWriter)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSTransformStream), (instance, name) =>
            ((SharpTSTransformStream)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSTransformStreamDefaultController), (instance, name) =>
            ((SharpTSTransformStreamDefaultController)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSByteLengthQueuingStrategy), (instance, name) =>
            ((SharpTSQueuingStrategy)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSCountQueuingStrategy), (instance, name) =>
            ((SharpTSQueuingStrategy)instance).GetMember(name));

        // The constructor wrapper itself carries static members
        // (ReadableStream.from) — without this registration, property access
        // on the imported constructor never reaches GetProperty (#210).
        registry.RegisterInstanceType(typeof(SharpTSReadableStreamConstructor), (instance, name) =>
            ((SharpTSReadableStreamConstructor)instance).GetProperty(name));
    }

    private static void RegisterWorkerTypes(BuiltInRegistry registry)
    {
        // Register Atomics namespace
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Atomics",
            IsSingleton: true,
            SingletonFactory: () => WorkerBuiltIns.Atomics,
            GetMethod: name => WorkerBuiltIns.GetAtomicsMember(name) as BuiltInMethod
        ));

        // Register ArrayBuffer namespace for static methods like isView
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "ArrayBuffer",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => name switch
            {
                "isView" => BuiltInMethod.CreateV2("isView", 1, static (_, _, args) =>
                    RuntimeValue.FromBoolean(SharpTSArrayBuffer.IsView(args.Length > 0 ? args[0].ToObject() : null))),
                _ => null
            }
        ));

        // Register Worker instance members (postMessage, terminate, on, etc.)
        registry.RegisterInstanceType(typeof(SharpTSWorker), (instance, name) =>
            ((SharpTSWorker)instance).GetMember(name));

        // Register ClusterWorker instance members
        registry.RegisterInstanceType(typeof(SharpTSClusterWorker), (instance, name) =>
            ((SharpTSClusterWorker)instance).GetMember(name));

        // Register MessagePort instance members
        registry.RegisterInstanceType(typeof(SharpTSMessagePort), (instance, name) =>
            ((SharpTSMessagePort)instance).GetMember(name));

        // Register the worker-side parentPort (postMessage + EventEmitter members).
        // Instance lookup is exact-type, so the WorkerParentPort subclass needs
        // its own registration to reach its GetMember (#209).
        registry.RegisterInstanceType(typeof(WorkerParentPort), (instance, name) =>
            ((WorkerParentPort)instance).GetMember(name));

        // Register MessageChannel instance members
        registry.RegisterInstanceType(typeof(SharpTSMessageChannel), (instance, name) =>
            ((SharpTSMessageChannel)instance).GetMember(name));

        // Register BroadcastChannel instance members
        registry.RegisterInstanceType(typeof(SharpTSBroadcastChannel), (instance, name) =>
            ((SharpTSBroadcastChannel)instance).GetMember(name));

        // Register SharedArrayBuffer instance members
        registry.RegisterInstanceType(typeof(SharpTSSharedArrayBuffer), (instance, name) =>
        {
            var sab = (SharpTSSharedArrayBuffer)instance;
            return name switch
            {
                "byteLength" => (double)sab.ByteLength,
                "slice" => BuiltInMethod.CreateV2("slice", 1, 2, (_, _, args) =>
                {
                    int begin = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
                    int? end = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : null;
                    return RuntimeValue.FromObject(sab.Slice(begin, end));
                }),
                _ => null
            };
        });

        // Register ArrayBuffer instance members
        registry.RegisterInstanceType(typeof(SharpTSArrayBuffer), (instance, name) =>
        {
            var ab = (SharpTSArrayBuffer)instance;
            return name switch
            {
                "byteLength" => (double)ab.ByteLength,
                "slice" => BuiltInMethod.CreateV2("slice", 1, 2, (_, _, args) =>
                {
                    int begin = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
                    int? end = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : null;
                    return RuntimeValue.FromObject(ab.Slice(begin, end));
                }),
                _ => null
            };
        });

        // Register TypedArray instance members
        registry.RegisterInstanceType(typeof(SharpTSInt8Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSUint8Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSUint8ClampedArray), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSInt16Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSUint16Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSInt32Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSUint32Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSFloat32Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSFloat64Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSBigInt64Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSBigUint64Array), (instance, name) =>
            ((SharpTSTypedArray)instance).GetMember(name));

        // Register DataView instance members
        registry.RegisterInstanceType(typeof(SharpTSDataView), (instance, name) =>
        {
            var dv = (SharpTSDataView)instance;
            return name switch
            {
                "buffer" => dv.Buffer,
                "byteOffset" => (double)dv.ByteOffset,
                "byteLength" => (double)dv.ByteLength,
                "getInt8" => BuiltInMethod.CreateV2("getInt8", 1, (_, _, args) =>
                    RuntimeValue.FromNumber(dv.GetInt8(args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0))),
                "getUint8" => BuiltInMethod.CreateV2("getUint8", 1, (_, _, args) =>
                    RuntimeValue.FromNumber(dv.GetUint8(args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0))),
                "getInt16" => BuiltInMethod.CreateV2("getInt16", 1, 2, (_, _, args) =>
                    RuntimeValue.FromNumber(dv.GetInt16(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 && args[1].IsBoolean && args[1].AsBooleanUnsafe()))),
                "getUint16" => BuiltInMethod.CreateV2("getUint16", 1, 2, (_, _, args) =>
                    RuntimeValue.FromNumber(dv.GetUint16(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 && args[1].IsBoolean && args[1].AsBooleanUnsafe()))),
                "getInt32" => BuiltInMethod.CreateV2("getInt32", 1, 2, (_, _, args) =>
                    RuntimeValue.FromNumber(dv.GetInt32(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 && args[1].IsBoolean && args[1].AsBooleanUnsafe()))),
                "getUint32" => BuiltInMethod.CreateV2("getUint32", 1, 2, (_, _, args) =>
                    RuntimeValue.FromNumber(dv.GetUint32(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 && args[1].IsBoolean && args[1].AsBooleanUnsafe()))),
                "getFloat32" => BuiltInMethod.CreateV2("getFloat32", 1, 2, (_, _, args) =>
                    RuntimeValue.FromNumber(dv.GetFloat32(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 && args[1].IsBoolean && args[1].AsBooleanUnsafe()))),
                "getFloat64" => BuiltInMethod.CreateV2("getFloat64", 1, 2, (_, _, args) =>
                    RuntimeValue.FromNumber(dv.GetFloat64(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 && args[1].IsBoolean && args[1].AsBooleanUnsafe()))),
                "getBigInt64" => BuiltInMethod.CreateV2("getBigInt64", 1, 2, (_, _, args) =>
                    RuntimeValue.FromBigInt(dv.GetBigInt64(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 && args[1].IsBoolean && args[1].AsBooleanUnsafe()))),
                "getBigUint64" => BuiltInMethod.CreateV2("getBigUint64", 1, 2, (_, _, args) =>
                    RuntimeValue.FromBigInt(dv.GetBigUint64(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 && args[1].IsBoolean && args[1].AsBooleanUnsafe()))),
                "setInt8" => BuiltInMethod.CreateV2("setInt8", 2, (_, _, args) =>
                {
                    dv.SetInt8(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null);
                    return RuntimeValue.Undefined;
                }),
                "setUint8" => BuiltInMethod.CreateV2("setUint8", 2, (_, _, args) =>
                {
                    dv.SetUint8(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null);
                    return RuntimeValue.Undefined;
                }),
                "setInt16" => BuiltInMethod.CreateV2("setInt16", 2, 3, (_, _, args) =>
                {
                    dv.SetInt16(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null,
                        args.Length > 2 && args[2].IsBoolean && args[2].AsBooleanUnsafe());
                    return RuntimeValue.Undefined;
                }),
                "setUint16" => BuiltInMethod.CreateV2("setUint16", 2, 3, (_, _, args) =>
                {
                    dv.SetUint16(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null,
                        args.Length > 2 && args[2].IsBoolean && args[2].AsBooleanUnsafe());
                    return RuntimeValue.Undefined;
                }),
                "setInt32" => BuiltInMethod.CreateV2("setInt32", 2, 3, (_, _, args) =>
                {
                    dv.SetInt32(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null,
                        args.Length > 2 && args[2].IsBoolean && args[2].AsBooleanUnsafe());
                    return RuntimeValue.Undefined;
                }),
                "setUint32" => BuiltInMethod.CreateV2("setUint32", 2, 3, (_, _, args) =>
                {
                    dv.SetUint32(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null,
                        args.Length > 2 && args[2].IsBoolean && args[2].AsBooleanUnsafe());
                    return RuntimeValue.Undefined;
                }),
                "setFloat32" => BuiltInMethod.CreateV2("setFloat32", 2, 3, (_, _, args) =>
                {
                    dv.SetFloat32(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null,
                        args.Length > 2 && args[2].IsBoolean && args[2].AsBooleanUnsafe());
                    return RuntimeValue.Undefined;
                }),
                "setFloat64" => BuiltInMethod.CreateV2("setFloat64", 2, 3, (_, _, args) =>
                {
                    dv.SetFloat64(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null,
                        args.Length > 2 && args[2].IsBoolean && args[2].AsBooleanUnsafe());
                    return RuntimeValue.Undefined;
                }),
                "setBigInt64" => BuiltInMethod.CreateV2("setBigInt64", 2, 3, (_, _, args) =>
                {
                    dv.SetBigInt64(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null,
                        args.Length > 2 && args[2].IsBoolean && args[2].AsBooleanUnsafe());
                    return RuntimeValue.Undefined;
                }),
                "setBigUint64" => BuiltInMethod.CreateV2("setBigUint64", 2, 3, (_, _, args) =>
                {
                    dv.SetBigUint64(
                        args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0,
                        args.Length > 1 ? args[1].ToObject() : null,
                        args.Length > 2 && args[2].IsBoolean && args[2].AsBooleanUnsafe());
                    return RuntimeValue.Undefined;
                }),
                _ => null
            };
        });

        // Register Atomics singleton member lookup
        registry.RegisterInstanceType(typeof(AtomicsSingleton), (instance, name) =>
            ((AtomicsSingleton)instance).GetMember(name));
    }

    private static void RegisterSymbolType(BuiltInRegistry registry)
    {
        // Symbol instance members (description, toString, valueOf)
        registry.RegisterInstanceType(typeof(SharpTSSymbol), (instance, name) =>
            SymbolBuiltIns.GetInstanceMember((SharpTSSymbol)instance, name));
    }

    private static void RegisterAbortSignalNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "AbortSignal",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => AbortSignalBuiltIns.GetStaticMethod(name)
        ));
    }

    private static void RegisterAbortControllerType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSAbortController), (instance, name) =>
            AbortControllerBuiltIns.GetMember((SharpTSAbortController)instance, name));
    }

    private static void RegisterAbortSignalType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSAbortSignal), (instance, name) =>
            AbortSignalBuiltIns.GetMember((SharpTSAbortSignal)instance, name));
    }

    private static void RegisterRequestResponseTypes(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSRequest), (instance, name) =>
            ((SharpTSRequest)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSResponse), (instance, name) =>
            ((SharpTSResponse)instance).GetMember(name));
    }

    private static void RegisterResponseNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Response",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => ResponseBuiltIns.GetStaticMethod(name) as ISharpTSCallable
        ));
    }

    private static void RegisterIntlNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Intl",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => name switch
            {
                "NumberFormat" => BuiltInMethod.CreateV2("NumberFormat", 0, 2, static (_, _, args) =>
                {
                    var locale = args.Length > 0 ? args[0].ToObject() : null;
                    var options = args.Length > 1 ? args[1].ToObject() : null;
                    return RuntimeValue.FromObject(new SharpTSIntlNumberFormat(locale, options));
                }),
                "DateTimeFormat" => BuiltInMethod.CreateV2("DateTimeFormat", 0, 2, static (_, _, args) =>
                {
                    var locale = args.Length > 0 ? args[0].ToObject() : null;
                    var options = args.Length > 1 ? args[1].ToObject() : null;
                    return RuntimeValue.FromObject(new SharpTSIntlDateTimeFormat(locale, options));
                }),
                "Collator" => BuiltInMethod.CreateV2("Collator", 0, 2, static (_, _, args) =>
                {
                    var locale = args.Length > 0 ? args[0].ToObject() : null;
                    var options = args.Length > 1 ? args[1].ToObject() : null;
                    return RuntimeValue.FromObject(new SharpTSIntlCollator(locale, options));
                }),
                "PluralRules" => BuiltInMethod.CreateV2("PluralRules", 0, 2, static (_, _, args) =>
                {
                    var locale = args.Length > 0 ? args[0].ToObject() : null;
                    var options = args.Length > 1 ? args[1].ToObject() : null;
                    return RuntimeValue.FromObject(new SharpTSIntlPluralRules(locale, options));
                }),
                "RelativeTimeFormat" => BuiltInMethod.CreateV2("RelativeTimeFormat", 0, 2, static (_, _, args) =>
                {
                    var locale = args.Length > 0 ? args[0].ToObject() : null;
                    var options = args.Length > 1 ? args[1].ToObject() : null;
                    return RuntimeValue.FromObject(new SharpTSIntlRelativeTimeFormat(locale, options));
                }),
                "ListFormat" => BuiltInMethod.CreateV2("ListFormat", 0, 2, static (_, _, args) =>
                {
                    var locale = args.Length > 0 ? args[0].ToObject() : null;
                    var options = args.Length > 1 ? args[1].ToObject() : null;
                    return RuntimeValue.FromObject(new SharpTSIntlListFormat(locale, options));
                }),
                "Segmenter" => BuiltInMethod.CreateV2("Segmenter", 0, 2, static (_, _, args) =>
                {
                    var locale = args.Length > 0 ? args[0].ToObject() : null;
                    var options = args.Length > 1 ? args[1].ToObject() : null;
                    return RuntimeValue.FromObject(new SharpTSIntlSegmenter(locale, options));
                }),
                "DisplayNames" => BuiltInMethod.CreateV2("DisplayNames", 0, 2, static (_, _, args) =>
                {
                    var locale = args.Length > 0 ? args[0].ToObject() : null;
                    var options = args.Length > 1 ? args[1].ToObject() : null;
                    return RuntimeValue.FromObject(new SharpTSIntlDisplayNames(locale, options));
                }),
                _ => null
            }
        ));
    }

    private static void RegisterIntlNumberFormatType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSIntlNumberFormat), (instance, name) =>
            ((SharpTSIntlNumberFormat)instance).GetMember(name));
    }

    private static void RegisterIntlDateTimeFormatType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSIntlDateTimeFormat), (instance, name) =>
            ((SharpTSIntlDateTimeFormat)instance).GetMember(name));
    }

    private static void RegisterIntlCollatorType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSIntlCollator), (instance, name) =>
            ((SharpTSIntlCollator)instance).GetMember(name));
    }

    private static void RegisterIntlPluralRulesType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSIntlPluralRules), (instance, name) =>
            ((SharpTSIntlPluralRules)instance).GetMember(name));
    }

    private static void RegisterIntlRelativeTimeFormatType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSIntlRelativeTimeFormat), (instance, name) =>
            ((SharpTSIntlRelativeTimeFormat)instance).GetMember(name));
    }

    private static void RegisterIntlListFormatType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSIntlListFormat), (instance, name) =>
            ((SharpTSIntlListFormat)instance).GetMember(name));
    }

    private static void RegisterIntlSegmenterType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSIntlSegmenter), (instance, name) =>
            ((SharpTSIntlSegmenter)instance).GetMember(name));
        registry.RegisterInstanceType(typeof(SharpTSIntlSegments), (instance, name) =>
            ((SharpTSIntlSegments)instance).GetMember(name));
    }

    private static void RegisterIntlDisplayNamesType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSIntlDisplayNames), (instance, name) =>
            ((SharpTSIntlDisplayNames)instance).GetMember(name));
    }

    private static void RegisterAsyncLocalStorageType(BuiltInRegistry registry)
    {
        registry.RegisterInstanceType(typeof(SharpTSAsyncLocalStorage), (instance, name) =>
            ((SharpTSAsyncLocalStorage)instance).GetMember(name));
    }

    private static string Stringify(object? obj)
    {
        if (obj == null) return "null";
        if (obj is double d)
        {
            string text = d.ToString();
            if (text.EndsWith(".0"))
            {
                text = text[..^2];
            }
            return text;
        }
        if (obj is bool b) return b ? "true" : "false";
        if (obj is SharpTSArray arr)
        {
            return "[" + string.Join(", ", arr.Select(Stringify)) + "]";
        }
        if (obj is SharpTSObject sobj)
        {
            var pairs = sobj.Fields.Select(kv => $"{kv.Key}: {Stringify(kv.Value)}");
            return "{ " + string.Join(", ", pairs) + " }";
        }
        return obj.ToString() ?? "null";
    }
}
