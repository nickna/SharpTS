using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a <c>new object[count]</c> populated element-by-element, leaving the
    /// array on the IL stack. <paramref name="emitElement"/> receives the element
    /// index and must push exactly one object reference (already boxed). This is
    /// the canonical arg-packing idiom (<c>ldc/newarr/{dup;ldc;…;stelem.ref}×N</c>)
    /// for raw-<see cref="ILGenerator"/> emit sites; expression-driven emitters use
    /// <see cref="ExpressionEmitterBase.EmitArgsArray"/> instead.
    /// </summary>
    private void EmitObjectArray(ILGenerator il, int count, System.Action<int> emitElement)
    {
        il.Emit(OpCodes.Ldc_I4, count);
        il.Emit(OpCodes.Newarr, _types.Object);
        for (int i = 0; i < count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitElement(i);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }
}
