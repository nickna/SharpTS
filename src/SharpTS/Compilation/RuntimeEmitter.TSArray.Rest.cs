using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // These helpers accept only a private call-argument destination. Appending
    // defines fresh own elements and must never invoke inherited index setters.
    private void EmitTSArrayRestBuilderHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var appendDouble = typeBuilder.DefineMethod("AppendRestDouble", MethodAttributes.Assembly,
            _types.Void, [_types.Double]);
        runtime.TSArrayAppendRestDouble = appendDouble;
        appendDouble.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        var il = appendDouble.GetILGenerator();
        var boxed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
        il.Emit(OpCodes.Brfalse, boxed);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TSArrayPushDouble);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(boxed);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.TSArrayAppendRest);
        il.Emit(OpCodes.Ret);

        var appendValue = typeBuilder.DefineMethod("AppendRestValue",
            MethodAttributes.Assembly | MethodAttributes.Static, _types.Void,
            [_types.ListOfObject, _types.Object]);
        runtime.TSArrayAppendRestValue = appendValue;
        appendValue.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        il = appendValue.GetILGenerator();
        var plain = il.DefineLabel();
        var destination = il.DeclareLocal(typeBuilder);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Stloc, destination);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Brfalse, plain);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TSArrayAppendRest);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(plain);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _tsArrayListAdd!);
        il.Emit(OpCodes.Ret);

        // Reserve only after an already-evaluated standard array's length is
        // available. Checked addition keeps expansion from wrapping capacity.
        var reserve = typeBuilder.DefineMethod("ReserveRest",
            MethodAttributes.Assembly | MethodAttributes.Static, _types.Void,
            [_types.ListOfObject, _types.Int32]);
        runtime.TSArrayReserveRest = reserve;
        il = reserve.GetILGenerator();
        destination = il.DeclareLocal(typeBuilder);
        var capacity = il.DeclareLocal(_types.Int32);
        plain = il.DefineLabel();
        var resize = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Stloc, destination);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Brfalse, plain);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
        il.Emit(OpCodes.Brfalse, plain);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add_Ovf);
        il.Emit(OpCodes.Stloc, capacity);
        il.Emit(OpCodes.Ldloc, capacity);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
        il.Emit(OpCodes.Brfalse, resize);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, capacity);
        il.Emit(OpCodes.Bge, done);
        il.MarkLabel(resize);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Ldflda, _tsArrayNumStoreField);
        il.Emit(OpCodes.Ldloc, capacity);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Array).GetMethod("Resize")!, _types.Double));
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(plain);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add_Ovf);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "EnsureCapacity", _types.Int32));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);

        // The caller proves standard iteration and numeric source storage.
        // Reading the numeric store here never calls Elements/EnsureBoxed.
        var appendSource = typeBuilder.DefineMethod("AppendNumericRestSource",
            MethodAttributes.Assembly | MethodAttributes.Static, _types.Void,
            [_types.ListOfObject, typeBuilder]);
        runtime.TSArrayAppendNumericRestSource = appendSource;
        il = appendSource.GetILGenerator();
        destination = il.DeclareLocal(typeBuilder);
        var index = il.DeclareLocal(_types.Int32);
        var count = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Stloc, destination);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
        il.Emit(OpCodes.Stloc, count);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Call, reserve);
        var loop = il.DefineLabel();
        done = il.DefineLabel();
        plain = il.DefineLabel();
        var next = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Brfalse, plain);
        il.Emit(OpCodes.Ldloc, destination);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_R8);
        il.Emit(OpCodes.Call, appendDouble);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(plain);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, _tsArrayListAdd!);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
    }
}
