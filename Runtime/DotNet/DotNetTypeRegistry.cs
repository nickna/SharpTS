using System.Collections.Concurrent;
using System.Reflection;
using SharpTS.Declaration;

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
    private static readonly ConcurrentDictionary<(Type, bool), PropertyInfo[]> _indexerCache = new();

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
    /// Resolves a friendly CLR type spelling, including constructed generics such as
    /// <c>System.Collections.Generic.Dictionary&lt;string, System.Int32&gt;</c>.
    /// TypeScript primitive spellings are accepted as convenient aliases
    /// (<c>number</c> is <see cref="double"/>).
    /// </summary>
    /// <param name="friendlyName">The friendly or ordinary CLR type name.</param>
    /// <param name="resolve">
    /// Optional resolver for non-generic type names. The compiler and language server pass
    /// reference-aware resolvers here; interpreter callers use the loaded-assembly default.
    /// </param>
    public static Type? ResolveFriendly(string friendlyName, Func<string, Type?>? resolve = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(friendlyName);
        resolve ??= Resolve;

        string name = friendlyName.Trim();
        if (TryResolveAlias(name, out var alias))
            return resolve(alias.FullName!) ?? alias;

        if (name.EndsWith("[]", StringComparison.Ordinal))
        {
            var element = ResolveFriendly(name[..^2], resolve);
            return element?.MakeArrayType();
        }

        if (name.EndsWith('?'))
        {
            var underlying = ResolveFriendly(name[..^1], resolve);
            if (underlying is not { IsValueType: true }) return null;
            var nullableDefinition = resolve("System.Nullable`1") ?? typeof(Nullable<>);
            return nullableDefinition.MakeGenericType(underlying);
        }

        int genericStart = name.IndexOf('<');
        if (genericStart < 0) return resolve(name);
        if (!name.EndsWith('>'))
            throw new ArgumentException($"Malformed generic .NET type name '{friendlyName}'.");

        string baseName = name[..genericStart].Trim();
        string argumentsText = name[(genericStart + 1)..^1];
        var argumentNames = SplitGenericArguments(argumentsText);
        if (argumentNames.Count == 0)
            throw new ArgumentException($"Generic .NET type '{friendlyName}' has no type arguments.");

        string definitionName = $"{baseName}`{argumentNames.Count}";
        var definition = resolve(definitionName);
        if (definition == null || !definition.IsGenericTypeDefinition) return null;

        // Preserve the legacy open-generic spellings List<> / Dictionary<,>. They remain
        // discoverable, but the interop classifier will reject them as runtime values.
        if (argumentNames.All(string.IsNullOrWhiteSpace)) return definition;
        if (argumentNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Generic .NET type '{friendlyName}' has a missing type argument.");

        var arguments = new Type[argumentNames.Count];
        for (int i = 0; i < argumentNames.Count; i++)
        {
            arguments[i] = ResolveFriendly(argumentNames[i], resolve)
                ?? throw new ArgumentException(
                    $"Could not resolve generic type argument '{argumentNames[i]}' in '{friendlyName}'.");
        }

        try
        {
            return definition.MakeGenericType(arguments);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"The generic arguments for '{friendlyName}' do not satisfy the CLR type constraints.", ex);
        }
    }

    /// <summary>Returns a CLR type's source-facing simple name without generic arity.</summary>
    public static string GetFriendlySimpleName(Type type)
    {
        string name = type.Name;
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    /// <summary>
    /// Formats a resolved CLR type as a round-trippable friendly name suitable for a
    /// <c>dotnet:</c> specifier.
    /// </summary>
    public static string GetFriendlyFullName(Type type)
    {
        if (type.IsArray)
            return GetFriendlyFullName(type.GetElementType()!) + "[]";

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
            return GetFriendlyFullName(nullable) + "?";

        if (TryGetAlias(type, out var alias))
            return alias;

        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        var definition = type.GetGenericTypeDefinition();
        string baseName = definition.FullName ?? definition.Name;
        int tick = baseName.IndexOf('`');
        if (tick >= 0) baseName = baseName[..tick];
        return $"{baseName}<{string.Join(", ", type.GetGenericArguments().Select(GetFriendlyFullName))}>";
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
        _indexerCache.Clear();
    }

    private static List<string> SplitGenericArguments(string arguments)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    if (depth < 0)
                        throw new ArgumentException($"Malformed generic argument list '<{arguments}>'.");
                    break;
                case ',' when depth == 0:
                    result.Add(arguments[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        if (depth != 0)
            throw new ArgumentException($"Malformed generic argument list '<{arguments}>'.");

        result.Add(arguments[start..].Trim());
        return result;
    }

    private static bool TryResolveAlias(string name, out Type type)
    {
        type = name switch
        {
            "bool" or "boolean" or "System.Boolean" => typeof(bool),
            "byte" or "System.Byte" => typeof(byte),
            "sbyte" or "System.SByte" => typeof(sbyte),
            "char" or "System.Char" => typeof(char),
            "short" or "System.Int16" => typeof(short),
            "ushort" or "System.UInt16" => typeof(ushort),
            "int" or "System.Int32" => typeof(int),
            "uint" or "System.UInt32" => typeof(uint),
            "long" or "System.Int64" => typeof(long),
            "ulong" or "System.UInt64" => typeof(ulong),
            "float" or "System.Single" => typeof(float),
            "number" or "double" or "System.Double" => typeof(double),
            "decimal" or "System.Decimal" => typeof(decimal),
            "string" or "System.String" => typeof(string),
            "object" or "unknown" or "any" or "System.Object" => typeof(object),
            "void" or "System.Void" => typeof(void),
            _ => null!
        };
        return type != null;
    }

    private static bool TryGetAlias(Type type, out string alias)
    {
        alias = type == typeof(bool) ? "boolean"
            : type == typeof(byte) ? "byte"
            : type == typeof(sbyte) ? "sbyte"
            : type == typeof(char) ? "char"
            : type == typeof(short) ? "short"
            : type == typeof(ushort) ? "ushort"
            : type == typeof(int) ? "int"
            : type == typeof(uint) ? "uint"
            : type == typeof(long) ? "long"
            : type == typeof(ulong) ? "ulong"
            : type == typeof(float) ? "float"
            : type == typeof(double) ? "number"
            : type == typeof(decimal) ? "decimal"
            : type == typeof(string) ? "string"
            : type == typeof(object) ? "object"
            : type == typeof(void) ? "void"
            : null!;
        return alias != null;
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
                .Where(m =>
                    (m.Name == name || m.Name == pascal) &&
                    DotNetInteropClassifier.UnsupportedMethodReason(m) == null)
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
            if (property != null &&
                DotNetInteropClassifier.UnsupportedSlotReason(property.PropertyType) == null)
            {
                return property;
            }

            var field = t.GetField(pascal, flags) ?? t.GetField(name, flags);
            return field != null &&
                   DotNetInteropClassifier.UnsupportedSlotReason(field.FieldType) == null
                ? field
                : null;
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
    /// Returns public single-parameter instance indexers, filtered for readable or writable use.
    /// </summary>
    internal static PropertyInfo[] GetIndexers(Type type, bool writable)
    {
        return _indexerCache.GetOrAdd((type, writable), static key =>
        {
            var (target, write) = key;
            return target.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 1 &&
                            (write ? p.CanWrite : p.CanRead) &&
                            DotNetInteropClassifier.UnsupportedSlotReason(
                                p.PropertyType) == null &&
                            DotNetInteropClassifier.UnsupportedSlotReason(
                                p.GetIndexParameters()[0].ParameterType) == null)
                .ToArray();
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
