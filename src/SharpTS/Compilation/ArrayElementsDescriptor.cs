using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Identifies the backing store kind for a TypeScript array.
/// </summary>
public enum ArrayElementsKind
{
    /// <summary>List&lt;double&gt; — unboxed number[]</summary>
    Double,
    /// <summary>List&lt;bool&gt; — unboxed boolean[]</summary>
    Bool,
    /// <summary>List&lt;object?&gt; — general-purpose (string[], object[], union[], etc.)</summary>
    Object
}

/// <summary>
/// Describes everything needed to emit IL for a specific array backing type.
/// Single source of truth replacing scattered isinst chains across 7+ files.
/// Inspired by V8's ElementsAccessor pattern and LLVM's declarative action tables.
/// </summary>
public sealed class ArrayElementsDescriptor(
    ArrayElementsKind kind,
    StackType stackType,
    bool needsBoxOnGet,
    TokenType? elementTokenType)
{
    /// <summary>Which backing store this describes.</summary>
    public ArrayElementsKind Kind { get; } = kind;

    /// <summary>The StackType to set after loading an element.</summary>
    public StackType StackType { get; } = stackType;

    /// <summary>Whether get_Item result must be boxed for generic dispatch.</summary>
    public bool NeedsBoxOnGet { get; } = needsBoxOnGet;

    /// <summary>The TokenType for TypeInfo.Primitive matching (null for Object).</summary>
    public TokenType? ElementTokenType { get; } = elementTokenType;

    /// <summary>Resolves the List&lt;T&gt; type from TypeProvider.</summary>
    public Type GetListType(TypeProvider types) => Kind switch
    {
        ArrayElementsKind.Double => types.ListOfDouble,
        ArrayElementsKind.Bool => types.ListOfBool,
        _ => types.ListOfObject
    };

    /// <summary>Resolves the element CLR type from TypeProvider.</summary>
    public Type GetElementType(TypeProvider types) => Kind switch
    {
        ArrayElementsKind.Double => types.Double,
        ArrayElementsKind.Bool => types.Boolean,
        _ => types.Object
    };

    /// <summary>Gets the emitted SetArrayElement method from EmittedRuntime.</summary>
    public MethodBuilder GetSetArrayElementMethod(EmittedRuntime runtime) => Kind switch
    {
        ArrayElementsKind.Double => runtime.SetArrayElementDouble,
        ArrayElementsKind.Bool => runtime.SetArrayElementBool,
        _ => runtime.SetArrayElement
    };

    /// <summary>Emits the default value for auto-extension (0.0, false, or null).</summary>
    public void EmitDefaultValue(ILGenerator il)
    {
        switch (Kind)
        {
            case ArrayElementsKind.Double:
                il.Emit(OpCodes.Ldc_R8, 0.0);
                break;
            case ArrayElementsKind.Bool:
                il.Emit(OpCodes.Ldc_I4_0);
                break;
            default:
                il.Emit(OpCodes.Ldnull);
                break;
        }
    }

    /// <summary>Emits boxing of the element if needed for generic dispatch.</summary>
    public void EmitBoxElement(ILGenerator il, TypeProvider types)
    {
        if (NeedsBoxOnGet)
            il.Emit(OpCodes.Box, GetElementType(types));
    }
}

/// <summary>
/// Registry and resolution for array backing type descriptors.
/// Single source of truth for all array backing type metadata.
/// </summary>
public static class ArrayElements
{
    /// <summary>Descriptor for List&lt;double&gt; backing store.</summary>
    public static readonly ArrayElementsDescriptor Double = new(
        ArrayElementsKind.Double, StackType.Double, needsBoxOnGet: true, TokenType.TYPE_NUMBER);

    /// <summary>Descriptor for List&lt;bool&gt; backing store.</summary>
    public static readonly ArrayElementsDescriptor Bool = new(
        ArrayElementsKind.Bool, StackType.Boolean, needsBoxOnGet: true, TokenType.TYPE_BOOLEAN);

    /// <summary>Descriptor for List&lt;object?&gt; backing store.</summary>
    public static readonly ArrayElementsDescriptor Object = new(
        ArrayElementsKind.Object, StackType.Unknown, needsBoxOnGet: false, elementTokenType: null);

    /// <summary>All descriptors in priority order for isinst dispatch chains.</summary>
    public static readonly ArrayElementsDescriptor[] All = [Double, Bool, Object];

    /// <summary>Typed-only descriptors (excludes Object — for typed fast-path patterns).</summary>
    public static readonly ArrayElementsDescriptor[] Typed = [Double, Bool];

    /// <summary>
    /// Resolves the best descriptor from a TypeInfo (must be TypeInfo.Array).
    /// Returns null if the type is not an array.
    /// </summary>
    public static ArrayElementsDescriptor? Resolve(TypeInfo? type)
    {
        if (type is not TypeInfo.Array arr) return null;
        return arr.ElementType switch
        {
            TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => Double,
            TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => Bool,
            _ => Object
        };
    }
}
