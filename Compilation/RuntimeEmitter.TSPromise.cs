using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Promise class for standalone Promise support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSPromise core functionality.
/// </summary>
public partial class RuntimeEmitter
{
    // Promise class fields
    private FieldBuilder _tsPromiseTaskField = null!;

    private void EmitTSPromiseClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // First emit the PromiseRejectedException class (needed by Reject method)
        EmitTSPromiseRejectedException(moduleBuilder, runtime);

        // Define class: public class $Promise
        var typeBuilder = moduleBuilder.DefineType(
            "$Promise",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSPromiseType = typeBuilder;

        // Field: private readonly Task<object?> _task
        _tsPromiseTaskField = typeBuilder.DefineField(
            "_task",
            _types.TaskOfObject,
            FieldAttributes.Private
        );

        // Constructor: public $Promise(Task<object?> task)
        EmitTSPromiseConstructor(typeBuilder, runtime);

        // Property: Task (getter)
        EmitTSPromiseTaskProperty(typeBuilder, runtime);

        // Static method: Resolve(object? value)
        EmitTSPromiseResolve(typeBuilder, runtime);

        // Static method: Reject(object? reason)
        EmitTSPromiseReject(typeBuilder, runtime);

        // Method: GetValueAsync()
        EmitTSPromiseGetValueAsync(typeBuilder, runtime);

        // Property: IsCompleted
        EmitTSPromiseIsCompletedProperty(typeBuilder, runtime);

        // Override: ToString()
        EmitTSPromiseToString(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitTSPromiseConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.TaskOfObject]
        );
        runtime.TSPromiseCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // _task = task
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsPromiseTaskField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSPromiseTaskProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty(
            "Task",
            PropertyAttributes.None,
            _types.TaskOfObject,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_Task",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.TaskOfObject,
            Type.EmptyTypes
        );
        runtime.TSPromiseTaskGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsPromiseTaskField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSPromiseResolve(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Resolve",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSPromiseType,
            [_types.Object]
        );
        runtime.TSPromiseResolve = method;

        var il = method.GetILGenerator();
        var notPromiseLabel = il.DefineLabel();

        // If value is already a $Promise, return it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notPromiseLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Ret);

        // Otherwise, create new Promise from Task.FromResult(value)
        il.MarkLabel(notPromiseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Task.GetMethod("FromResult")!.MakeGenericMethod(_types.Object));
        il.Emit(OpCodes.Newobj, runtime.TSPromiseCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSPromiseReject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Reject",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSPromiseType,
            [_types.Object]
        );
        runtime.TSPromiseReject = method;

        var il = method.GetILGenerator();
        var tcsLocal = il.DeclareLocal(_types.TaskCompletionSourceOfObject);

        // var tcs = new TaskCompletionSource<object?>();
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.TaskCompletionSourceOfObject));
        il.Emit(OpCodes.Stloc, tcsLocal);

        // tcs.SetException(new $PromiseRejectedException(reason));
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSPromiseRejectedExceptionCtor);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TaskCompletionSourceOfObject, "SetException", _types.Exception));

        // return new $Promise(tcs.Task);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!);
        il.Emit(OpCodes.Newobj, runtime.TSPromiseCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSPromiseGetValueAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // This is an async method, but for simplicity, we'll emit it as a regular method
        // that returns Task<object?> and handles promise flattening.
        // The actual async state machine would be complex to emit manually.
        // Instead, emit a simple wrapper that awaits and flattens.

        // For standalone compiled code, we'll use a simpler approach:
        // Return the task's result, flattening nested promises
        var method = typeBuilder.DefineMethod(
            "GetValueAsync",
            MethodAttributes.Public,
            _types.TaskOfObject,
            Type.EmptyTypes
        );
        runtime.TSPromiseGetValueAsync = method;

        var il = method.GetILGenerator();

        // For now, just return the underlying task
        // Full flattening would require async state machine emission
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsPromiseTaskField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSPromiseIsCompletedProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty(
            "IsCompleted",
            PropertyAttributes.None,
            _types.Boolean,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_IsCompleted",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes
        );

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsPromiseTaskField);
        il.Emit(OpCodes.Callvirt, _types.Task.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSPromiseToString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // Simple implementation: return "Promise { <status> }"
        var completedLabel = il.DefineLabel();
        var faultedLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsPromiseTaskField);
        il.Emit(OpCodes.Callvirt, _types.Task.GetProperty("IsCompletedSuccessfully")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, completedLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsPromiseTaskField);
        il.Emit(OpCodes.Callvirt, _types.Task.GetProperty("IsFaulted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, faultedLabel);

        // Pending
        il.Emit(OpCodes.Ldstr, "Promise { <pending> }");
        il.Emit(OpCodes.Ret);

        // Completed
        il.MarkLabel(completedLabel);
        il.Emit(OpCodes.Ldstr, "Promise { <resolved> }");
        il.Emit(OpCodes.Ret);

        // Faulted
        il.MarkLabel(faultedLabel);
        il.Emit(OpCodes.Ldstr, "Promise { <rejected> }");
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSPromiseRejectedException(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $PromiseRejectedException : Exception
        var typeBuilder = moduleBuilder.DefineType(
            "$PromiseRejectedException",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Exception
        );
        runtime.TSPromiseRejectedExceptionType = typeBuilder;

        // Field: private readonly object? _reason
        var reasonField = typeBuilder.DefineField(
            "_reason",
            _types.Object,
            FieldAttributes.Private
        );

        // Constructor: public $PromiseRejectedException(object? reason)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.TSPromiseRejectedExceptionCtor = ctor;

        var il = ctor.GetILGenerator();
        var hasReasonLabel = il.DefineLabel();

        // Call base(reason?.ToString() ?? "Promise rejected")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasReasonLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "Promise rejected");
        var afterReasonLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterReasonLabel);
        il.MarkLabel(hasReasonLabel);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.MarkLabel(afterReasonLabel);
        il.Emit(OpCodes.Call, _types.Exception.GetConstructor([_types.String])!);

        // _reason = reason
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, reasonField);

        il.Emit(OpCodes.Ret);

        // Property: Reason (getter)
        var prop = typeBuilder.DefineProperty(
            "Reason",
            PropertyAttributes.None,
            _types.Object,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_Reason",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.TSPromiseRejectedExceptionReasonGetter = getter;

        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, reasonField);
        getIL.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);

        typeBuilder.CreateType();
    }
}
