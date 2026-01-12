using System.Reflection;
using SharpTS.Compilation;
using SharpTS.LspBridge.Protocol;

namespace SharpTS.LspBridge.Handlers;

/// <summary>
/// Handles the get-attribute-info command to return constructor and property information.
/// </summary>
public sealed class GetAttributeInfoHandler(AssemblyReferenceLoader loader) : ICommandHandler
{
    public BridgeResponse Handle(BridgeRequest request)
    {
        var typeName = request.GetStringArgument("typeName");
        if (string.IsNullOrEmpty(typeName))
        {
            return BridgeResponse.Error("typeName argument is required");
        }

        // Try to resolve the type
        var type = loader.TryResolve(typeName);
        if (type == null && !typeName.EndsWith("Attribute", StringComparison.Ordinal))
        {
            type = loader.TryResolve(typeName + "Attribute");
        }

        if (type == null)
        {
            return BridgeResponse.Error($"Type not found: {typeName}");
        }

        // Get constructors
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(ctor => new ConstructorInfo
            {
                Parameters = ctor.GetParameters().Select(p => new ParameterInfo
                {
                    Name = p.Name ?? $"arg{p.Position}",
                    Type = FormatTypeName(p.ParameterType),
                    IsOptional = p.IsOptional,
                    DefaultValue = p.HasDefaultValue ? FormatDefaultValue(p.DefaultValue) : null
                }).ToList()
            })
            .ToList();

        // Get settable properties (for named arguments)
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetSetMethod(true)?.IsPublic == true)
            .Select(p => new PropertyInfo
            {
                Name = p.Name,
                Type = FormatTypeName(p.PropertyType)
            })
            .ToList();

        return BridgeResponse.Ok(new
        {
            fullName = type.FullName,
            constructors,
            properties
        });
    }

    private static string FormatTypeName(Type type)
    {
        // Handle nullable value types
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
            return FormatTypeName(underlying) + "?";

        // Handle arrays
        if (type.IsArray)
            return FormatTypeName(type.GetElementType()!) + "[]";

        // Handle common types with TypeScript-friendly names
        return type.FullName switch
        {
            "System.String" => "string",
            "System.Int32" => "number",
            "System.Int64" => "number",
            "System.Int16" => "number",
            "System.Byte" => "number",
            "System.Single" => "number",
            "System.Double" => "number",
            "System.Decimal" => "number",
            "System.Boolean" => "boolean",
            "System.Object" => "any",
            "System.Type" => "Type",
            _ => type.Name
        };
    }

    private static string? FormatDefaultValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            _ => value.ToString()
        };
    }

    private sealed class ConstructorInfo
    {
        public List<ParameterInfo> Parameters { get; init; } = [];
    }

    private sealed class ParameterInfo
    {
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public bool IsOptional { get; init; }
        public string? DefaultValue { get; init; }
    }

    private sealed class PropertyInfo
    {
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
    }
}
