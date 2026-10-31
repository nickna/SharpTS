using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct ArrayLikeArgumentsInputs(
        MethodInfo TypeOf,
        MethodInfo GetProperty,
        MethodInfo GetIndex,
        MethodInfo ToNumber,
        MethodInfo CreateException,
        ConstructorInfo TypeErrorConstructor);

    private readonly record struct CallableValidationInputs(
        MethodInfo TypeOf,
        Type ReflectedCallableType,
        Type? BoundTypedArrayMethodType,
        Type? TextDecodeMethodType,
        Type? TransformDoneCallbackType,
        Type? StreamWriteCallbackType,
        MethodInfo CreateException,
        ConstructorInfo TypeErrorConstructor);

    private readonly record struct PrepareApplyArgumentsInputs(
        CallableValidationInputs Callable, Type UndefinedType, MethodInfo FromArrayLike);

    private void DefinePrepareApplyArguments(
        TypeBuilder typeBuilder, EmittedFunctionPrototypeRuntime functionPrototypes)
    {
        functionPrototypes.PrepareApplyArguments = typeBuilder.DefineMethod(
            "PrepareApplyArguments", MethodAttributes.Public | MethodAttributes.Static,
            _types.ObjectArray, [_types.Object, _types.Object]);
    }

    private void EmitPrepareApplyArguments(
        EmittedFunctionPrototypeRuntime functionPrototypes, PrepareApplyArgumentsInputs inputs)
    {
        var il = functionPrototypes.PrepareApplyArguments.GetILGenerator();
        var empty = il.DefineLabel();

        EmitRequireCallable(il, inputs.Callable, () => il.Emit(OpCodes.Ldarg_0),
            "Function.prototype.apply called on a non-callable value");

        // Function.prototype.apply permits nullish lists; Reflect does not.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, empty);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, empty);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.FromArrayLike);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(empty);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Ret);
        functionPrototypes.MarkPrepareApplyArgumentsBodyEmitted();
    }

    private void EmitRequireCallable(ILGenerator il, CallableValidationInputs inputs,
        Action loadTarget, string message)
    {
        var callable = il.DefineLabel();
        var stringEquals = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        // IsCallable must precede every observable read from the argument list.
        // TypeOf already understands emitted functions, constructors, bound wrappers,
        // native callback representations, and callable versus noncallable proxies.
        // Preserve invocation-capable internal wrappers that do not participate in
        // the general typeof operation; their existing dispatch remains unchanged.
        foreach (var callableType in new[] { _types.MethodBase, inputs.ReflectedCallableType,
                     inputs.BoundTypedArrayMethodType, inputs.TextDecodeMethodType,
                     inputs.TransformDoneCallbackType, inputs.StreamWriteCallbackType })
        {
            if (callableType is null) continue;
            loadTarget();
            il.Emit(OpCodes.Isinst, callableType);
            il.Emit(OpCodes.Brtrue, callable);
        }
        loadTarget();
        il.Emit(OpCodes.Call, inputs.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, stringEquals);
        il.Emit(OpCodes.Brtrue, callable);
        GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TypeErrorConstructor,
            message);
        il.MarkLabel(callable);
    }

    private void EmitArrayLikeArguments(EmittedCallArgumentsRuntime arguments, ArrayLikeArgumentsInputs inputs)
    {
        var il = arguments.FromArrayLike.GetILGenerator();
        var empty = il.DefineLabel();
        var objectList = il.DefineLabel();
        var invalidList = il.DefineLabel();
        var boundedLength = il.DefineLabel();
        var haveLength = il.DefineLabel();
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        var listKind = il.DeclareLocal(_types.String);
        var numericLength = il.DeclareLocal(_types.Double);
        var length = il.DeclareLocal(_types.Int64);
        var index = il.DeclareLocal(_types.Int64);
        var values = il.DeclareLocal(_types.ListOfObject);
        var stringEquals = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        // CreateListFromArrayLike requires an object; primitive strings do not qualify.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, invalidList);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.TypeOf);
        il.Emit(OpCodes.Stloc, listKind);
        il.Emit(OpCodes.Ldloc, listKind);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Call, stringEquals);
        il.Emit(OpCodes.Brtrue, objectList);
        il.Emit(OpCodes.Ldloc, listKind);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, stringEquals);
        il.Emit(OpCodes.Brtrue, objectList);
        il.MarkLabel(invalidList);
        GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TypeErrorConstructor,
            "Argument list must be an array-like object");
        il.MarkLabel(objectList);

        // Read length and coerce it exactly once. Abstract ToNumber rejects BigInt,
        // unlike the separate Number() conversion helper.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Call, inputs.ToNumber);
        il.Emit(OpCodes.Stloc, numericLength);
        il.Emit(OpCodes.Ldloc, numericLength);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ble_Un, empty); // NaN and nonpositive lengths normalize to zero.

        const long maxSafeInteger = 9007199254740991L;
        il.Emit(OpCodes.Ldloc, numericLength);
        il.Emit(OpCodes.Ldc_R8, (double)maxSafeInteger);
        il.Emit(OpCodes.Blt, boundedLength);
        il.Emit(OpCodes.Ldc_I8, maxSafeInteger);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Br, haveLength);
        il.MarkLabel(boundedLength);
        il.Emit(OpCodes.Ldloc, numericLength);
        il.Emit(OpCodes.Conv_I8); // Positive finite length truncates toward zero.
        il.Emit(OpCodes.Stloc, length);
        il.MarkLabel(haveLength);

        // Grow the independent result incrementally rather than preallocating from
        // an untrusted length. An early getter must still run for huge lengths.
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, values);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, values);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, index);
        il.Emit(OpCodes.Call, _types.GetProperty(typeof(CultureInfo), "InvariantCulture").GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int64, "ToString", typeof(IFormatProvider)));
        il.Emit(OpCodes.Call, inputs.GetIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I8, 1L);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, values);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "ToArray", Type.EmptyTypes));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(empty);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Ret);
    }
}
