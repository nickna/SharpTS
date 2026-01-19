using System.Collections.Concurrent;

namespace SharpTS.Declaration;

/// <summary>
/// Maps .NET types to TypeScript type annotations.
/// </summary>
public static class DotNetTypeMapper
{
    private static readonly ConcurrentDictionary<Type, string> _cache = new();

    /// <summary>
    /// Maps a .NET type to its TypeScript equivalent.
    /// </summary>
    public static string MapToTypeScript(Type type)
    {
        if (_cache.TryGetValue(type, out var cached))
            return cached;

        var result = MapToTypeScriptCore(type);
        _cache.TryAdd(type, result);
        return result;
    }

    private static string MapToTypeScriptCore(Type type)
    {
        // Handle nullable value types
        var underlyingNullable = Nullable.GetUnderlyingType(type);
        if (underlyingNullable != null)
        {
            return MapToTypeScript(underlyingNullable) + " | null";
        }

        // Void
        if (type == typeof(void))
            return "void";

        // Primitives
        if (type == typeof(string))
            return "string";
        if (type == typeof(bool))
            return "boolean";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) ||
            type == typeof(byte) || type == typeof(sbyte) || type == typeof(uint) ||
            type == typeof(ulong) || type == typeof(ushort) || type == typeof(float) ||
            type == typeof(double) || type == typeof(decimal))
            return "number";

        // Date
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return "Date";

        // Task -> Promise
        if (type == typeof(System.Threading.Tasks.Task))
            return "Promise<void>";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Threading.Tasks.Task<>))
        {
            var innerType = type.GetGenericArguments()[0];
            return $"Promise<{MapToTypeScript(innerType)}>";
        }

        // ValueTask -> Promise
        if (type == typeof(System.Threading.Tasks.ValueTask))
            return "Promise<void>";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Threading.Tasks.ValueTask<>))
        {
            var innerType = type.GetGenericArguments()[0];
            return $"Promise<{MapToTypeScript(innerType)}>";
        }

        // Arrays
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            return WrapForArrayContext(MapToTypeScript(elementType)) + "[]";
        }

        // List<T> -> T[]
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = type.GetGenericArguments()[0];
            return WrapForArrayContext(MapToTypeScript(elementType)) + "[]";
        }

        // IList<T>, IEnumerable<T>, ICollection<T> -> T[]
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(IList<>) ||
                genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(ICollection<>) ||
                genericDef == typeof(IReadOnlyList<>) ||
                genericDef == typeof(IReadOnlyCollection<>))
            {
                var elementType = type.GetGenericArguments()[0];
                return WrapForArrayContext(MapToTypeScript(elementType)) + "[]";
            }
        }

        // Dictionary<K,V> -> Map<K, V>
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var args = type.GetGenericArguments();
            return $"Map<{MapToTypeScript(args[0])}, {MapToTypeScript(args[1])}>";
        }

        // IDictionary<K,V> -> Map<K, V>
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(IDictionary<,>) ||
                genericDef == typeof(IReadOnlyDictionary<,>))
            {
                var args = type.GetGenericArguments();
                return $"Map<{MapToTypeScript(args[0])}, {MapToTypeScript(args[1])}>";
            }
        }

        // HashSet<T> -> Set<T>
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            var elementType = type.GetGenericArguments()[0];
            return $"Set<{MapToTypeScript(elementType)}>";
        }

        // Tuple types
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var tupleTypes = new[]
            {
                typeof(Tuple<>), typeof(Tuple<,>), typeof(Tuple<,,>), typeof(Tuple<,,,>),
                typeof(Tuple<,,,,>), typeof(Tuple<,,,,,>), typeof(Tuple<,,,,,,>), typeof(Tuple<,,,,,,,>)
            };

            if (tupleTypes.Contains(genericDef))
            {
                var args = type.GetGenericArguments();
                var mappedArgs = args.Select(MapToTypeScript);
                return $"[{string.Join(", ", mappedArgs)}]";
            }

            // ValueTuple types
            if (type.FullName?.StartsWith("System.ValueTuple") == true)
            {
                var args = type.GetGenericArguments();
                var mappedArgs = args.Select(MapToTypeScript);
                return $"[{string.Join(", ", mappedArgs)}]";
            }
        }

        // Object type
        if (type == typeof(object))
            return "unknown";

        // Dynamic/ExpandoObject -> any
        if (type == typeof(System.Dynamic.ExpandoObject) || type.FullName == "System.Dynamic.DynamicObject")
            return "any";

        // Regular types - use the type name
        if (type.IsClass || type.IsInterface || type.IsValueType)
        {
            // For known .NET types that map to TypeScript primitives, we've already handled them
            // For other types, return the simple name (without namespace)
            return type.Name;
        }

        // Fallback
        return "unknown";
    }

    /// <summary>
    /// Wraps a TypeScript type in parentheses if it's a union type being used in array context.
    /// For example, "number | null" becomes "(number | null)" so that "[]" binds correctly.
    /// </summary>
    private static string WrapForArrayContext(string tsType)
    {
        // If the type is a union (contains " | "), wrap in parentheses
        if (tsType.Contains(" | "))
        {
            return $"({tsType})";
        }
        return tsType;
    }

    /// <summary>
    /// Converts a .NET method name to TypeScript naming convention (camelCase).
    /// </summary>
    public static string ToTypeScriptMethodName(string dotNetName)
    {
        if (string.IsNullOrEmpty(dotNetName))
            return dotNetName;

        // Handle special method name prefixes
        if (dotNetName.StartsWith("get_") || dotNetName.StartsWith("set_"))
            return dotNetName; // Keep getter/setter prefixes for now

        // Convert PascalCase to camelCase
        if (char.IsUpper(dotNetName[0]))
        {
            return char.ToLowerInvariant(dotNetName[0]) + dotNetName[1..];
        }

        return dotNetName;
    }

    /// <summary>
    /// Converts a .NET property name to TypeScript naming convention (camelCase).
    /// </summary>
    public static string ToTypeScriptPropertyName(string dotNetName)
    {
        if (string.IsNullOrEmpty(dotNetName))
            return dotNetName;

        // Convert PascalCase to camelCase
        if (char.IsUpper(dotNetName[0]))
        {
            return char.ToLowerInvariant(dotNetName[0]) + dotNetName[1..];
        }

        return dotNetName;
    }
}
