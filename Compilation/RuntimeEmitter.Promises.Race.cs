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
        var whenAnyAwaiterType = typeof(TaskAwaiter<Task<object?>>);
        var resultAwaiterType = typeof(TaskAwaiter<object?>);

        var shell = DefineCombinatorStateMachineShell(moduleBuilder, "$PromiseRace_SM", "iterable",
            MethodAttributes.Public);
        var whenAnyAwaiterField = shell.Type.DefineField("<>u__1", whenAnyAwaiterType, FieldAttributes.Private);
        var resultAwaiterField = shell.Type.DefineField("<>u__2", resultAwaiterType, FieldAttributes.Private);
        var winningTaskField = shell.Type.DefineField("<winningTask>5__1", typeof(Task<object?>), FieldAttributes.Private);

        return new PromiseRaceStateMachine
        {
            Type = shell.Type,
            StateField = shell.StateField,
            BuilderField = shell.BuilderField,
            IterableField = shell.InputField,
            WhenAnyAwaiterField = whenAnyAwaiterField,
            ResultAwaiterField = resultAwaiterField,
            WinningTaskField = winningTaskField,
            MoveNextMethod = shell.MoveNextMethod,
            BuilderType = shell.BuilderType
        };
    }

    /// <summary>
    /// Emits the PromiseRace wrapper method that creates and starts the state machine.
    /// </summary>
    private void EmitPromiseRaceWrapper(ILGenerator il, PromiseRaceStateMachine sm, EmittedRuntime runtime)
        => EmitCombinatorWrapper(il, sm.Type, sm.StateField, sm.IterableField, sm.BuilderField, sm.BuilderType,
            () =>
            {
                // Normalize inside MoveNext's try block; see PromiseAll.
                il.Emit(OpCodes.Ldarg_0);
            });

    /// <summary>
    /// Emits the MoveNext body for PromiseRace state machine.
    /// Implements: convert list to tasks, await Task.WhenAny, await winning task.
    /// </summary>
    private void EmitPromiseRaceMoveNext(PromiseRaceStateMachine sm, EmittedRuntime runtime)
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

        EmitNormalizeCombinatorIterable(il, runtime, sm.IterableField);

        // ECMA-262 §27.2.4.5 Promise.race: If iterable is not Object → throw TypeError.
        // Without this, a non-iterable arg falls through to Castclass which throws
        // InvalidCastException (string err), failing test262 `err instanceof TypeError`.
        var listLocal = EmitCombinatorIterableGuard(il, runtime, sm.IterableField, "race");

        // ECMA-262 §27.2.4.5: race over an empty iterable returns a promise that
        // NEVER settles — there are no competitors. Route a never-completing task
        // through the winning-task await machinery instead of settling with null.
        var notEmptyLabel = il.DefineLabel();
        var awaitWinningTaskLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // winningTask = new TaskCompletionSource<object?>().Task (never completes)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(TaskCompletionSource<object?>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetProperty("Task")!.GetGetMethod()!);
        il.Emit(OpCodes.Stfld, sm.WinningTaskField);
        il.Emit(OpCodes.Br, awaitWinningTaskLabel);

        il.MarkLabel(notEmptyLabel);

        // Convert elements to tasks (same as PromiseAll)
        var tasksLocal = EmitCombinatorTaskListLoop(il, listLocal);

        // Call Task.WhenAny<object?>(tasks)
        var whenAnyMethod = EmitGenerics.MakeGenericMethod(typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "WhenAny" && m.IsGenericMethod &&
                   m.GetParameters().Length == 1 &&
                   m.GetParameters()[0].ParameterType.IsGenericType &&
                   m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>)), typeof(object));
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Call, whenAnyMethod);

        // Await the WhenAny task (suspends at state 0 when not yet complete)
        il.Emit(OpCodes.Callvirt, typeof(Task<Task<object?>>).GetMethod("GetAwaiter")!);
        EmitCombinatorAwaitSuspend(il, sm.Type, sm.StateField, sm.BuilderField, sm.BuilderType,
            sm.WhenAnyAwaiterField, whenAnyAwaiterType, 0, continue0Label, returnLabel);

        // ========== STATE 0: Resume after WhenAny ==========
        EmitCombinatorResumeState(il, state0Label, sm.StateField);

        // ========== Continue after WhenAny ==========
        il.MarkLabel(continue0Label);

        // GetResult from WhenAny - returns the winning Task<object?>
        // Store it in the winningTask field
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.WhenAnyAwaiterField);
        il.Emit(OpCodes.Call, whenAnyAwaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stfld, sm.WinningTaskField);

        // Get awaiter for winning task (empty-iterable path jumps here with a
        // never-completing winningTask already stored); suspends at state 1
        il.MarkLabel(awaitWinningTaskLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.WinningTaskField);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        EmitCombinatorAwaitSuspend(il, sm.Type, sm.StateField, sm.BuilderField, sm.BuilderType,
            sm.ResultAwaiterField, resultAwaiterType, 1, continue1Label, returnLabel);

        // ========== STATE 1: Resume after winning task ==========
        EmitCombinatorResumeState(il, state1Label, sm.StateField);

        // ========== Continue after winning task ==========
        il.MarkLabel(continue1Label);

        // GetResult from winning task
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ResultAwaiterField);
        il.Emit(OpCodes.Call, resultAwaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // ========== Success path ==========
        il.MarkLabel(setResultLabel);
        EmitCombinatorSetResult(il, sm.StateField, sm.BuilderField, sm.BuilderType, resultLocal, returnLabel);

        // ========== Exception handler ==========
        EmitCombinatorCatchSetException(il, sm.StateField, sm.BuilderField, sm.BuilderType, exceptionLocal, returnLabel);

        il.EndExceptionBlock();

        // Return point
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    #endregion
}
