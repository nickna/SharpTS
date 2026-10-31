using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal sealed partial class NativeMethodLookupCatalog
{
    private void EmitFixedBridgeQuery(MethodBuilder producer, ILGenerator il, EmittedErrorRuntime errors, bool select, Type owner, string message)
    {
        if (select)
        {
            var unsupported = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Instance));
            il.Emit(OpCodes.Bne_Un, unsupported);
            foreach (string name in ManualBridgeNames)
            {
                var nextName = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, name);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextName);
                // Literal names keep the actual binder miss without marking every
                // public method (including unrelated dynamic array factories or constructor helpers).
                il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                il.Emit(OpCodes.Ldstr, name); il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Instance));
                int queryOffset = il.ILOffset;
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
                RecordQuery(producer, il, queryOffset, owner, BindingFlags.Public | BindingFlags.Instance, name);
                il.Emit(OpCodes.Ret); il.MarkLabel(nextName);
            }
            il.MarkLabel(unsupported);
        }
        // These fixed manual bridges have no ambiguous producer. Ordinary JS
        // arrays and functions use their dedicated property/prototype dispatch.
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, message);
    }
}
