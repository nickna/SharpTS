using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $TimeoutClosure class for capturing callback, args, and cancellation token.
    /// This class is used to invoke the callback after the delay in setTimeout.
    /// </summary>
    private void EmitTimeoutClosureClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TimeoutClosure
        var typeBuilder = moduleBuilder.DefineType(
            "$TimeoutClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.TimeoutClosureType = typeBuilder;

        // Fields: Callback ($TSFunction), Args (object[]), Cts (CancellationTokenSource)
        var callbackField = typeBuilder.DefineField("Callback", runtime.TSFunctionType, FieldAttributes.Public);
        var argsField = typeBuilder.DefineField("Args", _types.ObjectArray, FieldAttributes.Public);
        var ctsField = typeBuilder.DefineField("Cts", _types.CancellationTokenSource, FieldAttributes.Public);

        runtime.TimeoutClosureCallback = callbackField;
        runtime.TimeoutClosureArgs = argsField;
        runtime.TimeoutClosureCts = ctsField;

        // Default constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.TimeoutClosureCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);

        // Execute method: public void Execute(Task t)
        // This is called by ContinueWith after the delay completes
        var executeMethod = typeBuilder.DefineMethod(
            "Execute",
            MethodAttributes.Public,
            _types.Void,
            [_types.Task]
        );
        runtime.TimeoutClosureExecute = executeMethod;

        var il = executeMethod.GetILGenerator();
        var skipLabel = il.DefineLabel();

        // if (t.IsCanceled || Cts.IsCancellationRequested) return;
        // Check t.IsCanceled
        il.Emit(OpCodes.Ldarg_1); // t
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCanceled").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // Check Cts.IsCancellationRequested
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, ctsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.CancellationTokenSource, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // Callback.Invoke(Args)
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, callbackField);
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, argsField);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop); // Discard return value

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ret);

        // Finalize the type
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the $TSTimeout class for timer support in compiled assemblies.
    /// Provides unique ID generation, cancellation, and ref/unref behavior.
    /// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSTimeout
    /// </summary>
    private void EmitTSTimeoutClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSTimeout
        var typeBuilder = moduleBuilder.DefineType(
            "$TSTimeout",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.TSTimeoutType = typeBuilder;

        // Static field: private static int _nextId = 0
        var nextIdField = typeBuilder.DefineField(
            "_nextId",
            _types.Int32,
            FieldAttributes.Private | FieldAttributes.Static
        );

        // Instance fields
        var idField = typeBuilder.DefineField("_id", _types.Int32, FieldAttributes.Private);
        var ctsField = typeBuilder.DefineField("_cts", _types.CancellationTokenSource, FieldAttributes.Private);
        var hasRefField = typeBuilder.DefineField("_hasRef", _types.Boolean, FieldAttributes.Private);

        // Constructor: public $TSTimeout(CancellationTokenSource cts)
        EmitTSTimeoutConstructor(typeBuilder, runtime, nextIdField, idField, ctsField, hasRefField);

        // Cancel method: public void Cancel()
        EmitTSTimeoutCancel(typeBuilder, runtime, ctsField);

        // Ref method: public $TSTimeout Ref()
        EmitTSTimeoutRef(typeBuilder, runtime, hasRefField);

        // Unref method: public $TSTimeout Unref()
        EmitTSTimeoutUnref(typeBuilder, runtime, hasRefField);

        // HasRef property getter: public bool HasRef { get; }
        EmitTSTimeoutHasRefGetter(typeBuilder, runtime, hasRefField);

        // ToString override
        EmitTSTimeoutToString(typeBuilder, idField);

        // Finalize the type
        typeBuilder.CreateType();
    }

    private void EmitTSTimeoutConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder nextIdField, FieldBuilder idField, FieldBuilder ctsField, FieldBuilder hasRefField)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.CancellationTokenSource]
        );
        runtime.TSTimeoutCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object));

        // _id = Interlocked.Increment(ref _nextId)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsflda, nextIdField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Interlocked, "Increment", _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Stfld, idField);

        // _cts = cts
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, ctsField);

        // _hasRef = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, hasRefField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSTimeoutCancel(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder ctsField)
    {
        var method = typeBuilder.DefineMethod(
            "Cancel",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.TSTimeoutCancel = method;

        var il = method.GetILGenerator();
        var doneLabel = il.DefineLabel();

        // if (!_cts.IsCancellationRequested) _cts.Cancel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ctsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.CancellationTokenSource, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doneLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ctsField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.CancellationTokenSource, "Cancel"));

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSTimeoutRef(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder hasRefField)
    {
        var method = typeBuilder.DefineMethod(
            "Ref",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        runtime.TSTimeoutRef = method;

        var il = method.GetILGenerator();

        // _hasRef = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, hasRefField);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSTimeoutUnref(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder hasRefField)
    {
        var method = typeBuilder.DefineMethod(
            "Unref",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        runtime.TSTimeoutUnref = method;

        var il = method.GetILGenerator();

        // _hasRef = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, hasRefField);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSTimeoutHasRefGetter(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder hasRefField)
    {
        var method = typeBuilder.DefineMethod(
            "get_HasRef",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSTimeoutHasRefGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, hasRefField);
        il.Emit(OpCodes.Ret);

        // Define property
        var property = typeBuilder.DefineProperty(
            "HasRef",
            PropertyAttributes.None,
            _types.Boolean,
            Type.EmptyTypes
        );
        property.SetGetMethod(method);
    }

    private void EmitTSTimeoutToString(TypeBuilder typeBuilder, FieldBuilder idField)
    {
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // return $"Timeout {{ _id: {_id} }}"
        il.Emit(OpCodes.Ldstr, "Timeout {{ _id: ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, idField);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Ldstr, " }}");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $TSTimeout SetTimeout($TSFunction callback, double delay, object[] args)
    /// Creates a $TSTimeout and schedules the callback execution after the delay.
    /// Uses Task.Delay with ContinueWith for async callback invocation.
    /// </summary>
    private void EmitSetTimeoutMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetTimeout",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSTimeoutType,
            [runtime.TSFunctionType, _types.Double, _types.ObjectArray]
        );
        runtime.SetTimeout = method;

        var il = method.GetILGenerator();

        // var cts = new CancellationTokenSource();
        var ctsLocal = il.DeclareLocal(_types.CancellationTokenSource);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.CancellationTokenSource));
        il.Emit(OpCodes.Stloc, ctsLocal);

        // var timeout = new $TSTimeout(cts);
        var timeoutLocal = il.DeclareLocal(runtime.TSTimeoutType);
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Newobj, runtime.TSTimeoutCtor);
        il.Emit(OpCodes.Stloc, timeoutLocal);

        // int delayMs = Math.Max(0, (int)delay);
        var delayMsLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // delay
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, delayMsLocal);

        // Create closure: var closure = new $TimeoutClosure();
        var closureLocal = il.DeclareLocal(runtime.TimeoutClosureType);
        il.Emit(OpCodes.Newobj, runtime.TimeoutClosureCtor);
        il.Emit(OpCodes.Stloc, closureLocal);

        // closure.Callback = callback (arg0)
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldarg_0); // callback
        il.Emit(OpCodes.Stfld, runtime.TimeoutClosureCallback);

        // closure.Args = args (arg2)
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldarg_2); // args
        il.Emit(OpCodes.Stfld, runtime.TimeoutClosureArgs);

        // closure.Cts = cts
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Stfld, runtime.TimeoutClosureCts);

        // Create Action<Task> delegate: new Action<Task>(closure.Execute)
        var actionLocal = il.DeclareLocal(_types.ActionOfTask);
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldftn, runtime.TimeoutClosureExecute);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ActionOfTask, _types.Object, _types.IntPtr));
        il.Emit(OpCodes.Stloc, actionLocal);

        // Task.Delay(delayMs, cts.Token).ContinueWith(action)
        il.Emit(OpCodes.Ldloc, delayMsLocal);
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.CancellationTokenSource, "Token").GetGetMethod()!);
        var taskDelayMethod = _types.GetMethod(_types.Task, "Delay", _types.Int32, _types.CancellationToken);
        il.Emit(OpCodes.Call, taskDelayMethod);

        // .ContinueWith(action)
        il.Emit(OpCodes.Ldloc, actionLocal);
        var continueWithMethod = _types.GetMethod(_types.Task, "ContinueWith", _types.ActionOfTask);
        il.Emit(OpCodes.Callvirt, continueWithMethod);
        il.Emit(OpCodes.Pop); // Discard the continuation task

        // return timeout
        il.Emit(OpCodes.Ldloc, timeoutLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ClearTimeout(object handle)
    /// Cancels the timeout if handle is a $TSTimeout.
    /// </summary>
    private void EmitClearTimeoutMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ClearTimeout",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ClearTimeout = method;

        var il = method.GetILGenerator();
        var doneLabel = il.DefineLabel();

        // if (handle is $TSTimeout timeout) timeout.Cancel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSTimeoutType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // Call Cancel on the timeout
        il.Emit(OpCodes.Callvirt, runtime.TSTimeoutCancel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Pop); // Remove null from stack
        il.Emit(OpCodes.Ret);
    }
}
