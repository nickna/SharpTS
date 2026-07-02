using System.Collections.Concurrent;
using System.Reflection;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Resolves and caches .NET <see cref="Type"/> instances for <c>@DotNetType</c>-annotated
/// TypeScript classes in interpreter mode. Shared across the interpreter process.
/// </summary>
public static class DotNetTypeRegistry
{
    private static readonly ConcurrentDictionary<string, Type> _cache = new(StringComparer.Ordinal);

    // Member lookups are pure functions of (type, jsName, isStatic); callers never mutate
    // the returned arrays/infos, so results are cached process-wide. Interop member access
    // resolves through here on every call, making uncached reflection queries a hot path.
    private static readonly ConcurrentDictionary<(Type, string, bool), MethodInfo[]> _methodCache = new();
    private static readonly ConcurrentDictionary<(Type, string, bool), MemberInfo?> _propertyOrFieldCache = new();
    private static readonly ConcurrentDictionary<(Type, string, bool), EventInfo?> _eventCache = new();

    /// <summary>
    /// Resolves a fully-qualified .NET type name, searching all currently loaded assemblies.
    /// </summary>
    public static Type? Resolve(string clrTypeName)
    {
        if (_cache.TryGetValue(clrTypeName, out var cached)) return cached;

        var type = Type.GetType(clrTypeName, throwOnError: false);
        if (type == null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // A single broken assembly (e.g. a sharpts.json reference targeting a
                // missing dependency) must not poison resolution of unrelated types.
                try
                {
                    type = assembly.GetType(clrTypeName, throwOnError: false);
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException)
                {
                    continue;
                }
                if (type != null) break;
            }
        }

        if (type != null)
        {
            _cache[clrTypeName] = type;
        }
        return type;
    }

    /// <summary>
    /// Clears the type and member caches. Used by tests to ensure isolation.
    /// </summary>
    public static void ClearCache()
    {
        _cache.Clear();
        _methodCache.Clear();
        _propertyOrFieldCache.Clear();
        _eventCache.Clear();
    }

    /// <summary>
    /// Converts a friendly generic type name to CLR syntax.
    /// Example: <c>List&lt;&gt;</c> -> <c>System.Collections.Generic.List`1</c>.
    /// Mirrors <c>ILCompiler.ToClrTypeName</c> so both modes accept the same syntax.
    /// </summary>
    public static string ToClrTypeName(string friendlyName)
    {
        int genericStart = friendlyName.IndexOf('<');
        if (genericStart < 0) return friendlyName;

        string baseName = friendlyName[..genericStart];
        string genericPart = friendlyName[genericStart..];
        int paramCount = genericPart.Count(c => c == ',') + 1;
        return $"{baseName}`{paramCount}";
    }

    /// <summary>
    /// Returns all public methods with the given name (case-sensitive or PascalCase equivalent).
    /// </summary>
    public static MethodInfo[] GetMethods(Type type, string jsName, bool isStatic)
    {
        return _methodCache.GetOrAdd((type, jsName, isStatic), static key =>
        {
            var (t, name, stat) = key;
            string pascal = ToPascalCase(name);
            var flags = BindingFlags.Public | (stat ? BindingFlags.Static : BindingFlags.Instance);
            return t.GetMethods(flags)
                .Where(m => m.Name == name || m.Name == pascal)
                .ToArray();
        });
    }

    /// <summary>
    /// Returns the first matching property or field for the given JS-facing name.
    /// </summary>
    public static MemberInfo? GetPropertyOrField(Type type, string jsName, bool isStatic)
    {
        return _propertyOrFieldCache.GetOrAdd((type, jsName, isStatic), static key =>
        {
            var (t, name, stat) = key;
            string pascal = ToPascalCase(name);
            var flags = BindingFlags.Public | (stat ? BindingFlags.Static : BindingFlags.Instance);

            var property = t.GetProperty(pascal, flags) ?? t.GetProperty(name, flags);
            if (property != null) return property;

            return (MemberInfo?)t.GetField(pascal, flags) ?? t.GetField(name, flags);
        });
    }

    /// <summary>
    /// Returns the first matching <see cref="EventInfo"/> for the given JS-facing name,
    /// or null if no event is found. Used by <c>addEventListener</c>/<c>removeEventListener</c>.
    /// </summary>
    public static EventInfo? GetEvent(Type type, string jsName, bool isStatic)
    {
        return _eventCache.GetOrAdd((type, jsName, isStatic), static key =>
        {
            var (t, name, stat) = key;
            string pascal = ToPascalCase(name);
            var flags = BindingFlags.Public | (stat ? BindingFlags.Static : BindingFlags.Instance);
            return t.GetEvent(pascal, flags) ?? t.GetEvent(name, flags);
        });
    }

    /// <summary>
    /// Converts a camelCase name to PascalCase (first character uppercased).
    /// Matches <c>NamingConventions.ToPascalCase</c> semantics but lives here to avoid
    /// a Compilation namespace dependency.
    /// </summary>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsUpper(name[0])) return name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
