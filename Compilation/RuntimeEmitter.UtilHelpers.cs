using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits util module helper methods.
    /// </summary>
    private void EmitUtilMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitUtilFormat(typeBuilder, runtime);
        EmitUtilInspect(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static string UtilFormat(object[] args)
    /// </summary>
    private void EmitUtilFormat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilFormat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]);
        runtime.UtilFormat = method;

        var il = method.GetILGenerator();

        // Simple implementation: join args with space
        var sbType = _types.StringBuilder;
        var sbCtor = _types.GetDefaultConstructor(sbType);
        var appendMethod = _types.GetMethod(sbType, "Append", _types.Object);
        var appendStrMethod = _types.GetMethod(sbType, "Append", _types.String);
        var toStringMethod = _types.GetMethodNoParams(sbType, "ToString");

        var sbLocal = il.DeclareLocal(sbType);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // sb = new StringBuilder()
        il.Emit(OpCodes.Newobj, sbCtor);
        il.Emit(OpCodes.Stloc, sbLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(" ")
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var skipSpace = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipSpace);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Callvirt, appendStrMethod);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipSpace);

        // sb.Append(args[i])
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, appendMethod);
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, toStringMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string UtilInspect(object obj, object options)
    /// </summary>
    private void EmitUtilInspect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilInspect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object]);
        runtime.UtilInspect = method;

        var il = method.GetILGenerator();

        // Simple implementation: call ToString on the object
        var nullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}
