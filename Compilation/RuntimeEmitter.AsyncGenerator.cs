using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Holds information about the AsyncGeneratorAwaitContinue state machine.
/// </summary>
internal class AsyncGeneratorAwaitContinueStateMachine
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }           // <>1__state
    public required FieldBuilder BuilderField { get; init; }         // AsyncValueTaskMethodBuilder<bool>
    public required FieldBuilder TaskField { get; init; }            // Task<object> input
    public required FieldBuilder GeneratorField { get; init; }       // IAsyncEnumerator<object>
    public required FieldBuilder TaskAwaiterField { get; init; }     // TaskAwaiter<object>
    public required FieldBuilder ValueTaskAwaiterField { get; init; } // ValueTaskAwaiter<bool>
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
}

/// <summary>
/// Emits async generator interface and support methods into the generated assembly.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $IAsyncGenerator interface that extends IAsyncEnumerator&lt;object&gt; with async Return/Throw methods.
    /// </summary>
    private void EmitAsyncGeneratorInterface(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define interface: public interface $IAsyncGenerator : IAsyncEnumerator<object>, IAsyncEnumerable<object>
        var interfaceBuilder = moduleBuilder.DefineType(
            "$IAsyncGenerator",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            null,
            [_types.IAsyncEnumeratorOfObject, _types.IAsyncEnumerableOfObject]
        );
        runtime.AsyncGeneratorInterfaceType = interfaceBuilder;

        // Define next(object sentValue) method: Task<object> next(object)
        // sentValue is the value delivered as the result of the suspended yield expression (#473).
        // Using lowercase to match JavaScript API
        var nextMethod = interfaceBuilder.DefineMethod(
            "next",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.AsyncGeneratorNextMethod = nextMethod;

        // Define return(object value) method: Task<object> return(object value)
        // Note: "return" is a C# keyword but valid as a method name via reflection
        var returnMethod = interfaceBuilder.DefineMethod(
            "return",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.AsyncGeneratorReturnMethod = returnMethod;

        // Define throw(object error) method: Task<object> throw(object error)
        // Note: "throw" is a C# keyword but valid as a method name via reflection
        var throwMethod = interfaceBuilder.DefineMethod(
            "throw",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.AsyncGeneratorThrowMethod = throwMethod;

        interfaceBuilder.CreateType();
    }

    /// <summary>
    /// Emits the AsyncGeneratorAwaitContinue method that awaits a task and then continues with MoveNextAsync.
    /// This replaces the RuntimeTypes.AsyncGeneratorAwaitContinue method for standalone support.
    /// </summary>
    private void EmitAsyncGeneratorAwaitContinueMethods(TypeBuilder typeBuilder, ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define the state machine type
        var sm = DefineAsyncGeneratorAwaitContinueStateMachine(moduleBuilder);

        // Define the wrapper method
        var method = typeBuilder.DefineMethod(
            "AsyncGeneratorAwaitContinue",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(ValueTask<bool>),
            [typeof(Task<object>), _types.IAsyncEnumeratorOfObject]
        );
        runtime.AsyncGeneratorAwaitContinue = method;

        // Emit wrapper body
        EmitAsyncGeneratorAwaitContinueWrapper(method.GetILGenerator(), sm);

        // Emit MoveNext body
        EmitAsyncGeneratorAwaitContinueMoveNext(sm);

        // Create the state machine type
        sm.Type.CreateType();

        // Emit the next()-result builder used by truly-async next() (#631/#542).
        EmitAsyncGeneratorBuildResultMethod(typeBuilder, moduleBuilder, runtime);
    }

    /// <summary>
    /// Emits <c>static Task&lt;object&gt; AsyncGeneratorBuildResult(ValueTask&lt;bool&gt; moveNext,
    /// IAsyncEnumerator&lt;object&gt; gen)</c> — an async helper that awaits a MoveNextAsync result and
    /// produces the <c>{ value: gen.Current, done: !moved }</c> iterator-result dictionary.
    /// <para>
    /// This is what lets the emitted async-generator <c>next()</c> be truly asynchronous: instead of
    /// blocking the event-loop thread on <c>MoveNextAsync().AsTask().GetResult()</c> (which deadlocks a
    /// genuinely-async await — the continuation needs the very thread that is blocked, #631), <c>next()</c>
    /// drives one step and hands the (possibly pending) ValueTask here, returning the Task this produces.
    /// A faulted MoveNext (uncaught body throw) surfaces as a faulted Task — i.e. a rejected next()
    /// promise — exactly as ECMA-262 §27.6.1.2 requires.
    /// </para>
    /// </summary>
    private void EmitAsyncGeneratorBuildResultMethod(TypeBuilder typeBuilder, ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var builderType = _types.MakeGenericType(typeof(System.Runtime.CompilerServices.AsyncTaskMethodBuilder<>), _types.Object);
        var valueTaskAwaiterType = _types.ValueTaskAwaiterOfBool;

        // --- State machine type ---
        var smType = moduleBuilder.DefineType(
            "$AsyncGeneratorBuildResult_StateMachine",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]);

        var stateField = smType.DefineField("<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = smType.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var genField = smType.DefineField("gen", _types.IAsyncEnumeratorOfObject, FieldAttributes.Public);
        var awaiterField = smType.DefineField("<>u__1", valueTaskAwaiterType, FieldAttributes.Public);

        var moveNext = smType.DefineMethod(
            "MoveNext",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void), Type.EmptyTypes);
        smType.DefineMethodOverride(moveNext, _types.AsyncStateMachineMoveNext);

        var setStateMachine = smType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void), [typeof(IAsyncStateMachine)]);
        smType.DefineMethodOverride(setStateMachine, _types.AsyncStateMachineSetStateMachine);
        setStateMachine.GetILGenerator().Emit(OpCodes.Ret);

        // --- Wrapper: AsyncGeneratorBuildResult(ValueTask<bool> moveNext, IAsyncEnumerator<object> gen) ---
        var method = typeBuilder.DefineMethod(
            "AsyncGeneratorBuildResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.ValueTaskOfBool, _types.IAsyncEnumeratorOfObject]);
        runtime.AsyncGeneratorBuildResult = method;

        {
            var il = method.GetILGenerator();
            var smLocal = il.DeclareLocal(smType);
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Initobj, smType);
            // sm.<>1__state = -1
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Stfld, stateField);
            // sm.<>u__1 = moveNext.GetAwaiter()
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.ValueTaskOfBool, "GetAwaiter"));
            il.Emit(OpCodes.Stfld, awaiterField);
            // sm.gen = arg1
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, genField);
            // sm.<>t__builder = AsyncTaskMethodBuilder<object>.Create()
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Call, builderType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!);
            il.Emit(OpCodes.Stfld, builderField);
            // sm.<>t__builder.Start(ref sm)
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldflda, builderField);
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Call, builderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == "Start" && m.IsGenericMethod).MakeGenericMethod(smType));
            // return sm.<>t__builder.Task
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldflda, builderField);
            il.Emit(OpCodes.Call, builderType.GetProperty("Task", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!);
            il.Emit(OpCodes.Ret);
        }

        // --- MoveNext ---
        {
            var il = moveNext.GetILGenerator();
            var exLocal = il.DeclareLocal(typeof(Exception));
            var movedLocal = il.DeclareLocal(_types.Boolean);
            var resultLocal = il.DeclareLocal(_types.Object);
            var resumeLabel = il.DefineLabel();
            var continueLabel = il.DefineLabel();
            var returnLabel = il.DefineLabel();

            il.BeginExceptionBlock();

            // switch (state) { case 0: goto resume; default: fall through (-1) }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, stateField);
            il.Emit(OpCodes.Switch, [resumeLabel]);

            // state -1: if (awaiter.IsCompleted) goto continue;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, awaiterField);
            il.Emit(OpCodes.Call, valueTaskAwaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, continueLabel);

            // not completed: suspend
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, stateField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, builderField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, awaiterField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, builderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
                .MakeGenericMethod(valueTaskAwaiterType, smType));
            il.Emit(OpCodes.Leave, returnLabel);

            // state 0 resume:
            il.MarkLabel(resumeLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Stfld, stateField);

            il.MarkLabel(continueLabel);
            // moved = awaiter.GetResult()  (throws if the body faulted → faults this Task)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, awaiterField);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(valueTaskAwaiterType, "GetResult"));
            il.Emit(OpCodes.Stloc, movedLocal);

            // result = new Dictionary<string,object?> { ["value"] = gen.Current, ["done"] = !moved }
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "value");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, genField);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IAsyncEnumeratorOfObject, "Current"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "done");
            il.Emit(OpCodes.Ldloc, movedLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);                 // !moved
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
            il.Emit(OpCodes.Stloc, resultLocal);

            // state = -2; builder.SetResult(result)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, -2);
            il.Emit(OpCodes.Stfld, stateField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, builderField);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Call, builderType.GetMethod("SetResult")!);
            il.Emit(OpCodes.Leave, returnLabel);

            // catch (Exception e) { state=-2; builder.SetException(e); }
            il.BeginCatchBlock(typeof(Exception));
            il.Emit(OpCodes.Stloc, exLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, -2);
            il.Emit(OpCodes.Stfld, stateField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, builderField);
            il.Emit(OpCodes.Ldloc, exLocal);
            il.Emit(OpCodes.Call, builderType.GetMethod("SetException")!);
            il.Emit(OpCodes.Leave, returnLabel);
            il.EndExceptionBlock();

            il.MarkLabel(returnLabel);
            il.Emit(OpCodes.Ret);
        }

        smType.CreateType();
    }

    /// <summary>
    /// Defines the state machine type for AsyncGeneratorAwaitContinue.
    /// </summary>
    private AsyncGeneratorAwaitContinueStateMachine DefineAsyncGeneratorAwaitContinueStateMachine(ModuleBuilder moduleBuilder)
    {
        var builderType = typeof(AsyncValueTaskMethodBuilder<bool>);
        var taskAwaiterType = typeof(TaskAwaiter<object>);
        var valueTaskAwaiterType = typeof(ValueTaskAwaiter<bool>);

        var typeBuilder = moduleBuilder.DefineType(
            "$AsyncGeneratorAwaitContinue_StateMachine",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]
        );

        // Fields
        var stateField = typeBuilder.DefineField("<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = typeBuilder.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var taskField = typeBuilder.DefineField("task", typeof(Task<object>), FieldAttributes.Public);
        var generatorField = typeBuilder.DefineField("generator", _types.IAsyncEnumeratorOfObject, FieldAttributes.Public);
        var taskAwaiterField = typeBuilder.DefineField("<>u__1", taskAwaiterType, FieldAttributes.Private);
        var valueTaskAwaiterField = typeBuilder.DefineField("<>u__2", valueTaskAwaiterType, FieldAttributes.Private);

        // MoveNext method
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes
        );
        typeBuilder.DefineMethodOverride(moveNext, _types.AsyncStateMachineMoveNext);

        // SetStateMachine method
        var setStateMachine = typeBuilder.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(IAsyncStateMachine)]
        );
        typeBuilder.DefineMethodOverride(setStateMachine, _types.AsyncStateMachineSetStateMachine);
        var setIL = setStateMachine.GetILGenerator();
        setIL.Emit(OpCodes.Ret);

        return new AsyncGeneratorAwaitContinueStateMachine
        {
            Type = typeBuilder,
            StateField = stateField,
            BuilderField = builderField,
            TaskField = taskField,
            GeneratorField = generatorField,
            TaskAwaiterField = taskAwaiterField,
            ValueTaskAwaiterField = valueTaskAwaiterField,
            MoveNextMethod = moveNext,
            BuilderType = builderType
        };
    }

    /// <summary>
    /// Emits the wrapper method body that creates and starts the state machine.
    /// </summary>
    private void EmitAsyncGeneratorAwaitContinueWrapper(ILGenerator il, AsyncGeneratorAwaitContinueStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);

        // Initialize state machine
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, sm.Type);

        // sm.<>1__state = -1
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // sm.task = arg0
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, sm.TaskField);

        // sm.generator = arg1
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, sm.GeneratorField);

        // sm.<>t__builder = AsyncValueTaskMethodBuilder<bool>.Create()
        il.Emit(OpCodes.Ldloca, smLocal);
        var createMethod = sm.BuilderType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        il.Emit(OpCodes.Call, createMethod);
        il.Emit(OpCodes.Stfld, sm.BuilderField);

        // sm.<>t__builder.Start(ref sm)
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloca, smLocal);
        var startMethod = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Start" && m.IsGenericMethod)
            .MakeGenericMethod(sm.Type);
        il.Emit(OpCodes.Call, startMethod);

        // return sm.<>t__builder.Task
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        var taskGetter = sm.BuilderType.GetProperty("Task", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
        il.Emit(OpCodes.Call, taskGetter);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the MoveNext body for the AsyncGeneratorAwaitContinue state machine.
    /// State machine flow:
    ///   State -1: Initial - await task.GetAwaiter()
    ///   State 0: After task completes - call generator.MoveNextAsync(), await result
    ///   State 1: After MoveNextAsync completes - return result
    /// </summary>
    private void EmitAsyncGeneratorAwaitContinueMoveNext(AsyncGeneratorAwaitContinueStateMachine sm)
    {
        var il = sm.MoveNextMethod.GetILGenerator();

        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(bool));

        // Labels for state dispatch
        var state0Label = il.DefineLabel();
        var state1Label = il.DefineLabel();
        var continueAfterTaskAwait = il.DefineLabel();
        var continueAfterMoveNextAwait = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // Begin outer try block
        il.BeginExceptionBlock();

        // State dispatch
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);

        // switch (state) { case 0: goto state0Label; case 1: goto state1Label; default: continue }
        il.Emit(OpCodes.Switch, [state0Label, state1Label]);

        // ========== STATE -1: Initial - await task ==========

        // Get task awaiter: this.task.GetAwaiter()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.TaskField);
        il.Emit(OpCodes.Callvirt, typeof(Task<object>).GetMethod("GetAwaiter")!);

        // Store awaiter
        var taskAwaiterLocal = il.DeclareLocal(typeof(TaskAwaiter<object>));
        il.Emit(OpCodes.Stloc, taskAwaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, taskAwaiterLocal);
        il.Emit(OpCodes.Stfld, sm.TaskAwaiterField);

        // Check if completed
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.TaskAwaiterField);
        il.Emit(OpCodes.Call, typeof(TaskAwaiter<object>).GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continueAfterTaskAwait);

        // Not completed - suspend
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.TaskAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(typeof(TaskAwaiter<object>), sm.Type);
        il.Emit(OpCodes.Call, awaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 0: Resume after task await ==========
        il.MarkLabel(state0Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue after task await ==========
        il.MarkLabel(continueAfterTaskAwait);

        // Deliberately do NOT call awaiter.GetResult() here. A *rejected* awaited task must reach the
        // generator body's own resume point (which reads its AwaiterField.GetResult and re-throws into
        // the body's try/catch), not be re-thrown here — calling GetResult would fault this helper's
        // ValueTask and bypass the body's resume entirely, so a pending rejection inside a guest
        // try/catch could never reach its catch (#631, #617). We only needed the await above to wait
        // for completion; resuming MoveNextAsync regardless of fault lets the body observe it (mirrors
        // the ContinueWith-based RuntimeTypes.AsyncGeneratorAwaitContinue).

        // Call generator.MoveNextAsync()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.GeneratorField);
        il.Emit(OpCodes.Callvirt, _types.IAsyncEnumeratorOfObject.GetMethod("MoveNextAsync")!);

        // Get awaiter from ValueTask<bool>
        var valueTaskLocal = il.DeclareLocal(typeof(ValueTask<bool>));
        il.Emit(OpCodes.Stloc, valueTaskLocal);
        il.Emit(OpCodes.Ldloca, valueTaskLocal);
        il.Emit(OpCodes.Call, typeof(ValueTask<bool>).GetMethod("GetAwaiter")!);

        // Store awaiter
        var valueTaskAwaiterLocal = il.DeclareLocal(typeof(ValueTaskAwaiter<bool>));
        il.Emit(OpCodes.Stloc, valueTaskAwaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valueTaskAwaiterLocal);
        il.Emit(OpCodes.Stfld, sm.ValueTaskAwaiterField);

        // Check if completed
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ValueTaskAwaiterField);
        il.Emit(OpCodes.Call, typeof(ValueTaskAwaiter<bool>).GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continueAfterMoveNextAwait);

        // Not completed - suspend
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ValueTaskAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod2 = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(typeof(ValueTaskAwaiter<bool>), sm.Type);
        il.Emit(OpCodes.Call, awaitMethod2);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 1: Resume after MoveNextAsync await ==========
        il.MarkLabel(state1Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue after MoveNextAsync await ==========
        il.MarkLabel(continueAfterMoveNextAwait);

        // Get result
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ValueTaskAwaiterField);
        il.Emit(OpCodes.Call, typeof(ValueTaskAwaiter<bool>).GetMethod("GetResult")!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Set state to -2 and SetResult
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, sm.BuilderType.GetMethod("SetResult")!);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== Exception handler ==========
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, exceptionLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, sm.BuilderType.GetMethod("SetException")!);
        il.Emit(OpCodes.Leave, returnLabel);

        il.EndExceptionBlock();

        // Return point
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }
}
