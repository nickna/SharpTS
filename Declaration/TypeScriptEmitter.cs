using System.Text;

namespace SharpTS.Declaration;

/// <summary>
/// Emits TypeScript declaration code from type metadata.
/// </summary>
public class TypeScriptEmitter
{
    private readonly StringBuilder _sb = new();
    private int _indent = 0;

    /// <summary>
    /// Generates TypeScript declaration for a type.
    /// </summary>
    public string Emit(TypeMetadata metadata)
    {
        _sb.Clear();
        _indent = 0;

        EmitType(metadata);

        return _sb.ToString();
    }

    /// <summary>
    /// Generates TypeScript declarations for multiple types.
    /// </summary>
    public string EmitAll(IEnumerable<TypeMetadata> types, bool groupNestedTypes = false)
    {
        _sb.Clear();
        _indent = 0;

        var typesList = types.ToList();

        if (groupNestedTypes)
        {
            EmitWithNestedTypeGrouping(typesList);
        }
        else
        {
            EmitFlat(typesList);
        }

        return _sb.ToString();
    }

    private void EmitFlat(List<TypeMetadata> types)
    {
        bool first = true;
        foreach (var metadata in types)
        {
            if (!first)
            {
                AppendLine();
            }
            EmitType(metadata);
            first = false;
        }
    }

    private void EmitWithNestedTypeGrouping(List<TypeMetadata> types)
    {
        // Separate nested types from top-level types
        var topLevelTypes = types.Where(t => !t.IsNested).ToList();
        var nestedTypes = types.Where(t => t.IsNested).ToList();

        // Group nested types by their declaring type name
        var nestedByParent = nestedTypes
            .Where(t => t.DeclaringTypeName != null)
            .GroupBy(t => t.DeclaringTypeName!)
            .ToDictionary(g => g.Key, g => g.ToList());

        bool first = true;

        // Emit top-level types
        foreach (var metadata in topLevelTypes)
        {
            if (!first) AppendLine();
            first = false;

            EmitType(metadata);

            // Check if this type has nested types
            if (nestedByParent.TryGetValue(metadata.SimpleName, out var nested))
            {
                AppendLine();
                EmitNamespaceWithNestedTypes(metadata.SimpleName, nested);
            }
        }

        // Emit orphan nested types (whose parent wasn't in the list)
        var emittedParents = topLevelTypes.Select(t => t.SimpleName).ToHashSet();
        var orphanGroups = nestedByParent
            .Where(kvp => !emittedParents.Contains(kvp.Key))
            .ToList();

        foreach (var group in orphanGroups)
        {
            if (!first) AppendLine();
            first = false;

            EmitNamespaceWithNestedTypes(group.Key, group.Value);
        }
    }

    private void EmitNamespaceWithNestedTypes(string parentTypeName, List<TypeMetadata> nestedTypes)
    {
        AppendLine($"export namespace {parentTypeName} {{");
        _indent++;

        bool first = true;
        foreach (var nested in nestedTypes)
        {
            if (!first) AppendLine();
            first = false;

            EmitType(nested);
        }

        _indent--;
        AppendLine("}");
    }

    private void EmitType(TypeMetadata metadata)
    {
        if (metadata.IsEnum)
        {
            EmitEnum(metadata);
            return;
        }

        // Emit @deprecated JSDoc if type is obsolete
        EmitDeprecatedJsDoc(metadata.Obsolete);

        // Emit decorator
        AppendLine($"@DotNetType(\"{metadata.FullName}\")");

        // Emit class declaration
        string classModifier = metadata.IsAbstract ? "abstract " : "";
        string keyword = metadata.IsInterface ? "interface" : "class";
        string exportKeyword = "export";

        if (metadata.IsStatic)
        {
            // Static classes in TypeScript are just classes with static members
            AppendLine($"{exportKeyword} declare class {metadata.SimpleName} {{");
        }
        else
        {
            AppendLine($"{exportKeyword} declare {classModifier}{keyword} {metadata.SimpleName} {{");
        }

        _indent++;

        // Emit constructors (not for interfaces or static classes)
        if (!metadata.IsInterface && !metadata.IsStatic)
        {
            foreach (var ctor in metadata.Constructors)
            {
                EmitConstructor(ctor);
            }
        }

        // Emit static properties
        foreach (var prop in metadata.StaticProperties)
        {
            EmitProperty(prop, isStatic: true);
        }

        // Emit instance properties (not for static classes)
        if (!metadata.IsStatic)
        {
            foreach (var prop in metadata.Properties)
            {
                EmitProperty(prop, isStatic: false);
            }
        }

        // Emit static methods
        foreach (var method in metadata.StaticMethods)
        {
            EmitMethod(method, isStatic: true, isInterface: metadata.IsInterface);
        }

        // Emit instance methods (not for static classes)
        if (!metadata.IsStatic)
        {
            foreach (var method in metadata.Methods)
            {
                EmitMethod(method, isStatic: false, isInterface: metadata.IsInterface);
            }
        }

        _indent--;
        AppendLine("}");
    }

    private void EmitEnum(TypeMetadata metadata)
    {
        // Emit @deprecated JSDoc if enum is obsolete
        EmitDeprecatedJsDoc(metadata.Obsolete);

        AppendLine($"@DotNetType(\"{metadata.FullName}\")");
        AppendLine($"export declare enum {metadata.SimpleName} {{");
        _indent++;

        for (int i = 0; i < metadata.EnumMembers.Count; i++)
        {
            var member = metadata.EnumMembers[i];
            string suffix = i < metadata.EnumMembers.Count - 1 ? "," : "";
            AppendLine($"{member.Name} = {member.Value}{suffix}");
        }

        _indent--;
        AppendLine("}");
    }

    private void EmitConstructor(ConstructorMetadata ctor)
    {
        EmitDeprecatedJsDoc(ctor.Obsolete);
        var parameters = FormatParameters(ctor.Parameters);
        AppendLine($"constructor({parameters});");
    }

    private void EmitProperty(PropertyMetadata prop, bool isStatic)
    {
        EmitDeprecatedJsDoc(prop.Obsolete);
        string staticModifier = isStatic ? "static " : "";
        string readonlyModifier = prop.CanRead && !prop.CanWrite ? "readonly " : "";
        string tsType = DotNetTypeMapper.MapToTypeScript(prop.PropertyType);

        AppendLine($"{staticModifier}{readonlyModifier}{prop.TypeScriptName}: {tsType};");
    }

    private void EmitMethod(MethodMetadata method, bool isStatic, bool isInterface)
    {
        EmitDeprecatedJsDoc(method.Obsolete);
        string staticModifier = isStatic ? "static " : "";
        var parameters = FormatParameters(method.Parameters);
        string returnType = DotNetTypeMapper.MapToTypeScript(method.ReturnType);

        if (isInterface)
        {
            // Interface methods don't have static modifier
            AppendLine($"{method.TypeScriptName}({parameters}): {returnType};");
        }
        else
        {
            AppendLine($"{staticModifier}{method.TypeScriptName}({parameters}): {returnType};");
        }
    }

    private string FormatParameters(List<ParameterMetadata> parameters)
    {
        if (parameters.Count == 0)
            return "";

        var parts = new List<string>();
        foreach (var param in parameters)
        {
            string tsType = DotNetTypeMapper.MapToTypeScript(param.ParameterType);
            string optionalMark = param.IsOptional ? "?" : "";

            // Use camelCase for parameter names
            string paramName = DotNetTypeMapper.ToTypeScriptPropertyName(param.Name);

            parts.Add($"{paramName}{optionalMark}: {tsType}");
        }

        return string.Join(", ", parts);
    }

    private void EmitDeprecatedJsDoc(ObsoleteMetadata? obsolete)
    {
        if (obsolete == null)
            return;

        if (string.IsNullOrEmpty(obsolete.Message))
        {
            AppendLine("/** @deprecated */");
        }
        else
        {
            AppendLine($"/** @deprecated {obsolete.Message} */");
        }
    }

    private void Append(string text)
    {
        _sb.Append(text);
    }

    private void AppendLine(string text = "")
    {
        if (!string.IsNullOrEmpty(text))
        {
            _sb.Append(new string(' ', _indent * 4));
            _sb.AppendLine(text);
        }
        else
        {
            _sb.AppendLine();
        }
    }
}
