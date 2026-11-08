using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitArrayCallbackPrototypeAdapter(
        TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder helper,
        bool isReducer, bool shortCircuits)
    {
        var method = typeBuilder.DefineMethod(
            helper.Name + "Proto",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]);
        method.DefineParameter(2, ParameterAttributes.None, "args")
            .SetCustomAttribute(typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes)!,
                CustomAttributeEncoder.EmptyBlob);
        var il = method.GetILGenerator();
        var savedReceiver = il.DeclareLocal(_types.Object);
        var savedThisArg = il.DeclareLocal(_types.Object);
        var result = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldsfld, runtime.ArrayOperations.CurrentReceiverField);
        il.Emit(OpCodes.Stloc, savedReceiver);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayOperations.CallbackThisArgField);
        il.Emit(OpCodes.Stloc, savedThisArg);

        // Prototype function values need the same callback/receiver transport
        // as inline calls. Scope both slots across materialization and callback
        // execution so nested calls and abrupt completions restore the caller.
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.BoxedPrimitives.ToObject);
        il.Emit(OpCodes.Stsfld, runtime.ArrayOperations.CurrentReceiverField);

        if (isReducer)
        {
            // A reducer's second argument is its initial accumulator, not a
            // callback thisArg. Its complete argument list is forwarded below.
            il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        }
        else
        {
            var explicitThisArg = il.DefineLabel();
            var thisArgReady = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Bge, explicitThisArg);
            il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
            il.Emit(OpCodes.Br, thisArgReady);
            il.MarkLabel(explicitThisArg);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.MarkLabel(thisArgReady);
        }
        il.Emit(OpCodes.Stsfld, runtime.ArrayOperations.CallbackThisArgField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(shortCircuits ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ArrayOperations.MaterializeForIteration);

        if (isReducer)
            il.Emit(OpCodes.Ldarg_1);
        else
        {
            var explicitCallback = il.DefineLabel();
            var callbackReady = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Brtrue, explicitCallback);
            il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
            il.Emit(OpCodes.Br, callbackReady);
            il.MarkLabel(explicitCallback);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.MarkLabel(callbackReady);
        }
        il.Emit(OpCodes.Call, helper);
        if (helper.ReturnType == _types.Void)
            il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        else if (helper.ReturnType == _types.Double)
            il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stloc, result);

        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, savedReceiver);
        il.Emit(OpCodes.Stsfld, runtime.ArrayOperations.CurrentReceiverField);
        il.Emit(OpCodes.Ldloc, savedThisArg);
        il.Emit(OpCodes.Stsfld, runtime.ArrayOperations.CallbackThisArgField);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
