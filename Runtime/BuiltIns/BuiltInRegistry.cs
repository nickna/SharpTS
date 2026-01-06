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
    private readonly List<(Type Type, Func<object, string, object?> GetMember)> _instanceTypes = new();

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
        foreach (var (type, getMember) in _instanceTypes)
        {
            if (type.IsInstanceOfType(instance))
            {
                var member = getMember(instance, memberName);
                if (member != null)
                {
                    return member;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if a type has built-in instance members registered.
    /// </summary>
    public bool HasInstanceMembers(object instance)
    {
        foreach (var (type, _) in _instanceTypes)
        {
            if (type.IsInstanceOfType(instance))
            {
                return true;
            }
        }
        return false;
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
        _instanceTypes.Add((type, getMember));
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
        RegisterDateNamespace(registry);

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
        RegisterIteratorType(registry);

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

    private static void RegisterIteratorType(BuiltInRegistry registry)
    {
        // Iterator doesn't have instance methods beyond iteration itself
        // It's consumed by for...of loops directly
        registry.RegisterInstanceType(typeof(SharpTSIterator), (_, _) => null);
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
