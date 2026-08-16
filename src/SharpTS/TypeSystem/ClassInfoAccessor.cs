namespace SharpTS.TypeSystem;

/// <summary>
/// Provides a generic accessor for extracting properties from class-like TypeInfo nodes
/// (Class, GenericClass, InstantiatedGeneric wrapping GenericClass).
/// </summary>
internal static class ClassInfoAccessor
{
    internal static T? Get<T>(TypeInfo? classType,
        Func<TypeInfo.Class, T?> fromClass,
        Func<TypeInfo.GenericClass, T?> fromGeneric) => classType switch
    {
        TypeInfo.Class c => fromClass(c),
        TypeInfo.GenericClass gc => fromGeneric(gc),
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => fromGeneric(gc),
            _ => default
        },
        _ => default
    };
}
