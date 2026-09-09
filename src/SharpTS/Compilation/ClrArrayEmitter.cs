using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Shared native-array conversion for method results and delegate arguments.</summary>
internal static class ClrArrayEmitter
{
    internal static void EmitToGuest(ILGenerator il, Type arrayType, TypeProvider types,
        EmittedRuntime runtime, Action<Type> boxElement)
    {
        Type elementType = arrayType.GetElementType()!;
        var source = il.DeclareLocal(arrayType);
        var elements = il.DeclareLocal(types.ObjectArray);
        var index = il.DeclareLocal(types.Int32);
        il.Emit(OpCodes.Stloc, source);

        var nonNull = il.DefineLabel();
        var finished = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Brtrue, nonNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, finished);
        il.MarkLabel(nonNull);

        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newarr, types.Object);
        il.Emit(OpCodes.Stloc, elements);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);

        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, done);

        il.Emit(OpCodes.Ldloc, elements);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem, elementType);
        if (elementType.IsArray)
            EmitToGuest(il, elementType, types, runtime, boxElement);
        else
            boxElement(elementType);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);

        il.Emit(OpCodes.Ldloc, elements);
        il.Emit(OpCodes.Call, runtime.CreateArray);
        il.MarkLabel(finished);
    }
}
