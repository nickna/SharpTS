using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits Promise.allKeyed / Promise.allSettledKeyed after the object model
    /// is complete. Both variants share own-key collection and result mapping;
    /// the existing Promise.all/allSettled state machines provide settlement.
    /// </summary>
    private void EmitPromiseKeyedMethodBodies(EmittedRuntime runtime)
    {
        EmitPromiseKeyedBody(runtime.PromiseAllKeyed, runtime.PromiseAll, runtime);
        EmitPromiseKeyedBody(
            runtime.PromiseAllSettledKeyed, runtime.PromiseAllSettled, runtime);
        EmitPromiseKeyedMapResultBody(runtime);
    }

    private void EmitPromiseKeyedBody(
        MethodBuilder method, MethodInfo settlementMethod, EmittedRuntime runtime)
    {
        var il = method.GetILGenerator();
        var validInput = il.DefineLabel();
        var rejectInput = il.DefineLabel();

        // Await Dictionary accepts Objects only. Produce a rejected promise,
        // rather than throwing synchronously, for primitive input.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, rejectInput);
        EmitIsInstanceBranch(il, runtime.UndefinedType, rejectInput);
        EmitIsInstanceBranch(il, _types.Boolean, rejectInput);
        EmitIsInstanceBranch(il, _types.Double, rejectInput);
        EmitIsInstanceBranch(il, _types.Int32, rejectInput);
        EmitIsInstanceBranch(il, _types.String, rejectInput);
        EmitIsInstanceBranch(il, runtime.TSSymbolType, rejectInput);
        EmitIsInstanceBranch(il, typeof(System.Numerics.BigInteger), rejectInput);
        il.Emit(OpCodes.Br, validInput);

        il.MarkLabel(rejectInput);
        il.Emit(OpCodes.Ldstr, "Promise keyed combinator requires an object argument");
        il.Emit(OpCodes.Newobj, runtime.TSTypeErrorCtor);
        il.Emit(OpCodes.Call, runtime.PromiseReject);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validInput);
        var listType = _types.ListOfObject;
        var keys = il.DeclareLocal(listType);
        var symbols = il.DeclareLocal(listType);
        var values = il.DeclareLocal(listType);
        var index = il.DeclareLocal(_types.Int32);
        var key = il.DeclareLocal(_types.Object);

        // Own enumerable string keys, already descriptor-filtered and ordered.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetKeys);
        il.Emit(OpCodes.Stloc, keys);

        // Append enumerable symbol keys after strings, preserving
        // [[OwnPropertyKeys]] order within the symbol portion.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetOwnPropertySymbols);
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Stloc, symbols);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        var symbolLoop = il.DefineLabel();
        var symbolNext = il.DefineLabel();
        var symbolsDone = il.DefineLabel();
        il.MarkLabel(symbolLoop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, symbols);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, symbolsDone);
        il.Emit(OpCodes.Ldloc, symbols);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, runtime.PropertyIsEnumerableHelperMethod);
        il.Emit(OpCodes.Brfalse, symbolNext);
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", _types.Object));
        il.MarkLabel(symbolNext);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, symbolLoop);
        il.MarkLabel(symbolsDone);

        // Read each selected property in key order and feed the existing
        // Promise combinator. GetIndex preserves accessors and Symbol keys.
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(listType));
        il.Emit(OpCodes.Stloc, values);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        var valueLoop = il.DefineLabel();
        var valuesDone = il.DefineLabel();
        il.MarkLabel(valueLoop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, valuesDone);
        il.Emit(OpCodes.Ldloc, values);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", _types.Int32));
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, valueLoop);
        il.MarkLabel(valuesDone);

        // ContinueWith's state overload carries the key list without an
        // emitted closure allocation. GetAwaiter().GetResult() in the mapper
        // preserves the original rejection exception rather than wrapping it.
        var callbackType = _types.MakeGenericType(
            typeof(Func<,,>), _types.TaskOfObject, _types.Object, _types.Object);
        var continueDefinition = typeof(Task<object>).GetMethods()
            .Single(candidate =>
            {
                if (candidate.Name != "ContinueWith" ||
                    !candidate.IsGenericMethodDefinition)
                    return false;
                var parameters = candidate.GetParameters();
                return parameters.Length == 2 &&
                    parameters[0].ParameterType.IsGenericType &&
                    parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<,,>) &&
                    parameters[0].ParameterType.GetGenericArguments()[0] == typeof(Task<object>) &&
                    parameters[1].ParameterType == typeof(object);
            });
        var continueMethod = EmitGenerics.MakeGenericMethod(
            continueDefinition, _types.Object);

        il.Emit(OpCodes.Ldloc, values);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Call, settlementMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, runtime.PromiseKeyedMapResult);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(
            callbackType, _types.Object, _types.IntPtr));
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Callvirt, continueMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitPromiseKeyedMapResultBody(EmittedRuntime runtime)
    {
        var il = runtime.PromiseKeyedMapResult.GetILGenerator();
        var listType = _types.ListOfObject;
        var keys = il.DeclareLocal(listType);
        var values = il.DeclareLocal(listType);
        var result = il.DeclareLocal(_types.Object);
        var awaiterType = _types.TaskOfObjectGetAwaiter.ReturnType;
        var awaiter = il.DeclareLocal(awaiterType);
        var index = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Stloc, keys);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        il.Emit(OpCodes.Stloc, awaiter);
        il.Emit(OpCodes.Ldloca, awaiter);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(awaiterType, "GetResult"));
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Stloc, values);

        // CreateKeyedPromiseCombinatorResultObject uses a null prototype.
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Call, runtime.ObjectCreate);
        il.Emit(OpCodes.Stloc, result);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", _types.Int32));
        il.Emit(OpCodes.Ldloc, values);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", _types.Int32));
        il.Emit(OpCodes.Call, runtime.SetIndex);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitIsInstanceBranch(
        ILGenerator il, Type type, Label target)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, type);
        il.Emit(OpCodes.Brtrue, target);
    }
}
