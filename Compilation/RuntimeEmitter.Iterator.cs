using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits iterator protocol support methods into the generated assembly.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits methods for iterator protocol support.
    /// </summary>
    private void EmitIteratorMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitGetIterator(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits GetIterator: gets an enumerable from an object using the Symbol.iterator protocol.
    /// Signature: IEnumerable GetIterator(object obj, $TSSymbol iteratorSymbol)
    ///
    /// This method:
    /// 1. Checks if obj has a Symbol.iterator property
    /// 2. Calls the iterator function to get an iterator object
    /// 3. Returns an IEnumerable that wraps the iterator protocol
    ///
    /// For objects without Symbol.iterator, falls back to treating as array/string.
    /// </summary>
    private void EmitGetIterator(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // This is a complex method that would be difficult to emit entirely in IL.
        // Instead, we'll create a simpler wrapper that leverages existing runtime helpers.
        // The method signature: IEnumerable<object?> GetIterator(object obj, $TSSymbol iteratorSymbol)

        var method = typeBuilder.DefineMethod(
            "GetIterator",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.IEnumerable,
            [_types.Object, runtime.TSSymbolType]
        );
        runtime.GetIterator = method;

        var il = method.GetILGenerator();

        // For now, just return the object cast to IEnumerable
        // Full implementation would need to:
        // 1. Get the symbol dict from the object
        // 2. Look up the iterator function
        // 3. Call it and wrap the result

        // Simple fallback: check if it's already IEnumerable
        var isEnumerableLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // if (obj is IEnumerable) return (IEnumerable)obj;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumerable);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, returnLabel);
        il.Emit(OpCodes.Pop);

        // For non-IEnumerable, return null (caller should handle)
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }
}

