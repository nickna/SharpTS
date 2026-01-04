using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Categorizes enum types by their member value types.
/// </summary>
public enum EnumKind { Numeric, String, Heterogeneous }

/// <summary>
/// Base record for compile-time type representations.
/// </summary>
/// <remarks>
/// Used by <see cref="TypeChecker"/> during static analysis to represent and compare types.
/// Nested records define specific type kinds: Primitive (string, number, boolean), Function,
/// Class, Interface, Enum, Instance (class instance), Array, Tuple, Record (object literal),
/// Void, Any, Union, Literal (string/number/boolean literals), Unknown, and Never.
/// Completely separate from runtime values—these are compile-time-only constructs.
/// </remarks>
/// <seealso cref="TypeChecker"/>
/// <seealso cref="TypeEnvironment"/>
public abstract record TypeInfo
{
    public record Primitive(TokenType Type) : TypeInfo
    {
        public override string ToString() => Type.ToString().Replace("TYPE_", "").ToLower();
    }
    
    public record Function(List<TypeInfo> ParamTypes, TypeInfo ReturnType, int RequiredParams = -1, bool HasRestParam = false) : TypeInfo
    {
        // RequiredParams defaults to -1 meaning all params are required (for backwards compat)
        public int MinArity => RequiredParams < 0 ? ParamTypes.Count : RequiredParams;
        public override string ToString() =>
            $"({string.Join(", ", ParamTypes)}) => {ReturnType}";
    }

    /// <summary>
    /// Represents an overloaded function with multiple callable signatures and a single implementation.
    /// TypeScript-style overloading: multiple signatures (compile-time) with one implementation (runtime).
    /// </summary>
    public record OverloadedFunction(
        List<TypeInfo.Function> Signatures,   // Callable overload signatures for type checking
        TypeInfo.Function Implementation      // The actual implementation's signature
    ) : TypeInfo
    {
        public override string ToString() =>
            $"overloaded ({Signatures.Count} signatures) => {Implementation.ReturnType}";
    }

    public record Class(
        string Name,
        TypeInfo.Class? Superclass,
        Dictionary<string, TypeInfo> Methods,  // Can be Function or OverloadedFunction
        Dictionary<string, TypeInfo> StaticMethods,  // Can be Function or OverloadedFunction
        Dictionary<string, TypeInfo> StaticProperties,
        Dictionary<string, AccessModifier>? MethodAccess = null,
        Dictionary<string, AccessModifier>? FieldAccess = null,
        HashSet<string>? ReadonlyFields = null,
        Dictionary<string, TypeInfo>? Getters = null,
        Dictionary<string, TypeInfo>? Setters = null,
        Dictionary<string, TypeInfo>? FieldTypes = null,
        bool IsAbstract = false,
        HashSet<string>? AbstractMethods = null,
        HashSet<string>? AbstractGetters = null,
        HashSet<string>? AbstractSetters = null) : TypeInfo
    {
        public Dictionary<string, AccessModifier> MethodAccessModifiers => MethodAccess ?? [];
        public Dictionary<string, AccessModifier> FieldAccessModifiers => FieldAccess ?? [];
        public HashSet<string> ReadonlyFieldSet => ReadonlyFields ?? [];
        public Dictionary<string, TypeInfo> GetterTypes => Getters ?? [];
        public Dictionary<string, TypeInfo> SetterTypes => Setters ?? [];
        public Dictionary<string, TypeInfo> DeclaredFieldTypes => FieldTypes ?? [];
        public HashSet<string> AbstractMethodSet => AbstractMethods ?? [];
        public HashSet<string> AbstractGetterSet => AbstractGetters ?? [];
        public HashSet<string> AbstractSetterSet => AbstractSetters ?? [];
        public override string ToString() => IsAbstract ? $"abstract class {Name}" : $"class {Name}";
    }

    public record Interface(
        string Name,
        Dictionary<string, TypeInfo> Members,
        HashSet<string>? OptionalMembers = null,
        TypeInfo? StringIndexType = null,
        TypeInfo? NumberIndexType = null,
        TypeInfo? SymbolIndexType = null
    ) : TypeInfo
    {
        public HashSet<string> OptionalMemberSet => OptionalMembers ?? [];
        public bool HasIndexSignature => StringIndexType != null || NumberIndexType != null || SymbolIndexType != null;
        public override string ToString() => $"interface {Name}";
    }

    public record Enum(string Name, Dictionary<string, object> Members, EnumKind Kind, bool IsConst = false) : TypeInfo
    {
        // Only create reverse mapping for numeric members (not available for const enums)
        public Dictionary<double, string> ReverseMembers =>
            Members.Where(kvp => kvp.Value is double)
                   .ToDictionary(kvp => (double)kvp.Value, kvp => kvp.Key);
        public override string ToString() => IsConst ? $"const enum {Name}" : $"enum {Name}";
    }

    /// <summary>
    /// Represents an instance of a class. ClassType can be either a regular Class
    /// or an InstantiatedGeneric (for generic class instances like Box&lt;number&gt;).
    /// </summary>
    public record Instance(TypeInfo ClassType) : TypeInfo
    {
        public override string ToString() => ClassType switch
        {
            Class c => c.Name,
            InstantiatedGeneric ig => ig.ToString(),
            _ => "instance"
        };
    }

    public record Array(TypeInfo ElementType) : TypeInfo
    {
        public override string ToString() => ElementType is Union
            ? $"({ElementType})[]"
            : $"{ElementType}[]";
    }

    public record Tuple(
        List<TypeInfo> ElementTypes,
        int RequiredCount,
        TypeInfo? RestElementType = null
    ) : TypeInfo
    {
        public int MinLength => RequiredCount;
        public int? MaxLength => RestElementType != null ? null : ElementTypes.Count;
        public bool HasRest => RestElementType != null;

        public override string ToString()
        {
            var parts = new List<string>();
            for (int i = 0; i < ElementTypes.Count; i++)
            {
                bool isOptional = i >= RequiredCount;
                parts.Add(isOptional ? $"{ElementTypes[i]}?" : ElementTypes[i].ToString());
            }
            if (RestElementType != null)
                parts.Add($"...{RestElementType}[]");
            return $"[{string.Join(", ", parts)}]";
        }
    }

    public record Record(
        Dictionary<string, TypeInfo> Fields,
        TypeInfo? StringIndexType = null,
        TypeInfo? NumberIndexType = null,
        TypeInfo? SymbolIndexType = null
    ) : TypeInfo
    {
        public bool HasIndexSignature => StringIndexType != null || NumberIndexType != null || SymbolIndexType != null;
        public override string ToString() => $"{{ {string.Join(", ", Fields.Select(f => $"{f.Key}: {f.Value}"))} }}";
    }
    
    public record Void() : TypeInfo
    {
        public override string ToString() => "void";
    }

    public record Any() : TypeInfo
    {
        public override string ToString() => "any";
    }

    public record Null() : TypeInfo
    {
        public override string ToString() => "null";
    }

    public record Unknown() : TypeInfo
    {
        public override string ToString() => "unknown";
    }

    public record Never() : TypeInfo
    {
        public override string ToString() => "never";
    }

    public record Symbol() : TypeInfo
    {
        public override string ToString() => "symbol";
    }

    public record StringLiteral(string Value) : TypeInfo
    {
        public override string ToString() => $"\"{Value}\"";
    }

    public record NumberLiteral(double Value) : TypeInfo
    {
        public override string ToString() => Value.ToString();
    }

    public record BooleanLiteral(bool Value) : TypeInfo
    {
        public override string ToString() => Value ? "true" : "false";
    }

    public record Union(List<TypeInfo> Types) : TypeInfo
    {
        public List<TypeInfo> FlattenedTypes => Types
            .SelectMany(t => t is Union u ? u.FlattenedTypes : [t])
            .Distinct(TypeInfoEqualityComparer.Instance)
            .ToList();

        public bool ContainsNull => FlattenedTypes.Any(t => t is Null);

        public override string ToString() => string.Join(" | ", FlattenedTypes);
    }

    /// <summary>
    /// Represents an intersection type (A &amp; B). A value of this type must satisfy ALL constituent types.
    /// </summary>
    /// <remarks>
    /// Intersection types combine multiple types into one:
    /// - For objects: merges all properties from all types
    /// - For primitives: conflicting primitives produce 'never' (e.g., string &amp; number = never)
    /// - never &amp; T = never, any &amp; T = any, unknown &amp; T = T
    /// </remarks>
    public record Intersection(List<TypeInfo> Types) : TypeInfo
    {
        /// <summary>
        /// Returns a flattened list of all intersected types, collapsing nested intersections.
        /// Applies simplification: never absorbs all, any absorbs all, unknown is removed (identity).
        /// </summary>
        public List<TypeInfo> FlattenedTypes
        {
            get
            {
                var flattened = Types
                    .SelectMany(t => t is Intersection i ? i.FlattenedTypes : [t])
                    .Distinct(TypeInfoEqualityComparer.Instance)
                    .ToList();

                // never absorbs everything in intersection
                if (flattened.Any(t => t is Never))
                    return [new Never()];

                // any absorbs in intersection
                if (flattened.Any(t => t is Any))
                    return [new Any()];

                // unknown is identity element - remove it
                flattened = flattened.Where(t => t is not Unknown).ToList();
                if (flattened.Count == 0)
                    return [new Unknown()];

                return flattened;
            }
        }

        public override string ToString() => string.Join(" & ", FlattenedTypes);
    }

    // Type parameter placeholder (T in <T>)
    public record TypeParameter(string Name, TypeInfo? Constraint = null) : TypeInfo
    {
        public override string ToString() => Constraint != null
            ? $"{Name} extends {Constraint}" : Name;
    }

    // Generic function (not yet instantiated)
    public record GenericFunction(
        List<TypeParameter> TypeParams,
        List<TypeInfo> ParamTypes,
        TypeInfo ReturnType,
        int RequiredParams = -1,
        bool HasRestParam = false
    ) : TypeInfo
    {
        public int MinArity => RequiredParams < 0 ? ParamTypes.Count : RequiredParams;
        public override string ToString() =>
            $"<{string.Join(", ", TypeParams)}>({string.Join(", ", ParamTypes)}) => {ReturnType}";
    }

    // Generic class (not yet instantiated)
    public record GenericClass(
        string Name,
        List<TypeParameter> TypeParams,
        TypeInfo.Class? Superclass,
        Dictionary<string, TypeInfo> Methods,  // Can be Function or OverloadedFunction
        Dictionary<string, TypeInfo> StaticMethods,  // Can be Function or OverloadedFunction
        Dictionary<string, TypeInfo> StaticProperties,
        Dictionary<string, AccessModifier>? MethodAccess = null,
        Dictionary<string, AccessModifier>? FieldAccess = null,
        HashSet<string>? ReadonlyFields = null,
        Dictionary<string, TypeInfo>? Getters = null,
        Dictionary<string, TypeInfo>? Setters = null,
        Dictionary<string, TypeInfo>? FieldTypes = null,
        bool IsAbstract = false,
        HashSet<string>? AbstractMethods = null,
        HashSet<string>? AbstractGetters = null,
        HashSet<string>? AbstractSetters = null
    ) : TypeInfo
    {
        public Dictionary<string, AccessModifier> MethodAccessModifiers => MethodAccess ?? [];
        public Dictionary<string, AccessModifier> FieldAccessModifiers => FieldAccess ?? [];
        public HashSet<string> ReadonlyFieldSet => ReadonlyFields ?? [];
        public Dictionary<string, TypeInfo> GetterTypes => Getters ?? [];
        public Dictionary<string, TypeInfo> SetterTypes => Setters ?? [];
        public Dictionary<string, TypeInfo> DeclaredFieldTypes => FieldTypes ?? [];
        public HashSet<string> AbstractMethodSet => AbstractMethods ?? [];
        public HashSet<string> AbstractGetterSet => AbstractGetters ?? [];
        public HashSet<string> AbstractSetterSet => AbstractSetters ?? [];
        public override string ToString() => IsAbstract
            ? $"abstract class {Name}<{string.Join(", ", TypeParams)}>"
            : $"class {Name}<{string.Join(", ", TypeParams)}>";
    }

    // Generic interface (not yet instantiated)
    public record GenericInterface(
        string Name,
        List<TypeParameter> TypeParams,
        Dictionary<string, TypeInfo> Members,
        HashSet<string>? OptionalMembers = null
    ) : TypeInfo
    {
        public HashSet<string> OptionalMemberSet => OptionalMembers ?? [];
        public override string ToString() => $"interface {Name}<{string.Join(", ", TypeParams)}>";
    }

    // Instantiated generic type (e.g., Box<number>)
    public record InstantiatedGeneric(
        TypeInfo GenericDefinition,
        List<TypeInfo> TypeArguments
    ) : TypeInfo
    {
        public override string ToString()
        {
            var baseName = GenericDefinition switch
            {
                GenericClass gc => gc.Name,
                GenericInterface gi => gi.Name,
                GenericFunction => "Function",
                _ => "Generic"
            };
            return $"{baseName}<{string.Join(", ", TypeArguments)}>";
        }
    }
}

public class TypeInfoEqualityComparer : IEqualityComparer<TypeInfo>
{
    public static readonly TypeInfoEqualityComparer Instance = new();
    public bool Equals(TypeInfo? x, TypeInfo? y) => x?.ToString() == y?.ToString();
    public int GetHashCode(TypeInfo obj) => obj.ToString().GetHashCode();
}
