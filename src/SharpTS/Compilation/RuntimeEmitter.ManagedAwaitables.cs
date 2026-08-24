using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the standalone CLR Task/ValueTask normalization seam used by dynamic
    /// external interop. The generated assembly cannot depend on SharpTS.dll, so
    /// this adapter intentionally lives in the emitted runtime.
    /// </summary>
    private void EmitManagedAwaitableInterop(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        MethodBuilder normalizeResult = typeBuilder.DefineMethod(
            "NormalizeManagedResult",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        MethodBuilder complete = typeBuilder.DefineMethod(
            "CompleteManagedAwaitable",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.Task, _types.Object]);
        MethodBuilder normalize = typeBuilder.DefineMethod(
            "NormalizeManagedAwaitable",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.NormalizeManagedAwaitable = normalize;

        EmitNormalizeManagedResult(normalizeResult);
        EmitCompleteManagedAwaitable(complete, normalizeResult);
        EmitNormalizeManagedAwaitable(normalize, complete);
    }

    private void EmitNormalizeManagedResult(MethodBuilder method)
    {
        ILGenerator il = method.GetILGenerator();
        var array = il.DeclareLocal(_types.ArrayType);
        var result = il.DeclareLocal(_types.ListOfObject);
        var index = il.DeclareLocal(_types.Int32);
        var notArray = il.DefineLabel();
        var loop = il.DefineLabel();
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ArrayType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notArray);
        il.Emit(OpCodes.Stloc, array);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, array);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ArrayType, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, array);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ArrayType, "GetValue", _types.Int32));
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notArray);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCompleteManagedAwaitable(MethodBuilder method, MethodBuilder normalizeResult)
    {
        ILGenerator il = method.GetILGenerator();
        Type tcsType = _types.TaskCompletionSourceOfObject;
        var tcs = il.DeclareLocal(tcsType);
        var property = il.DeclareLocal(_types.PropertyInfo);
        var success = il.DefineLabel();
        var hasProperty = il.DefineLabel();
        var setNull = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, tcsType);
        il.Emit(OpCodes.Stloc, tcs);

        var notCanceled = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCanceled").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notCanceled);
        il.Emit(OpCodes.Ldloc, tcs);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(tcsType, "TrySetCanceled", Type.EmptyTypes));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notCanceled);

        var notFaulted = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsFaulted").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notFaulted);
        il.Emit(OpCodes.Ldloc, tcs);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "Exception").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetMethod(nameof(Exception.GetBaseException))!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(tcsType, "TrySetException", _types.Exception));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notFaulted);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "Result");
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod(nameof(Type.GetProperty), [typeof(string)])!);
        il.Emit(OpCodes.Stloc, property);
        il.Emit(OpCodes.Ldloc, property);
        il.Emit(OpCodes.Brtrue, hasProperty);
        il.Emit(OpCodes.Br, setNull);

        il.MarkLabel(hasProperty);
        il.Emit(OpCodes.Ldloc, property);
        il.Emit(OpCodes.Callvirt, typeof(PropertyInfo).GetProperty(nameof(PropertyInfo.PropertyType))!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(MemberInfo).GetProperty(nameof(MemberInfo.Name))!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "VoidTaskResult");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, setNull);
        il.Emit(OpCodes.Ldloc, tcs);
        il.Emit(OpCodes.Ldloc, property);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(PropertyInfo).GetMethod(nameof(PropertyInfo.GetValue), [typeof(object)])!);
        il.Emit(OpCodes.Call, normalizeResult);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(tcsType, "TrySetResult", _types.Object));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, success);

        il.MarkLabel(setNull);
        il.Emit(OpCodes.Ldloc, tcs);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(tcsType, "TrySetResult", _types.Object));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(success);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNormalizeManagedAwaitable(MethodBuilder method, MethodBuilder complete)
    {
        ILGenerator il = method.GetILGenerator();
        Type tcsType = _types.TaskCompletionSourceOfObject;
        Type continuationType = typeof(Action<Task, object?>);
        var task = il.DeclareLocal(_types.Task);
        var tcs = il.DeclareLocal(tcsType);
        var valueTask = il.DeclareLocal(typeof(ValueTask));
        var valueType = il.DeclareLocal(_types.Type);
        var adaptTask = il.DefineLabel();
        var notObjectTask = il.DefineLabel();
        var notTask = il.DefineLabel();
        var notValueTask = il.DefineLabel();
        var returnOriginal = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notObjectTask);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notObjectTask);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Task);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notTask);
        il.Emit(OpCodes.Stloc, task);
        il.Emit(OpCodes.Br, adaptTask);

        il.MarkLabel(notTask);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(ValueTask));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notValueTask);
        il.Emit(OpCodes.Unbox_Any, typeof(ValueTask));
        il.Emit(OpCodes.Stloc, valueTask);
        il.Emit(OpCodes.Ldloca, valueTask);
        il.Emit(OpCodes.Call, typeof(ValueTask).GetMethod(nameof(ValueTask.AsTask))!);
        il.Emit(OpCodes.Stloc, task);
        il.Emit(OpCodes.Br, adaptTask);

        il.MarkLabel(notValueTask);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, valueType);
        il.Emit(OpCodes.Ldloc, valueType);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.IsGenericType))!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnOriginal);
        il.Emit(OpCodes.Ldloc, valueType);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod(nameof(Type.GetGenericTypeDefinition))!);
        il.Emit(OpCodes.Ldtoken, typeof(ValueTask<>));
        il.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!);
        il.Emit(OpCodes.Call, typeof(Type).GetMethod("op_Equality", [typeof(Type), typeof(Type)])!);
        il.Emit(OpCodes.Brfalse, returnOriginal);
        il.Emit(OpCodes.Ldloc, valueType);
        il.Emit(OpCodes.Ldstr, nameof(ValueTask.AsTask));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod(nameof(Type.GetMethod), [typeof(string)])!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(typeof(object)));
        il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod(nameof(MethodBase.Invoke), [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Castclass, _types.Task);
        il.Emit(OpCodes.Stloc, task);
        il.Emit(OpCodes.Br, adaptTask);

        il.MarkLabel(returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(adaptTask);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(tcsType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tcs);
        il.Emit(OpCodes.Ldloc, task);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, complete);
        il.Emit(OpCodes.Newobj, continuationType.GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Ldloc, tcs);
        il.Emit(OpCodes.Call, typeof(CancellationToken).GetProperty(nameof(CancellationToken.None))!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
        il.Emit(OpCodes.Call, typeof(TaskScheduler).GetProperty(nameof(TaskScheduler.Default))!.GetGetMethod()!);
        MethodInfo continueWith = typeof(Task).GetMethod(nameof(Task.ContinueWith),
            [continuationType, typeof(object), typeof(CancellationToken), typeof(TaskContinuationOptions), typeof(TaskScheduler)])!;
        il.Emit(OpCodes.Callvirt, continueWith);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, tcs);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(tcsType, "Task").GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }
}
