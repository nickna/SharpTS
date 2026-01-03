using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Maps TypeScript types to .NET CLR types for IL compilation.
/// </summary>
/// <remarks>
/// Converts <see cref="TypeInfo"/> records and type annotation strings to .NET
/// <see cref="Type"/> instances. Primitives map directly (number→double, string→string,
/// boolean→bool). Complex types (arrays, functions, records, unions) map to object
/// since they use dynamic runtime representations. Used by <see cref="ILCompiler"/>
/// and <see cref="ILEmitter"/> for parameter/return type declarations.
/// </remarks>
/// <seealso cref="TypeInfo"/>
/// <seealso cref="ILCompiler"/>
public class TypeMapper
{
    private readonly ModuleBuilder _moduleBuilder;

    public TypeMapper(ModuleBuilder moduleBuilder)
    {
        _moduleBuilder = moduleBuilder;
    }

    public Type MapTypeInfo(TypeInfo typeInfo) => typeInfo switch
    {
        TypeInfo.Primitive p => MapPrimitive(p),
        TypeInfo.Array => typeof(object), // Will be TSArray at runtime
        TypeInfo.Function => typeof(object), // Will be delegate at runtime
        TypeInfo.Class c => GetClassType(c.Name),
        TypeInfo.Instance i => i.ClassType switch
        {
            TypeInfo.Class c => GetClassType(c.Name),
            TypeInfo.InstantiatedGeneric => typeof(object),
            _ => typeof(object)
        },
        TypeInfo.Record => typeof(object), // Will be TSObject at runtime
        TypeInfo.Void => typeof(void),
        TypeInfo.Any => typeof(object),
        TypeInfo.Union => typeof(object), // Union types are dynamic at runtime
        TypeInfo.Null => typeof(object), // Null maps to object
        TypeInfo.Unknown => typeof(object), // Unknown is dynamic at runtime
        TypeInfo.Never => typeof(void), // Never represents no return
        // Generic types erase to object at runtime (type checking is compile-time only)
        TypeInfo.TypeParameter => typeof(object),
        TypeInfo.GenericClass => typeof(object),
        TypeInfo.GenericFunction => typeof(object),
        TypeInfo.GenericInterface => typeof(object),
        TypeInfo.InstantiatedGeneric => typeof(object),
        _ => typeof(object)
    };

    private static Type MapPrimitive(TypeInfo.Primitive p) => p.Type switch
    {
        TokenType.TYPE_NUMBER => typeof(double),
        TokenType.TYPE_STRING => typeof(string),
        TokenType.TYPE_BOOLEAN => typeof(bool),
        _ => typeof(object)
    };

    public Type GetClassType(string className)
    {
        // Return object for now - actual class types are resolved during compilation
        return typeof(object);
    }

    public static Type GetClrType(string typeAnnotation) => typeAnnotation switch
    {
        "number" => typeof(double),
        "string" => typeof(string),
        "boolean" => typeof(bool),
        "void" => typeof(void),
        "any" => typeof(object),
        "unknown" => typeof(object),
        "never" => typeof(void),
        _ when typeAnnotation.EndsWith("[]") => typeof(object), // Array type
        _ => typeof(object) // Class or interface type
    };
}
