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

        // Define next() method: Task<object> next()
        // This wraps MoveNextAsync + Current into a single async call returning iterator result
        // Using lowercase to match JavaScript API
        var nextMethod = interfaceBuilder.DefineMethod(
            "next",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            Type.EmptyTypes
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

        // Get result (we don't use it, just need to trigger any exception)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.TaskAwaiterField);
        il.Emit(OpCodes.Call, typeof(TaskAwaiter<object>).GetMethod("GetResult")!);
        il.Emit(OpCodes.Pop);  // Discard result

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
