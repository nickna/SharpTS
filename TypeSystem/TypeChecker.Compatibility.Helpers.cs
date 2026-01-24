using SharpTS.Runtime.BuiltIns;
using SharpTS.Parsing;
using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Helper methods for type compatibility checking - type predicates and class accessors.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Generic helper for type checking with union support.
    /// Checks if a type matches a predicate, with automatic handling for Any, Union, and TypeParameter types.
    /// </summary>
    /// <param name="t">The type to check.</param>
    /// <param name="baseTypeCheck">Predicate for checking base (non-Any, non-Union) types.</param>
    /// <returns>True if the type matches or is Any, or if all union members match, or if TypeParameter constraint matches.</returns>
    private bool IsTypeOfKind(TypeInfo t, Func<TypeInfo, bool> baseTypeCheck) =>
        baseTypeCheck(t) ||
        t is TypeInfo.Any ||
        (t is TypeInfo.Union u && u.FlattenedTypes.All(inner => IsTypeOfKind(inner, baseTypeCheck))) ||
        (t is TypeInfo.TypeParameter tp && tp.Constraint != null && IsTypeOfKind(tp.Constraint, baseTypeCheck));

    private bool IsNumber(TypeInfo t) =>
        IsTypeOfKind(t, type =>
            type is TypeInfo.Primitive p && p.Type == TokenType.TYPE_NUMBER ||
            type is TypeInfo.NumberLiteral);

    private bool IsString(TypeInfo t) =>
        IsTypeOfKind(t, type =>
            type is TypeInfo.String ||
            type is TypeInfo.StringLiteral);

    private bool IsBigInt(TypeInfo t) =>
        IsTypeOfKind(t, type => type is TypeInfo.BigInt);

    /// <summary>
    /// Checks if a type is a primitive (not valid as WeakMap key or WeakSet value).
    /// </summary>
    private bool IsPrimitiveType(TypeInfo t) => t is TypeInfo.String or TypeInfo.Primitive or TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigInt or TypeInfo.Symbol or TypeInfo.UniqueSymbol;

    /// <summary>
    /// Checks if a name is a built-in Error type name.
    /// Delegates to ErrorBuiltIns for centralized type name knowledge.
    /// </summary>
    private static bool IsErrorTypeName(string name) => ErrorBuiltIns.IsErrorTypeName(name);

    /// <summary>
    /// Gets the superclass of a class type, handling both Class and InstantiatedGeneric.
    /// </summary>
    private static TypeInfo? GetSuperclass(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Superclass,
        TypeInfo.GenericClass gc => gc.Superclass,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Superclass,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the methods dictionary from a class-like type (Class, GenericClass, or InstantiatedGeneric).
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetMethods(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Methods,
        TypeInfo.GenericClass gc => gc.Methods,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Methods,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the name of a class-like type (Class, GenericClass, or InstantiatedGeneric).
    /// </summary>
    private static string? GetClassName(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Name,
        TypeInfo.GenericClass gc => gc.Name,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Name,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the static methods dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetStaticMethods(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.StaticMethods,
        TypeInfo.GenericClass gc => gc.StaticMethods,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.StaticMethods,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the static properties dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetStaticProperties(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.StaticProperties,
        TypeInfo.GenericClass gc => gc.StaticProperties,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.StaticProperties,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Converts a class-like type to a TypeInfo.Class for walking hierarchy.
    /// Returns null if the type is not class-like.
    /// </summary>
    private static TypeInfo.Class? AsClass(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c,
        _ => null
    };

    /// <summary>
    /// Gets the field types dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetFieldTypes(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.FieldTypes,
        TypeInfo.GenericClass gc => gc.FieldTypes,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.FieldTypes,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the getters dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetGetters(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Getters,
        TypeInfo.GenericClass gc => gc.Getters,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Getters,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the setters dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetSetters(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Setters,
        TypeInfo.GenericClass gc => gc.Setters,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Setters,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the method access dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, AccessModifier>? GetMethodAccess(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.MethodAccess,
        TypeInfo.GenericClass gc => gc.MethodAccess,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.MethodAccess,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the field access dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, AccessModifier>? GetFieldAccess(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.FieldAccess,
        TypeInfo.GenericClass gc => gc.FieldAccess,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.FieldAccess,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the readonly fields set from a class-like type.
    /// </summary>
    private static FrozenSet<string>? GetReadonlyFields(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.ReadonlyFields,
        TypeInfo.GenericClass gc => gc.ReadonlyFields,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.ReadonlyFields,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the abstract methods set from a class-like type.
    /// </summary>
    private static FrozenSet<string>? GetAbstractMethods(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.AbstractMethodSet,
        TypeInfo.GenericClass gc => gc.AbstractMethodSet,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.AbstractMethodSet,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the abstract getters set from a class-like type.
    /// </summary>
    private static FrozenSet<string>? GetAbstractGetters(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.AbstractGetterSet,
        TypeInfo.GenericClass gc => gc.AbstractGetterSet,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.AbstractGetterSet,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the abstract setters set from a class-like type.
    /// </summary>
    private static FrozenSet<string>? GetAbstractSetters(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.AbstractSetterSet,
        TypeInfo.GenericClass gc => gc.AbstractSetterSet,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.AbstractSetterSet,
            _ => null
        },
        _ => null
    };
}
