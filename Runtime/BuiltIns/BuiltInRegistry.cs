using SharpTS.Execution;
using SharpTS.Runtime.Types;

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
    /// Registers instance member lookup for a type.
    /// </summary>
    /// <param name="type">The runtime type (e.g., typeof(string))</param>
    /// <param name="getMember">Function to look up a member by name</param>
    public void RegisterInstanceType(Type type, Func<object, string, object?> getMember)
    {
        _instanceTypes[type] = getMember;
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
        RegisterDateNamespace(registry);
        RegisterReflectNamespace(registry);
        RegisterSymbolNamespace(registry);
        RegisterProcessNamespace(registry);
        RegisterGlobalThisNamespace(registry);

        // Register instance types
        RegisterStringType(registry);
        RegisterArrayType(registry);
        RegisterMathType(registry);
        RegisterPromiseType(registry);
        RegisterDoubleType(registry);
        RegisterDateType(registry);
        RegisterRegExpType(registry);
        RegisterMapType(registry);
        RegisterSetType(registry);
        RegisterWeakMapType(registry);
        RegisterWeakSetType(registry);
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

        return registry;
    }

    private static void RegisterMathNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Math",
            IsSingleton: true,
            SingletonFactory: () => SharpTSMath.Instance,
            GetMethod: name => MathBuiltIns.GetMember(name) as BuiltInMethod
        ));
    }

    private static void RegisterObjectNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Object",
            IsSingleton: false,
            SingletonFactory: null,
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
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => JSONBuiltIns.GetStaticMethod(name) as BuiltInMethod
        ));
    }

    private static void RegisterConsoleNamespace(BuiltInRegistry registry)
    {
        // console.log is handled as a special case in the interpreter,
        // but we register it here for consistency and potential future use
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "console",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => name switch
            {
                "log" => new BuiltInMethod("log", 0, int.MaxValue, (_, _, args) =>
                {
                    // This implementation is for completeness; interpreter uses its own
                    Console.WriteLine(string.Join(" ", args.Select(Stringify)));
                    return null;
                }),
                _ => null
            }
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
        // Math members accessed via property access (Math.PI, Math.abs)
        registry.RegisterInstanceType(typeof(SharpTSMath), (_, name) =>
            MathBuiltIns.GetMember(name));
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

    private static void RegisterNumberNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Number",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => NumberBuiltIns.GetStaticMember(name) as BuiltInMethod
        ));
    }

    private static void RegisterStringNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "String",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => StringBuiltIns.GetStaticMember(name) as BuiltInMethod
        ));
    }

    private static void RegisterDoubleType(BuiltInRegistry registry)
    {
        // Handle instance methods on boxed doubles: (123).toFixed(2)
        registry.RegisterInstanceType(typeof(double), (instance, name) =>
            NumberBuiltIns.GetInstanceMember((double)instance, name));
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

    private static void RegisterSymbolNamespace(BuiltInRegistry registry)
    {
        registry.RegisterNamespace(new BuiltInNamespace(
            Name: "Symbol",
            IsSingleton: false,
            SingletonFactory: null,
            GetMethod: name => SymbolBuiltIns.GetStaticMember(name) as BuiltInMethod
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

    private static void RegisterIteratorType(BuiltInRegistry registry)
    {
        // Iterator doesn't have instance methods beyond iteration itself
        // It's consumed by for...of loops directly
        registry.RegisterInstanceType(typeof(SharpTSIterator), (_, _) => null);
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
            return "[" + string.Join(", ", arr.Elements.Select(Stringify)) + "]";
        }
        if (obj is SharpTSObject sobj)
        {
            var pairs = sobj.Fields.Select(kv => $"{kv.Key}: {Stringify(kv.Value)}");
            return "{ " + string.Join(", ", pairs) + " }";
        }
        return obj.ToString() ?? "null";
    }
}
