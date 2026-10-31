using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Promise resolution reads then only from Objects. These are the actual
    // emitted primitive representations; boxed objects and foreign CLR values
    // retain the existing object path without additional admission.
    private void EmitPromisePrimitiveValueBranch(ILGenerator il, EmittedRuntime runtime, Label primitive)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, primitive);
        Type[] primitives = [_types.Double, _types.String, _types.Boolean,
            _types.BigInteger, runtime.Sentinels.UndefinedType, runtime.Symbols.Type];
        foreach (var type in primitives)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, type);
            il.Emit(OpCodes.Brtrue, primitive);
        }
    }
}
