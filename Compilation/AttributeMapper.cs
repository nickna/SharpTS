using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Maps TypeScript decorators to .NET attributes.
/// Provides interoperability between TypeScript decorator patterns and .NET attribute system.
/// </summary>
/// <remarks>
/// Supports mapping of:
/// - @Obsolete / @deprecated → [Obsolete]
/// - @Serializable → [Serializable]
/// - @NonSerialized → [NonSerialized]
/// - Custom attributes via @attribute("FullTypeName")
/// </remarks>
public static class AttributeMapper
{
    /// <summary>
    /// Well-known decorator-to-attribute mappings.
    /// Key: decorator name (case-insensitive), Value: attribute type
    /// </summary>
    private static readonly Dictionary<string, Type> KnownAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Obsolete"] = typeof(ObsoleteAttribute),
        ["deprecated"] = typeof(ObsoleteAttribute),
        ["Serializable"] = typeof(SerializableAttribute),
        ["NonSerialized"] = typeof(NonSerializedAttribute),
    };

    /// <summary>
    /// Attempts to map a TypeScript decorator to a .NET CustomAttributeBuilder.
    /// Returns null if the decorator cannot be mapped to a .NET attribute.
    /// </summary>
    /// <param name="decorator">The decorator to map</param>
    /// <returns>A CustomAttributeBuilder if mappable, null otherwise</returns>
    public static CustomAttributeBuilder? MapToAttribute(Decorator decorator)
    {
        string? decoratorName = GetDecoratorName(decorator.Expression);
        if (decoratorName == null)
            return null;

        // Check for known attributes
        if (KnownAttributes.TryGetValue(decoratorName, out var attributeType))
        {
            return CreateAttributeBuilder(attributeType, decorator);
        }

        // Check for @attribute("FullTypeName") pattern
        if (decoratorName.Equals("attribute", StringComparison.OrdinalIgnoreCase) &&
            decorator.Expression is Expr.Call call &&
            call.Arguments.Count > 0 &&
            call.Arguments[0] is Expr.Literal literal &&
            literal.Value is string typeName)
        {
            var type = Type.GetType(typeName);
            if (type != null && typeof(Attribute).IsAssignableFrom(type))
            {
                return CreateAttributeBuilder(type, decorator);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the name of a decorator from its expression.
    /// </summary>
    private static string? GetDecoratorName(Expr expr)
    {
        return expr switch
        {
            Expr.Variable variable => variable.Name.Lexeme,
            Expr.Call call => GetDecoratorName(call.Callee),
            Expr.Get get => get.Name.Lexeme,
            _ => null
        };
    }

    /// <summary>
    /// Creates a CustomAttributeBuilder for the given attribute type and decorator.
    /// </summary>
    private static CustomAttributeBuilder CreateAttributeBuilder(Type attributeType, Decorator decorator)
    {
        // Get constructor arguments from decorator call
        var args = GetDecoratorArguments(decorator);

        // Try to find a matching constructor
        var constructors = attributeType.GetConstructors();

        // First, try parameterless constructor
        var defaultCtor = constructors.FirstOrDefault(c => c.GetParameters().Length == 0);
        if (defaultCtor != null && args.Length == 0)
        {
            return new CustomAttributeBuilder(defaultCtor, []);
        }

        // Try constructor with string parameter (common for Obsolete)
        if (args.Length > 0 && args[0] is string)
        {
            var stringCtor = constructors.FirstOrDefault(c =>
            {
                var p = c.GetParameters();
                return p.Length >= 1 && p[0].ParameterType == typeof(string);
            });

            if (stringCtor != null)
            {
                var ctorParams = stringCtor.GetParameters();
                var ctorArgs = new object?[ctorParams.Length];

                for (int i = 0; i < ctorParams.Length && i < args.Length; i++)
                {
                    ctorArgs[i] = ConvertArgument(args[i], ctorParams[i].ParameterType);
                }

                // Fill remaining with defaults
                for (int i = args.Length; i < ctorParams.Length; i++)
                {
                    ctorArgs[i] = ctorParams[i].HasDefaultValue
                        ? ctorParams[i].DefaultValue
                        : GetDefaultValue(ctorParams[i].ParameterType);
                }

                return new CustomAttributeBuilder(stringCtor, ctorArgs!);
            }
        }

        // Fall back to parameterless constructor if available
        if (defaultCtor != null)
        {
            return new CustomAttributeBuilder(defaultCtor, []);
        }

        // Can't create attribute - return null
        return null!;
    }

    /// <summary>
    /// Extracts arguments from a decorator expression.
    /// </summary>
    private static object?[] GetDecoratorArguments(Decorator decorator)
    {
        if (decorator.Expression is not Expr.Call call)
            return [];

        List<object?> args = [];
        foreach (var arg in call.Arguments)
        {
            if (arg is Expr.Literal literal)
            {
                args.Add(literal.Value);
            }
            else
            {
                args.Add(null);
            }
        }
        return args.ToArray();
    }

    /// <summary>
    /// Converts a value to the target type.
    /// </summary>
    private static object? ConvertArgument(object? value, Type targetType)
    {
        if (value == null)
            return GetDefaultValue(targetType);

        if (targetType.IsAssignableFrom(value.GetType()))
            return value;

        if (targetType == typeof(bool) && value is double d)
            return d != 0;

        if (targetType == typeof(int) && value is double num)
            return (int)num;

        return value;
    }

    /// <summary>
    /// Gets the default value for a type.
    /// </summary>
    private static object? GetDefaultValue(Type type)
    {
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        return null;
    }
}
