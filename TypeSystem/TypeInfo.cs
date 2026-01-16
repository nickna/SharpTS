using System.Collections.Frozen;
using System.Text;
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
/// String manipulation operations for intrinsic string types.
/// </summary>
public enum StringManipulation { Uppercase, Lowercase, Capitalize, Uncapitalize }

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

    /// <summary>
    /// Mutable class type used during class declaration checking.
    /// Supports incremental building of methods/fields, then freezing to immutable Class.
    /// This replaces the placeholder class pattern by providing a single object identity
    /// that is registered early and populated during signature collection.
    /// </summary>
    /// <remarks>
    /// Using a record with mutable properties (non-positional) to allow inheritance from TypeInfo.
    /// The collections are mutable during construction and frozen when Freeze() is called.
    /// </remarks>
    public sealed record MutableClass(string Name) : TypeInfo
    {
        public TypeInfo.Class? Superclass { get; set; }
        public Dictionary<string, TypeInfo> Methods { get; } = [];
        public Dictionary<string, TypeInfo> StaticMethods { get; } = [];
        public Dictionary<string, TypeInfo> StaticProperties { get; } = [];
        public Dictionary<string, AccessModifier> MethodAccess { get; } = [];
        public Dictionary<string, AccessModifier> FieldAccess { get; } = [];
        public HashSet<string> ReadonlyFields { get; } = [];
        public Dictionary<string, TypeInfo> Getters { get; } = [];
        public Dictionary<string, TypeInfo> Setters { get; } = [];
        public Dictionary<string, TypeInfo> FieldTypes { get; } = [];
        public bool IsAbstract { get; set; }
        public HashSet<string> AbstractMethods { get; } = [];
        public HashSet<string> AbstractGetters { get; } = [];
        public HashSet<string> AbstractSetters { get; } = [];

        private TypeInfo.Class? _frozen;

        /// <summary>
        /// Freezes the mutable class into an immutable Class.
        /// Idempotent - returns the same frozen instance on subsequent calls.
        /// </summary>
        public TypeInfo.Class Freeze()
        {
            if (_frozen != null) return _frozen;
            _frozen = new TypeInfo.Class(
                Name, Superclass,
                Methods.ToFrozenDictionary(),
                StaticMethods.ToFrozenDictionary(),
                StaticProperties.ToFrozenDictionary(),
                MethodAccess.ToFrozenDictionary(),
                FieldAccess.ToFrozenDictionary(),
                ReadonlyFields.ToFrozenSet(),
                Getters.ToFrozenDictionary(),
                Setters.ToFrozenDictionary(),
                FieldTypes.ToFrozenDictionary(),
                IsAbstract,
                AbstractMethods.Count > 0 ? AbstractMethods.ToFrozenSet() : null,
                AbstractGetters.Count > 0 ? AbstractGetters.ToFrozenSet() : null,
                AbstractSetters.Count > 0 ? AbstractSetters.ToFrozenSet() : null);
            return _frozen;
        }

        /// <summary>Gets the frozen class if available, otherwise null.</summary>
        public TypeInfo.Class? Frozen => _frozen;

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
    /// Represents an external .NET type mapped via @DotNetType decorator.
    /// Used for interop where TypeScript code declares shapes that map to existing .NET types.
    /// </summary>
    public record ExternalDotNetType(
        string TypeScriptName,    // The TypeScript class name
        string ClrTypeName,       // The full CLR type name (e.g., "System.Console")
        Type? ResolvedType,       // The actual .NET Type if resolved
        FrozenDictionary<string, TypeInfo> Methods,
        FrozenDictionary<string, TypeInfo> StaticMethods,
        FrozenDictionary<string, TypeInfo> Properties
    ) : TypeInfo
    {
        public override string ToString() => $"external {TypeScriptName} -> {ClrTypeName}";
    }

    /// <summary>
    /// Represents an instance of a class. ClassType can be either a regular Class,
    /// a MutableClass (during signature collection), or an InstantiatedGeneric (for generic class instances).
    /// </summary>
    public record Instance(TypeInfo ClassType) : TypeInfo
    {
        /// <summary>
        /// Gets the resolved class type, handling MutableClass resolution.
        /// If ClassType is a MutableClass, returns its frozen form if available, otherwise the MutableClass itself.
        /// </summary>
        public TypeInfo ResolvedClassType => ClassType switch
        {
            MutableClass mc => (TypeInfo?)mc.Frozen ?? mc,
            _ => ClassType
        };

        public override string ToString() => ClassType switch
        {
            Class c => c.Name,
            MutableClass mc => mc.Name,
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
        TypeInfo? RestElementType = null,
        List<string?>? ElementNames = null
    ) : TypeInfo
    {
        public int MinLength => RequiredCount;
        public int? MaxLength => RestElementType != null ? null : ElementTypes.Count;
        public bool HasRest => RestElementType != null;
        public bool HasNames => ElementNames != null && ElementNames.Any(n => n != null);

        public override string ToString()
        {
            List<string> parts = [];
            for (int i = 0; i < ElementTypes.Count; i++)
            {
                bool isOptional = i >= RequiredCount;
                string elemStr = ElementTypes[i].ToString();

                // Include name if present
                if (ElementNames != null && i < ElementNames.Count && ElementNames[i] != null)
                {
                    elemStr = $"{ElementNames[i]}: {elemStr}";
                }

                parts.Add(isOptional ? $"{elemStr}?" : elemStr);
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

    public record Undefined() : TypeInfo
    {
        public override string ToString() => "undefined";
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

    /// <summary>String type - represents the TypeScript string primitive.</summary>
    public record String() : TypeInfo
    {
        public override string ToString() => "string";
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
    /// Represents a module namespace type returned by dynamic import.
    /// Contains the exported members and optional default export.
    /// Used for typeof import('./path') type inference.
    /// </summary>
    /// <param name="ModulePath">The resolved absolute path of the module</param>
    /// <param name="Exports">Named exports from the module</param>
    /// <param name="DefaultExport">Default export type, if present</param>
    public record Module(
        string ModulePath,
        FrozenDictionary<string, TypeInfo> Exports,
        TypeInfo? DefaultExport = null
    ) : TypeInfo
    {
        public override string ToString() => $"typeof import('{System.IO.Path.GetFileName(ModulePath)}')";
    }

    /// <summary>
    /// Represents a Generator type (Generator&lt;T&gt;).
    /// Generator functions return Generator objects that yield values of type T.
    /// </summary>
    public record Generator(TypeInfo YieldType) : TypeInfo
    {
        public override string ToString() => $"Generator<{YieldType}>";
    }

    /// <summary>
    /// Represents an AsyncGenerator type (AsyncGenerator&lt;T&gt;).
    /// Async generator functions return AsyncGenerator objects that yield values of type T asynchronously.
    /// </summary>
    public record AsyncGenerator(TypeInfo YieldType) : TypeInfo
    {
        public override string ToString() => $"AsyncGenerator<{YieldType}>";
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

    /// <summary>
    /// Represents a template literal type pattern: `prefix${Type}suffix`
    /// </summary>
    /// <param name="Strings">Static string parts (n+1 elements for n interpolations)</param>
    /// <param name="InterpolatedTypes">Type slots between static parts (n elements)</param>
    public record TemplateLiteralType(List<string> Strings, List<TypeInfo> InterpolatedTypes) : TypeInfo
    {
        public override string ToString()
        {
            var sb = new StringBuilder("`");
            for (int i = 0; i < InterpolatedTypes.Count; i++)
            {
                sb.Append(Strings[i]);
                sb.Append("${");
                sb.Append(InterpolatedTypes[i]);
                sb.Append('}');
            }
            sb.Append(Strings[^1]);
            sb.Append('`');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents an unevaluated intrinsic string type (e.g., Uppercase&lt;T&gt; where T is unresolved)
    /// </summary>
    public record IntrinsicStringType(StringManipulation Operation, TypeInfo Inner) : TypeInfo
    {
        public override string ToString() => $"{Operation}<{Inner}>";
    }

    public record Union(List<TypeInfo> Types) : TypeInfo
    {
        // Cached flattened types to avoid repeated computation
        private List<TypeInfo>? _flattenedTypes;

        public List<TypeInfo> FlattenedTypes => _flattenedTypes ??= Types
            .SelectMany(t => t is Union u ? u.FlattenedTypes : [t])
            .Distinct(TypeInfoEqualityComparer.Instance)
            .ToList();

        // Cache these boolean properties since they depend on FlattenedTypes
        private bool? _containsNull;
        private bool? _containsUndefined;

        public bool ContainsNull => _containsNull ??= FlattenedTypes.Any(t => t is Null);

        public bool ContainsUndefined => _containsUndefined ??= FlattenedTypes.Any(t => t is Undefined);

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
        // Cached flattened types to avoid repeated computation
        private List<TypeInfo>? _flattenedTypes;

        /// <summary>
        /// Returns a flattened list of all intersected types, collapsing nested intersections.
        /// Applies simplification: never absorbs all, any absorbs all, unknown is removed (identity).
        /// </summary>
        public List<TypeInfo> FlattenedTypes
        {
            get
            {
                if (_flattenedTypes != null) return _flattenedTypes;

                var flattened = Types
                    .SelectMany(t => t is Intersection i ? i.FlattenedTypes : [t])
                    .Distinct(TypeInfoEqualityComparer.Instance)
                    .ToList();

                // never absorbs everything in intersection
                if (flattened.Any(t => t is Never))
                    return _flattenedTypes = [new Never()];

                // any absorbs in intersection
                if (flattened.Any(t => t is Any))
                    return _flattenedTypes = [new Any()];

                // unknown is identity element - remove it
                flattened = flattened.Where(t => t is not Unknown).ToList();
                if (flattened.Count == 0)
                    return _flattenedTypes = [new Unknown()];

                return _flattenedTypes = flattened;
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

    /// <summary>
    /// Represents a conditional type: T extends U ? X : Y
    /// Conditional types are evaluated lazily, distributing over union types.
    /// </summary>
    /// <param name="CheckType">The type being checked (T in T extends U ? X : Y)</param>
    /// <param name="ExtendsType">The constraint type (U)</param>
    /// <param name="TrueType">The type if T extends U (X)</param>
    /// <param name="FalseType">The type if T does not extend U (Y)</param>
    public record ConditionalType(
        TypeInfo CheckType,
        TypeInfo ExtendsType,
        TypeInfo TrueType,
        TypeInfo FalseType
    ) : TypeInfo
    {
        public override string ToString() =>
            $"{CheckType} extends {ExtendsType} ? {TrueType} : {FalseType}";
    }

    /// <summary>
    /// Represents an inferred type parameter in a conditional type's extends clause.
    /// Used for pattern matching: T extends Array&lt;infer U&gt; ? U : T
    /// </summary>
    /// <param name="Name">The name of the inferred type parameter (e.g., "U")</param>
    public record InferredTypeParameter(string Name) : TypeInfo
    {
        public override string ToString() => $"infer {Name}";
    }
}

public class TypeInfoEqualityComparer : IEqualityComparer<TypeInfo>
{
    public static readonly TypeInfoEqualityComparer Instance = new();
    public bool Equals(TypeInfo? x, TypeInfo? y) => x?.ToString() == y?.ToString();
    public int GetHashCode(TypeInfo obj) => obj.ToString().GetHashCode();
}
