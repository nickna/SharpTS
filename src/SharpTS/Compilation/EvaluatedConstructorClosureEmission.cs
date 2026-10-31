using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>The existing evaluated-owner closure, used only after owned legacy admission.</summary>
internal static class EvaluatedConstructorClosureEmission
{
    internal static void EmitLegacy(ILGenerator il, TypeProvider types, Type factoryMarker)
    {
        var owner = il.DeclareLocal(types.Type);
        var supplied = il.DeclareLocal(types.MakeArrayType(types.Type));
        var done = il.DefineLabel();
        var guest = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stloc, owner);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, supplied);
        il.Emit(OpCodes.Ldloc, owner);
        il.Emit(OpCodes.Callvirt, types.GetPropertyGetter(types.Type, "IsGenericTypeDefinition"));
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldtoken, factoryMarker);
        il.Emit(OpCodes.Call, types.TypeGetTypeFromHandle); il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, types.GetMethod(typeof(MemberInfo), "IsDefined", types.Type, types.Boolean));
        il.Emit(OpCodes.Brtrue, guest);
        il.Emit(OpCodes.Ldloc, supplied); il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldloc, supplied);
        il.Emit(OpCodes.Callvirt, types.GetMethod(types.Type, "MakeGenericType", types.MakeArrayType(types.Type)));
        il.Emit(OpCodes.Stloc, owner); il.Emit(OpCodes.Br, done);

        il.MarkLabel(guest);
        var canonical = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, supplied); il.Emit(OpCodes.Brfalse, canonical);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldtoken, types.Object);
        il.Emit(OpCodes.Call, types.TypeGetTypeFromHandle); il.Emit(OpCodes.Beq, canonical);
        il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Bne_Un, canonical);
        GenericTypeClosingEmitter.Emit(il, types, owner, supplied);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(canonical);
        GenericTypeClosingEmitter.Emit(il, types, owner);
        il.MarkLabel(done); il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ret);
    }
}