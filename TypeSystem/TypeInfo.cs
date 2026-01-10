using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Categorizes enum types by their member value types.
/// </summary>
public enum EnumKind { Numeric, String, Heterogeneous }

/// <summary>
/// The target of a decorator application.
/// </summary>
public enum DecoratorTarget
{
    Class,
    Method,
    StaticMethod,
    Getter,
    Setter,
    Field,
    StaticField,
    Parameter
}

/// <summary>
/// Modifiers for mapped type properties (+/-readonly, +/-optional).
/// </summary>
[Flags]
public enum MappedTypeModifiers
{
    None = 0,
    AddReadonly = 1,       // +readonly
    RemoveReadonly = 2,    // -readonly
    AddOptional = 4,       // +? or just ?
    RemoveOptional = 8     // -?
}

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
    
    /// <summary>
    /// Function type with optional explicit this type annotation.
    /// ThisType specifies the expected type of 'this' within the function body.
    /// </summary>
    public record Function(List<TypeInfo> ParamTypes, TypeInfo ReturnType, int RequiredParams = -1, bool HasRestParam = false, TypeInfo? ThisType = null) : TypeInfo
    {
        // RequiredParams defaults to -1 meaning all params are required (for backwards compat)
        public int MinArity => RequiredParams < 0 ? ParamTypes.Count : RequiredParams;
        public override string ToString() =>
            ThisType != null
                ? $"(this: {ThisType}, {string.Join(", ", ParamTypes)}) => {ReturnType}"
                : $"({string.Join(", ", ParamTypes)}) => {ReturnType}";
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
        FrozenDictionary<string, TypeInfo> Methods,  // Can be Function or OverloadedFunction
        FrozenDictionary<string, TypeInfo> StaticMethods,  // Can be Function or OverloadedFunction
        FrozenDictionary<string, TypeInfo> StaticProperties,
        FrozenDictionary<string, AccessModifier> MethodAccess,
        FrozenDictionary<string, AccessModifier> FieldAccess,
        FrozenSet<string> ReadonlyFields,
        FrozenDictionary<string, TypeInfo> Getters,
        FrozenDictionary<string, TypeInfo> Setters,
        FrozenDictionary<string, TypeInfo> FieldTypes,
        bool IsAbstract = false,
        FrozenSet<string>? AbstractMethods = null,
        FrozenSet<string>? AbstractGetters = null,
        FrozenSet<string>? AbstractSetters = null) : TypeInfo
    {
        // Keep nullable with defaults for backward compatibility during migration
        public FrozenSet<string> AbstractMethodSet => AbstractMethods ?? FrozenSet<string>.Empty;
        public FrozenSet<string> AbstractGetterSet => AbstractGetters ?? FrozenSet<string>.Empty;
        public FrozenSet<string> AbstractSetterSet => AbstractSetters ?? FrozenSet<string>.Empty;
        public override string ToString() => IsAbstract ? $"abstract class {Name}" : $"class {Name}";
    }

    public record Interface(
        string Name,
        FrozenDictionary<string, TypeInfo> Members,
        FrozenSet<string> OptionalMembers,
        TypeInfo? StringIndexType = null,
        TypeInfo? NumberIndexType = null,
        TypeInfo? SymbolIndexType = null
    ) : TypeInfo
    {
        public bool HasIndexSignature => StringIndexType != null || NumberIndexType != null || SymbolIndexType != null;
        public override string ToString() => $"interface {Name}";
    }

    public record Enum(string Name, FrozenDictionary<string, object> Members, EnumKind Kind, bool IsConst = false) : TypeInfo
    {
        // Only create reverse mapping for numeric members (not available for const enums)
        public Dictionary<double, string> ReverseMembers =>
            Members.Where(kvp => kvp.Value is double)
                   .ToDictionary(kvp => (double)kvp.Value, kvp => kvp.Key);
        public override string ToString() => IsConst ? $"const enum {Name}" : $"enum {Name}";
    }

    /// <summary>
    /// Represents a TypeScript namespace type.
    /// Contains types (classes, interfaces, enums, type aliases, nested namespaces)
    /// and values (functions, variables) declared within the namespace.
    /// </summary>
    public record Namespace(
        string Name,
        FrozenDictionary<string, TypeInfo> Types,   // Classes, interfaces, enums, nested namespaces
        FrozenDictionary<string, TypeInfo> Values   // Functions, variables
    ) : TypeInfo
    {
        /// <summary>
        /// Gets a member by name, checking both types and values.
        /// </summary>
        public TypeInfo? GetMember(string name)
        {
            if (Types.TryGetValue(name, out var type)) return type;
            if (Values.TryGetValue(name, out var value)) return value;
            return null;
        }

        public override string ToString() => $"namespace {Name}";
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

    /// <summary>
    /// Represents the Map&lt;K, V&gt; built-in type for key-value collections.
    /// </summary>
    public record Map(TypeInfo KeyType, TypeInfo ValueType) : TypeInfo
    {
        public override string ToString() => $"Map<{KeyType}, {ValueType}>";
    }

    /// <summary>
    /// Represents the Set&lt;T&gt; built-in type for unique value collections.
    /// </summary>
    public record Set(TypeInfo ElementType) : TypeInfo
    {
        public override string ToString() => $"Set<{ElementType}>";
    }

    /// <summary>
    /// Represents an iterator type returned by keys(), values(), entries() methods.
    /// </summary>
    public record Iterator(TypeInfo ElementType) : TypeInfo
    {
        public override string ToString() => $"IterableIterator<{ElementType}>";
    }

    /// <summary>
    /// Represents the WeakMap&lt;K, V&gt; built-in type for weak key-value collections.
    /// Keys must be objects (not primitives). No size property, no iteration.
    /// </summary>
    public record WeakMap(TypeInfo KeyType, TypeInfo ValueType) : TypeInfo
    {
        public override string ToString() => $"WeakMap<{KeyType}, {ValueType}>";
    }

    /// <summary>
    /// Represents the WeakSet&lt;T&gt; built-in type for weak value collections.
    /// Values must be objects (not primitives). No size property, no iteration.
    /// </summary>
    public record WeakSet(TypeInfo ElementType) : TypeInfo
    {
        public override string ToString() => $"WeakSet<{ElementType}>";
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
            List<string> parts = [];
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
        FrozenDictionary<string, TypeInfo> Fields,
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

    public record BigInt() : TypeInfo
    {
        public override string ToString() => "bigint";
    }

    public record Date() : TypeInfo
    {
        public override string ToString() => "Date";
    }

    public record RegExp() : TypeInfo
    {
        public override string ToString() => "RegExp";
    }

    /// <summary>
    /// Represents a Promise type (Promise&lt;T&gt;).
    /// Promise types automatically flatten - Promise&lt;Promise&lt;T&gt;&gt; becomes Promise&lt;T&gt;.
    /// </summary>
    public record Promise(TypeInfo ValueType) : TypeInfo
    {
        public override string ToString() => $"Promise<{ValueType}>";
    }

    /// <summary>
    /// Represents a Generator type (Generator&lt;T&gt;).
    /// Generator functions return Generator objects that yield values of type T.
    /// </summary>
    public record Generator(TypeInfo YieldType) : TypeInfo
    {
        public override string ToString() => $"Generator<{YieldType}>";
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

    /// <summary>
    /// Generic function type with optional explicit this type annotation.
    /// </summary>
    public record GenericFunction(
        List<TypeParameter> TypeParams,
        List<TypeInfo> ParamTypes,
        TypeInfo ReturnType,
        int RequiredParams = -1,
        bool HasRestParam = false,
        TypeInfo? ThisType = null
    ) : TypeInfo
    {
        public int MinArity => RequiredParams < 0 ? ParamTypes.Count : RequiredParams;
        public override string ToString() =>
            ThisType != null
                ? $"<{string.Join(", ", TypeParams)}>(this: {ThisType}, {string.Join(", ", ParamTypes)}) => {ReturnType}"
                : $"<{string.Join(", ", TypeParams)}>({string.Join(", ", ParamTypes)}) => {ReturnType}";
    }

    // Generic class (not yet instantiated)
    public record GenericClass(
        string Name,
        List<TypeParameter> TypeParams,
        TypeInfo.Class? Superclass,
        FrozenDictionary<string, TypeInfo> Methods,  // Can be Function or OverloadedFunction
        FrozenDictionary<string, TypeInfo> StaticMethods,  // Can be Function or OverloadedFunction
        FrozenDictionary<string, TypeInfo> StaticProperties,
        FrozenDictionary<string, AccessModifier> MethodAccess,
        FrozenDictionary<string, AccessModifier> FieldAccess,
        FrozenSet<string> ReadonlyFields,
        FrozenDictionary<string, TypeInfo> Getters,
        FrozenDictionary<string, TypeInfo> Setters,
        FrozenDictionary<string, TypeInfo> FieldTypes,
        bool IsAbstract = false,
        FrozenSet<string>? AbstractMethods = null,
        FrozenSet<string>? AbstractGetters = null,
        FrozenSet<string>? AbstractSetters = null
    ) : TypeInfo
    {
        // Keep nullable with defaults for backward compatibility during migration
        public FrozenSet<string> AbstractMethodSet => AbstractMethods ?? FrozenSet<string>.Empty;
        public FrozenSet<string> AbstractGetterSet => AbstractGetters ?? FrozenSet<string>.Empty;
        public FrozenSet<string> AbstractSetterSet => AbstractSetters ?? FrozenSet<string>.Empty;
        public override string ToString() => IsAbstract
            ? $"abstract class {Name}<{string.Join(", ", TypeParams)}>"
            : $"class {Name}<{string.Join(", ", TypeParams)}>";
    }

    // Generic interface (not yet instantiated)
    public record GenericInterface(
        string Name,
        List<TypeParameter> TypeParams,
        FrozenDictionary<string, TypeInfo> Members,
        FrozenSet<string> OptionalMembers
    ) : TypeInfo
    {
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

    /// <summary>
    /// Represents the keyof T type operator, which extracts property keys as a union of string literals.
    /// For example: keyof { name: string; age: number } = "name" | "age"
    /// </summary>
    public record KeyOf(TypeInfo SourceType) : TypeInfo
    {
        public override string ToString() => $"keyof {SourceType}";
    }

    /// <summary>
    /// Represents a mapped type: { [K in Constraint]: ValueType } with optional modifiers and key remapping.
    /// Lazy evaluation: this type is not expanded until needed for compatibility checks or property access.
    /// </summary>
    /// <param name="ParameterName">The iteration variable name (e.g., K in [K in keyof T])</param>
    /// <param name="Constraint">The type to iterate over (e.g., keyof T or a union of string literals)</param>
    /// <param name="ValueType">The value type for each property (can reference K via indexed access)</param>
    /// <param name="Modifiers">+/- readonly and +/- optional modifiers</param>
    /// <param name="AsClause">Optional key remapping clause (e.g., K as Uppercase&lt;K&gt;)</param>
    public record MappedType(
        string ParameterName,
        TypeInfo Constraint,
        TypeInfo ValueType,
        MappedTypeModifiers Modifiers = MappedTypeModifiers.None,
        TypeInfo? AsClause = null
    ) : TypeInfo
    {
        public override string ToString()
        {
            var readonlyMod = Modifiers.HasFlag(MappedTypeModifiers.AddReadonly) ? "+readonly "
                            : Modifiers.HasFlag(MappedTypeModifiers.RemoveReadonly) ? "-readonly "
                            : "";
            var optionalMod = Modifiers.HasFlag(MappedTypeModifiers.AddOptional) ? "+?"
                            : Modifiers.HasFlag(MappedTypeModifiers.RemoveOptional) ? "-?"
                            : "";
            var asStr = AsClause != null ? $" as {AsClause}" : "";
            return $"{{ {readonlyMod}[{ParameterName} in {Constraint}{asStr}]{optionalMod}: {ValueType} }}";
        }
    }

    /// <summary>
    /// Represents an indexed access type: T[K] where T is an object type and K is a key type.
    /// For example: Person["name"] = string (if Person has name: string)
    /// </summary>
    public record IndexedAccess(TypeInfo ObjectType, TypeInfo IndexType) : TypeInfo
    {
        public override string ToString() => $"{ObjectType}[{IndexType}]";
    }
}

public class TypeInfoEqualityComparer : IEqualityComparer<TypeInfo>
{
    public static readonly TypeInfoEqualityComparer Instance = new();
    public bool Equals(TypeInfo? x, TypeInfo? y) => x?.ToString() == y?.ToString();
    public int GetHashCode(TypeInfo obj) => obj.ToString().GetHashCode();
}
