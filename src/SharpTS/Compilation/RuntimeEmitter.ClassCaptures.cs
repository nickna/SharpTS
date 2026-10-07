using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitClassCaptureBodies(TypeBuilder captureType, FieldBuilder owner, FieldBuilder field,
        FieldBuilder captures, MethodBuilder read, MethodBuilder write, MethodBuilder find)
    {
        var il = read.GetILGenerator();
        var cell = il.DeclareLocal(captureType);
        var snapshot = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, captureType);
        il.Emit(OpCodes.Stloc, cell);
        il.Emit(OpCodes.Ldloc, cell);
        il.Emit(OpCodes.Brfalse, snapshot);
        il.Emit(OpCodes.Ldloc, cell);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ldloc, cell);
        il.Emit(OpCodes.Ldfld, owner);
        il.Emit(OpCodes.Callvirt, _types.FieldInfoGetValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(snapshot);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il = write.GetILGenerator();
        cell = il.DeclareLocal(captureType);
        snapshot = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, captures);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, captureType);
        il.Emit(OpCodes.Stloc, cell);
        il.Emit(OpCodes.Ldloc, cell);
        il.Emit(OpCodes.Brfalse, snapshot);
        il.Emit(OpCodes.Ldloc, cell);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ldloc, cell);
        il.Emit(OpCodes.Ldfld, owner);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "SetValue", _types.Object, _types.Object));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(snapshot);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, captures);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ret);

        il = find.GetILGenerator();
        var index = il.DeclareLocal(_types.Int32);
        cell = il.DeclareLocal(captureType);
        var loop = il.DefineLabel();
        var next = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, captures);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, captures);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, captureType);
        il.Emit(OpCodes.Stloc, cell);
        il.Emit(OpCodes.Ldloc, cell);
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, cell);
        il.Emit(OpCodes.Ldfld, owner);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsInstanceOfType", _types.Object));
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, cell);
        il.Emit(OpCodes.Ldfld, owner);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
