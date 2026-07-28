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
    string? DeclaringTypeName = null,
    List<FieldMetadata>? Fields = null,
    List<FieldMetadata>? StaticFields = null,
    bool HasEvents = false
)
{
    public List<FieldMetadata> Fields { get; init; } = Fields ?? [];
    public List<FieldMetadata> StaticFields { get; init; } = StaticFields ?? [];
}

public record MethodMetadata(
    string Name,
    string TypeScriptName,
    Type ReturnType,
    List<ParameterMetadata> Parameters,
    ObsoleteMetadata? Obsolete = null,
    List<Type>? GenericParameters = null
);

public record PropertyMetadata(
    string Name,
    string TypeScriptName,
    Type PropertyType,
    bool CanRead,
    bool CanWrite,
    ObsoleteMetadata? Obsolete = null,
    bool IsIndexer = false,
    List<ParameterMetadata>? IndexParameters = null
)
{
    public List<ParameterMetadata> IndexParameters { get; init; } = IndexParameters ?? [];
}

public record FieldMetadata(
    string Name,
    string TypeScriptName,
    Type FieldType,
    bool IsReadonly,
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
    object? DefaultValue,
    bool IsByRef = false,
    bool IsOut = false,
    bool IsIn = false,
    bool IsParams = false
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
    /// <param name="type">The type to inspect.</param>
    /// <param name="includeInherited">
    /// When true, the sweep drops <see cref="BindingFlags.DeclaredOnly"/> and includes members
    /// inherited from base types (including <see cref="object"/>). The runtime interop dispatch
    /// (<c>DotNetTypeRegistry.GetMethods</c>) sees inherited members, so consumers that mirror the
    /// callable surface — the <c>dotnet:</c> import synthesizer — must pass true. The declaration
    /// generator keeps the declared-only default so emitted declarations stay minimal.
    /// </param>
    public TypeMetadata Inspect(Type type, bool includeInherited = false)
    {
        var methods = new List<MethodMetadata>();
        var staticMethods = new List<MethodMetadata>();
        var properties = new List<PropertyMetadata>();
        var staticProperties = new List<PropertyMetadata>();
        var constructors = new List<ConstructorMetadata>();
        var enumMembers = new List<EnumMemberMetadata>();
        var fields = new List<FieldMetadata>();
        var staticFields = new List<FieldMetadata>();

        var declaredOnly = includeInherited ? BindingFlags.Default : BindingFlags.DeclaredOnly;

        // Handle enum types
        if (type.IsEnum)
        {
            foreach (var name in Enum.GetNames(type))
            {
                var value = Enum.Parse(type, name);
                enumMembers.Add(new EnumMemberMetadata(name, Convert.ToInt64(value)));
            }

            if (includeInherited)
            {
                // Enum values are callable at runtime (toString, hasFlag, …); surface those
                // instance methods for consumers that mirror the callable surface.
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (ShouldIncludeMethod(method, includeInherited))
                        methods.Add(ExtractMethod(method));
                }
            }

            return new TypeMetadata(
                type.FullName ?? type.Name,
                type.Name,
                IsStatic: false,
                IsAbstract: false,
                IsInterface: false,
                IsEnum: true,
                Methods: methods,
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
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | declaredOnly))
        {
            if (ShouldIncludeMethod(method, includeInherited))
            {
                methods.Add(ExtractMethod(method));
            }
        }

        // Extract static methods
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | declaredOnly))
        {
            if (ShouldIncludeMethod(method, includeInherited))
            {
                staticMethods.Add(ExtractMethod(method));
            }
        }

        // Extract instance properties
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | declaredOnly))
        {
            properties.Add(ExtractProperty(prop));
        }

        // Extract static properties
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static | declaredOnly))
        {
            staticProperties.Add(ExtractProperty(prop));
        }

        // Extract public fields (e.g. Guid.Empty, TimeSpan.Zero). The runtime member lookup
        // (DotNetTypeRegistry.GetPropertyOrField) resolves fields alongside properties.
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | declaredOnly))
        {
            fields.Add(ExtractField(field));
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | declaredOnly))
        {
            staticFields.Add(ExtractField(field));
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
            DeclaringTypeName: type.DeclaringType?.Name,
            Fields: fields,
            StaticFields: staticFields,
            HasEvents: type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length > 0
        );
    }

    private static bool ShouldIncludeMethod(MethodInfo method, bool includeInherited = false)
    {
        // Exclude property accessors and operators
        if (method.IsSpecialName)
            return false;

        // Exclude methods inherited from Object (unless they're overridden). When mirroring the
        // full callable surface, keep them — toString()/getHashCode() work at runtime.
        if (!includeInherited && method.DeclaringType == typeof(object))
            return false;

        return true;
    }

    /// <summary>
    /// Maps a reflected <see cref="ParameterInfo"/> to <see cref="ParameterMetadata"/>, capturing
    /// the by-ref (ref/out/in) shape faithfully so the discovery tool can render and classify it.
    /// </summary>
    private static ParameterMetadata ToParameterMetadata(ParameterInfo p) => new(
        p.Name ?? $"arg{p.Position}",
        p.ParameterType,
        p.IsOptional,
        p.HasDefaultValue ? p.DefaultValue : null,
        IsByRef: p.ParameterType.IsByRef,
        // `out` and `in` are both by-ref; distinguish for a faithful signature.
        IsOut: p.IsOut,
        IsIn: p.ParameterType.IsByRef && p.IsIn,
        IsParams: p.IsDefined(typeof(ParamArrayAttribute), inherit: false)
    );

    private MethodMetadata ExtractMethod(MethodInfo method)
    {
        var parameters = method.GetParameters()
            .Select(ToParameterMetadata)
            .ToList();

        return new MethodMetadata(
            method.Name,
            DotNetTypeMapper.ToTypeScriptMethodName(method.Name),
            method.ReturnType,
            parameters,
            ExtractObsoleteInfo(method),
            method.IsGenericMethodDefinition
                ? method.GetGenericArguments().ToList()
                : null
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
            ExtractObsoleteInfo(property),
            IsIndexer: property.GetIndexParameters().Length > 0,
            IndexParameters: property.GetIndexParameters().Select(ToParameterMetadata).ToList()
        );
    }

    private static FieldMetadata ExtractField(FieldInfo field)
    {
        return new FieldMetadata(
            field.Name,
            DotNetTypeMapper.ToTypeScriptPropertyName(field.Name),
            field.FieldType,
            IsReadonly: field.IsInitOnly || field.IsLiteral,
            ExtractObsoleteInfo(field)
        );
    }

    private ConstructorMetadata ExtractConstructor(ConstructorInfo ctor)
    {
        var parameters = ctor.GetParameters()
            .Select(ToParameterMetadata)
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
