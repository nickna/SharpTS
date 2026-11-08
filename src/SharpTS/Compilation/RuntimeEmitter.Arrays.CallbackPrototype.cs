using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private (MethodBuilder FindLast, MethodBuilder FindLastIndex) EmitArrayReversePredicatePrototypeAdapters(
        TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var core = typeBuilder.DefineMethod("ArrayReversePredicate", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object, _types.Boolean]);
        var il = core.GetILGenerator();
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array reverse search");

        var args = il.DeclareLocal(_types.ObjectArray);
        var index = il.DeclareLocal(_types.Double);
        var integerKey = il.DeclareLocal(_types.Int64);
        var element = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, args);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Stloc, index);

        var loop = il.DefineLabel();
        var missing = il.DefineLabel();
        var returnValue = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Blt, missing);

        // FindViaPredicate uses Get at every visited index, including holes,
        // and returns the value captured before the callback can mutate it.
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stloc, integerKey);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloca, integerKey);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(typeof(CultureInfo), nameof(CultureInfo.InvariantCulture)));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int64, "ToString", typeof(IFormatProvider)));
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Stloc, element);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, element);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Call, runtime.Invocation.Method);
        il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
        il.Emit(OpCodes.Brfalse, loop);

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, returnValue);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(returnValue);
        il.Emit(OpCodes.Ldloc, element);
        il.Emit(OpCodes.Ret);

        var missingValue = il.DefineLabel();
        il.MarkLabel(missing);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, missingValue);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(missingValue);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        return (EmitAdapter("ArrayFindLastProto", returnIndex: false),
            EmitAdapter("ArrayFindLastIndexProto", returnIndex: true));

        MethodBuilder EmitAdapter(string name, bool returnIndex)
        {
            var method = typeBuilder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static,
                _types.Object, [_types.Object, _types.ObjectArray]);
            method.DefineParameter(2, ParameterAttributes.None, "args")
                .SetCustomAttribute(typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes)!,
                    CustomAttributeEncoder.EmptyBlob);
            var adapterIL = method.GetILGenerator();
            adapterIL.Emit(OpCodes.Ldarg_0);
            EmitArgumentOrUndefined(0);
            EmitArgumentOrUndefined(1);
            adapterIL.Emit(returnIndex ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            adapterIL.Emit(OpCodes.Call, core);
            adapterIL.Emit(OpCodes.Ret);
            return method;

            void EmitArgumentOrUndefined(int argumentIndex)
            {
                var provided = adapterIL.DefineLabel();
                var ready = adapterIL.DefineLabel();
                adapterIL.Emit(OpCodes.Ldarg_1);
                adapterIL.Emit(OpCodes.Ldlen);
                adapterIL.Emit(OpCodes.Conv_I4);
                adapterIL.Emit(OpCodes.Ldc_I4, argumentIndex);
                adapterIL.Emit(OpCodes.Bgt, provided);
                adapterIL.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
                adapterIL.Emit(OpCodes.Br, ready);
                adapterIL.MarkLabel(provided);
                adapterIL.Emit(OpCodes.Ldarg_1);
                adapterIL.Emit(OpCodes.Ldc_I4, argumentIndex);
                adapterIL.Emit(OpCodes.Ldelem_Ref);
                adapterIL.MarkLabel(ready);
            }
        }
    }

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

        LocalBuilder? callback = null;
        if (!isReducer)
        {
            callback = il.DeclareLocal(_types.Object);
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
            il.Emit(OpCodes.Stloc, callback);
        }

        il.Emit(OpCodes.Ldarg_0);
        bool usesVirtualArgumentsLength = shortCircuits || helper == runtime.ArrayOperations.Filter;
        il.Emit(usesVirtualArgumentsLength ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        if (_features.UsesArguments && helper == runtime.ArrayOperations.Map)
        {
            // Null disables map validation in the materializer. A guest null
            // callback is invalid just like undefined, so pass that sentinel.
            var validationCallbackReady = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, callback!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, validationCallbackReady);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
            il.MarkLabel(validationCallbackReady);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Call, runtime.ArrayOperations.MaterializeForIteration);

        if (callback is null)
            il.Emit(OpCodes.Ldarg_1);
        else
            il.Emit(OpCodes.Ldloc, callback);
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
