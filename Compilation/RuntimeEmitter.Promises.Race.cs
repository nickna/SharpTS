using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    #region PromiseRace State Machine

    /// <summary>
    /// Defines the PromiseRace state machine type structure.
    /// Requires two awaiter fields: one for WhenAny, one for the winning task.
    /// </summary>
    private PromiseRaceStateMachine DefinePromiseRaceStateMachine(ModuleBuilder moduleBuilder)
    {
        var builderType = typeof(AsyncTaskMethodBuilder<object>);
        var whenAnyAwaiterType = typeof(TaskAwaiter<Task<object?>>);
        var resultAwaiterType = typeof(TaskAwaiter<object?>);

        // Define state machine struct: $PromiseRace_SM
        var smType = moduleBuilder.DefineType(
            "$PromiseRace_SM",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]
        );

        // Define fields
        var stateField = smType.DefineField("<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = smType.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var iterableField = smType.DefineField("iterable", typeof(object), FieldAttributes.Public);
        var whenAnyAwaiterField = smType.DefineField("<>u__1", whenAnyAwaiterType, FieldAttributes.Private);
        var resultAwaiterField = smType.DefineField("<>u__2", resultAwaiterType, FieldAttributes.Private);
        var winningTaskField = smType.DefineField("<winningTask>5__1", typeof(Task<object?>), FieldAttributes.Private);

        // Define MoveNext method
        var moveNext = smType.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes
        );
        smType.DefineMethodOverride(moveNext, typeof(IAsyncStateMachine).GetMethod("MoveNext")!);

        // Define SetStateMachine method (empty body for value types)
        var setStateMachine = smType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(IAsyncStateMachine)]
        );
        smType.DefineMethodOverride(setStateMachine, typeof(IAsyncStateMachine).GetMethod("SetStateMachine")!);
        var setSmIL = setStateMachine.GetILGenerator();
        setSmIL.Emit(OpCodes.Ret);

        return new PromiseRaceStateMachine
        {
            Type = smType,
            StateField = stateField,
            BuilderField = builderField,
            IterableField = iterableField,
            WhenAnyAwaiterField = whenAnyAwaiterField,
            ResultAwaiterField = resultAwaiterField,
            WinningTaskField = winningTaskField,
            MoveNextMethod = moveNext,
            BuilderType = builderType
        };
    }

    /// <summary>
    /// Emits the PromiseRace wrapper method that creates and starts the state machine.
    /// </summary>
    private void EmitPromiseRaceWrapper(ILGenerator il, PromiseRaceStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);

        // Initialize state machine: var sm = default($PromiseRace_SM);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, sm.Type);

        // sm.<>1__state = -1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // sm.iterable = arg0;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, sm.IterableField);

        // sm.<>t__builder = AsyncTaskMethodBuilder<object>.Create();
        il.Emit(OpCodes.Ldloca, smLocal);
        var createMethod = sm.BuilderType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        il.Emit(OpCodes.Call, createMethod);
        il.Emit(OpCodes.Stfld, sm.BuilderField);

        // sm.<>t__builder.Start(ref sm);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloca, smLocal);
        var startMethod = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Start" && m.IsGenericMethod)
            .MakeGenericMethod(sm.Type);
        il.Emit(OpCodes.Call, startMethod);

        // return sm.<>t__builder.Task;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        var taskGetter = sm.BuilderType.GetProperty("Task", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
        il.Emit(OpCodes.Call, taskGetter);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the MoveNext body for PromiseRace state machine.
    /// Implements: convert list to tasks, await Task.WhenAny, await winning task.
    /// </summary>
    private void EmitPromiseRaceMoveNext(PromiseRaceStateMachine sm)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var listType = typeof(List<object?>);
        var taskListType = typeof(List<Task<object?>>);
        var whenAnyAwaiterType = typeof(TaskAwaiter<Task<object?>>);
        var resultAwaiterType = typeof(TaskAwaiter<object?>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();  // Resume after WhenAny
        var state1Label = il.DefineLabel();  // Resume after winning task
        var continue0Label = il.DefineLabel();  // Continue after WhenAny completes
        var continue1Label = il.DefineLabel();  // Continue after winning task completes
        var setResultLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // Begin outer try block
        il.BeginExceptionBlock();

        // State dispatch
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, state0Label);  // state == 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, state1Label);  // state == 1

        // ========== STATE -1: Initial execution ==========

        // Cast iterable to List<object?>
        var listLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.IterableField);
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Stloc, listLocal);

        // Check for empty list - return null immediately
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // Empty list - return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        il.MarkLabel(notEmptyLabel);

        // Create tasks list
        var tasksLocal = il.DeclareLocal(taskListType);
        il.Emit(OpCodes.Newobj, taskListType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tasksLocal);

        // Loop through input list and convert to tasks (same as PromiseAll)
        var indexLocal = il.DeclareLocal(typeof(int));
        var countLocal = il.DeclareLocal(typeof(int));
        var elementLocal = il.DeclareLocal(typeof(object));

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        var isTaskLabel = il.DefineLabel();
        var afterAddLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, typeof(Task<object?>));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, isTaskLabel);
        il.Emit(OpCodes.Pop);

        // Not a task - wrap in Task.FromResult
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Call, typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(object)));
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);
        il.Emit(OpCodes.Br, afterAddLabel);

        il.MarkLabel(isTaskLabel);
        var taskTemp = il.DeclareLocal(typeof(Task<object?>));
        il.Emit(OpCodes.Stloc, taskTemp);
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Ldloc, taskTemp);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);

        il.MarkLabel(afterAddLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Call Task.WhenAny<object?>(tasks)
        var whenAnyMethod = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "WhenAny" && m.IsGenericMethod &&
                   m.GetParameters().Length == 1 &&
                   m.GetParameters()[0].ParameterType.IsGenericType &&
                   m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .MakeGenericMethod(typeof(object));
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Call, whenAnyMethod);

        // GetAwaiter for WhenAny result
        var whenAnyAwaiterLocal = il.DeclareLocal(whenAnyAwaiterType);
        il.Emit(OpCodes.Callvirt, typeof(Task<Task<object?>>).GetMethod("GetAwaiter")!);
        il.Emit(OpCodes.Stloc, whenAnyAwaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, whenAnyAwaiterLocal);
        il.Emit(OpCodes.Stfld, sm.WhenAnyAwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.WhenAnyAwaiterField);
        il.Emit(OpCodes.Call, whenAnyAwaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continue0Label);

        // Not completed - suspend at state 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.WhenAnyAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod0 = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(whenAnyAwaiterType, sm.Type);
        il.Emit(OpCodes.Call, awaitMethod0);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 0: Resume after WhenAny ==========
        il.MarkLabel(state0Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue after WhenAny ==========
        il.MarkLabel(continue0Label);

        // GetResult from WhenAny - returns the winning Task<object?>
        // Store it in the winningTask field
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.WhenAnyAwaiterField);
        il.Emit(OpCodes.Call, whenAnyAwaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stfld, sm.WinningTaskField);

        // Get awaiter for winning task
        var resultAwaiterLocal = il.DeclareLocal(resultAwaiterType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.WinningTaskField);
        il.Emit(OpCodes.Callvirt, typeof(Task<object?>).GetMethod("GetAwaiter")!);
        il.Emit(OpCodes.Stloc, resultAwaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultAwaiterLocal);
        il.Emit(OpCodes.Stfld, sm.ResultAwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ResultAwaiterField);
        il.Emit(OpCodes.Call, resultAwaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continue1Label);

        // Not completed - suspend at state 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ResultAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod1 = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(resultAwaiterType, sm.Type);
        il.Emit(OpCodes.Call, awaitMethod1);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 1: Resume after winning task ==========
        il.MarkLabel(state1Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue after winning task ==========
        il.MarkLabel(continue1Label);

        // GetResult from winning task
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ResultAwaiterField);
        il.Emit(OpCodes.Call, resultAwaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // ========== Success path ==========
        il.MarkLabel(setResultLabel);

        // Set state to -2 (completed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // builder.SetResult(result)
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

    #endregion
}

