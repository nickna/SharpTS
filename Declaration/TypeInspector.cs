using System.Reflection;

namespace SharpTS.Declaration;

/// <summary>
/// Metadata about the [Obsolete] attribute on a member or type.
/// </summary>
public record ObsoleteMetadata(string? Message, bool IsError);

/// <summary>
/// Metadata about a .NET type extracted via reflection.
/// </summary>
public record TypeMetadata(
    string FullName,
    string SimpleName,
    bool IsStatic,
    bool IsAbstract,
    bool IsInterface,
    bool IsEnum,
    List<MethodMetadata> Methods,
    List<MethodMetadata> StaticMethods,
    List<PropertyMetadata> Properties,
    List<PropertyMetadata> StaticProperties,
    List<ConstructorMetadata> Constructors,
    List<EnumMemberMetadata> EnumMembers,
    ObsoleteMetadata? Obsolete = null,
    bool IsNested = false,
    string? DeclaringTypeName = null
);

public record MethodMetadata(
    string Name,
    string TypeScriptName,
    Type ReturnType,
    List<ParameterMetadata> Parameters,
    ObsoleteMetadata? Obsolete = null
);

public record PropertyMetadata(
    string Name,
    string TypeScriptName,
    Type PropertyType,
    bool CanRead,
    bool CanWrite,
    ObsoleteMetadata? Obsolete = null
);

public record ConstructorMetadata(
    List<ParameterMetadata> Parameters,
    ObsoleteMetadata? Obsolete = null
);

public record ParameterMetadata(
    string Name,
    Type ParameterType,
    bool IsOptional,
    object? DefaultValue
);

public record EnumMemberMetadata(
    string Name,
    object Value
);

/// <summary>
/// Inspects .NET types via reflection to extract metadata for declaration generation.
/// </summary>
public class TypeInspector
{
    /// <summary>
    /// Extracts metadata from a .NET type.
    /// </summary>
    public TypeMetadata Inspect(Type type)
    {
        var methods = new List<MethodMetadata>();
        var staticMethods = new List<MethodMetadata>();
        var properties = new List<PropertyMetadata>();
        var staticProperties = new List<PropertyMetadata>();
        var constructors = new List<ConstructorMetadata>();
        var enumMembers = new List<EnumMemberMetadata>();

        // Handle enum types
        if (type.IsEnum)
        {
            foreach (var name in Enum.GetNames(type))
            {
                var value = Enum.Parse(type, name);
                enumMembers.Add(new EnumMemberMetadata(name, Convert.ToInt64(value)));
            }

            return new TypeMetadata(
                type.FullName ?? type.Name,
                type.Name,
                IsStatic: false,
                IsAbstract: false,
                IsInterface: false,
                IsEnum: true,
                Methods: [],
                StaticMethods: [],
                Properties: [],
                StaticProperties: [],
                Constructors: [],
                EnumMembers: enumMembers,
                Obsolete: ExtractObsoleteInfo(type),
                IsNested: type.IsNested,
                DeclaringTypeName: type.DeclaringType?.Name
            );
        }

        // Extract constructors
        if (!type.IsAbstract && !type.IsInterface)
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                constructors.Add(ExtractConstructor(ctor));
            }
        }

        // Extract instance methods
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (ShouldIncludeMethod(method))
            {
                methods.Add(ExtractMethod(method));
            }
        }

        // Extract static methods
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (ShouldIncludeMethod(method))
            {
                staticMethods.Add(ExtractMethod(method));
            }
        }

        // Extract instance properties
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            properties.Add(ExtractProperty(prop));
        }

        // Extract static properties
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            staticProperties.Add(ExtractProperty(prop));
        }

        bool isStatic = type.IsAbstract && type.IsSealed; // Static classes in C#

        return new TypeMetadata(
            type.FullName ?? type.Name,
            type.Name,
            IsStatic: isStatic,
            IsAbstract: type.IsAbstract && !isStatic,
            IsInterface: type.IsInterface,
            IsEnum: false,
            Methods: methods,
            StaticMethods: staticMethods,
            Properties: properties,
            StaticProperties: staticProperties,
            Constructors: constructors,
            EnumMembers: [],
            Obsolete: ExtractObsoleteInfo(type),
            IsNested: type.IsNested,
            DeclaringTypeName: type.DeclaringType?.Name
        );
    }

    private static bool ShouldIncludeMethod(MethodInfo method)
    {
        // Exclude property accessors
        if (method.IsSpecialName)
            return false;

        // Exclude methods inherited from Object (unless they're overridden)
        if (method.DeclaringType == typeof(object))
            return false;

        // Exclude generic method definitions for MVP (no generic support)
        if (method.IsGenericMethodDefinition)
            return false;

        return true;
    }

    private MethodMetadata ExtractMethod(MethodInfo method)
    {
        var parameters = method.GetParameters()
            .Select(p => new ParameterMetadata(
                p.Name ?? $"arg{p.Position}",
                p.ParameterType,
                p.IsOptional,
                p.HasDefaultValue ? p.DefaultValue : null
            ))
            .ToList();

        return new MethodMetadata(
            method.Name,
            DotNetTypeMapper.ToTypeScriptMethodName(method.Name),
            method.ReturnType,
            parameters,
            ExtractObsoleteInfo(method)
        );
    }

    private PropertyMetadata ExtractProperty(PropertyInfo property)
    {
        return new PropertyMetadata(
            property.Name,
            DotNetTypeMapper.ToTypeScriptPropertyName(property.Name),
            property.PropertyType,
            property.CanRead,
            property.CanWrite,
            ExtractObsoleteInfo(property)
        );
    }

    private ConstructorMetadata ExtractConstructor(ConstructorInfo ctor)
    {
        var parameters = ctor.GetParameters()
            .Select(p => new ParameterMetadata(
                p.Name ?? $"arg{p.Position}",
                p.ParameterType,
                p.IsOptional,
                p.HasDefaultValue ? p.DefaultValue : null
            ))
            .ToList();

        return new ConstructorMetadata(parameters, ExtractObsoleteInfo(ctor));
    }

    private static ObsoleteMetadata? ExtractObsoleteInfo(MemberInfo member)
    {
        var obsoleteAttr = member.GetCustomAttribute<ObsoleteAttribute>();
        if (obsoleteAttr == null)
            return null;

        return new ObsoleteMetadata(obsoleteAttr.Message, obsoleteAttr.IsError);
    }

    private static ObsoleteMetadata? ExtractObsoleteInfo(Type type)
    {
        var obsoleteAttr = type.GetCustomAttribute<ObsoleteAttribute>();
        if (obsoleteAttr == null)
            return null;

        return new ObsoleteMetadata(obsoleteAttr.Message, obsoleteAttr.IsError);
    }
}
