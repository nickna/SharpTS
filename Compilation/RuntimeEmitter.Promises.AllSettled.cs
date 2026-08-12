using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    #region ProcessElementSettled State Machine

    /// <summary>
    /// Defines the ProcessElementSettled helper state machine type structure.
    /// This helper handles a single element for PromiseAllSettled with try/catch.
    /// </summary>
    private ProcessElementSettledStateMachine DefineProcessElementSettledStateMachine(ModuleBuilder moduleBuilder)
    {
        var awaiterType = typeof(TaskAwaiter<object?>);
        var shell = DefineCombinatorStateMachineShell(moduleBuilder, "$ProcessElementSettled_StateMachine", "element",
            MethodAttributes.Private);
        var awaiterField = shell.Type.DefineField("<>u__1", awaiterType, FieldAttributes.Private);

        return new ProcessElementSettledStateMachine
        {
            Type = shell.Type,
            StateField = shell.StateField,
            BuilderField = shell.BuilderField,
            ElementField = shell.InputField,
            AwaiterField = awaiterField,
            MoveNextMethod = shell.MoveNextMethod,
            BuilderType = shell.BuilderType,
            AwaiterType = awaiterType
        };
    }

    /// <summary>
    /// Emits the wrapper method that creates the state machine and starts it.
    /// </summary>
    private void EmitProcessElementSettledWrapper(ILGenerator il, ProcessElementSettledStateMachine sm)
        => EmitCombinatorWrapper(il, sm.Type, sm.StateField, sm.ElementField, sm.BuilderField, sm.BuilderType,
            () => il.Emit(OpCodes.Ldarg_0)); // sm.element = arg0 (no promise-list normalization)

    /// <summary>
    /// Emits the MoveNext body for ProcessElementSettled state machine.
    /// Handles a single element with try/catch, returns {status, value/reason} dictionary.
    /// Uses a single try/catch and converts all exceptions to "rejected" dictionaries.
    /// </summary>
    private void EmitProcessElementSettledMoveNext(ProcessElementSettledStateMachine sm, EmittedRuntime runtime)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var dictType = typeof(Dictionary<string, object?>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));
        var valueLocal = il.DeclareLocal(typeof(object));
        var dictLocal = il.DeclareLocal(dictType);

        // Labels
        var state0Label = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();
        var nonTaskLabel = il.DefineLabel();
        var afterAwaitSetupLabel = il.DefineLabel();
        var setResultLabel = il.DefineLabel();

        // Begin try block - exceptions are converted to "rejected" dictionaries
        il.BeginExceptionBlock();

        // State dispatch: if (this.<>1__state == 0) goto state0Label
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, state0Label);

        // ========== STATE -1: Initial execution ==========

        // Check if element is Task<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.ElementField);
        il.Emit(OpCodes.Isinst, typeof(Task<object?>));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, nonTaskLabel);

        // It's a task - await it (suspends at state 0 when not yet complete)
        var taskLocal = il.DeclareLocal(typeof(Task<object?>));
        il.Emit(OpCodes.Stloc, taskLocal);
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        EmitCombinatorAwaitSuspend(il, sm.Type, sm.StateField, sm.BuilderField, sm.BuilderType,
            sm.AwaiterField, sm.AwaiterType, 0, continueLabel, returnLabel);

        // ========== nonTaskLabel: Element is not a Task, use as value directly ==========
        il.MarkLabel(nonTaskLabel);
        il.Emit(OpCodes.Pop);  // pop the null from isinst
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.ElementField);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, afterAwaitSetupLabel);

        // ========== STATE 0: Resume after await ==========
        EmitCombinatorResumeState(il, state0Label, sm.StateField);

        // ========== continueLabel: Completed synchronously or resumed ==========
        il.MarkLabel(continueLabel);
        // GetResult may throw if the task faulted - this is caught by our exception handler
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.AwaiterType, "GetResult")!);
        il.Emit(OpCodes.Stloc, valueLocal);

        // ========== afterAwaitSetupLabel: Create fulfilled dictionary ==========
        il.MarkLabel(afterAwaitSetupLabel);

        // Create Dictionary { ["status"] = "fulfilled", ["value"] = value }
        il.Emit(OpCodes.Newobj, dictType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "status");
        il.Emit(OpCodes.Ldstr, "fulfilled");
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        // ========== Exception handler: Create rejected dictionary ==========
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, exceptionLocal);

        // Create Dictionary { ["status"] = "rejected", ["reason"] = WrapException(ex) }
        // — the guest rejection value (thrown error object / rejection reason),
        // not the host exception's Message string (#232).
        il.Emit(OpCodes.Newobj, dictType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "status");
        il.Emit(OpCodes.Ldstr, "rejected");
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "reason");
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, runtime.WrapException);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        il.EndExceptionBlock();

        // ========== setResultLabel: Set result and complete ==========
        // Unlike the main combinators, this sits OUTSIDE the exception block
        // (both the fulfilled and rejected paths leave to it) and falls
        // through to the return point, so the shared SetResult epilogue
        // (which emits a `leave`) does not apply.
        il.MarkLabel(setResultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetResult")!);

        // Return point
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region PromiseAllSettled State Machine

    /// <summary>
    /// Defines the PromiseAllSettled main state machine type structure.
    /// Uses ProcessElementSettled helper + WhenAll pattern.
    /// </summary>
    private PromiseAllSettledStateMachine DefinePromiseAllSettledStateMachine(ModuleBuilder moduleBuilder)
    {
        var awaiterType = typeof(TaskAwaiter<object?[]>);
        var shell = DefineCombinatorStateMachineShell(moduleBuilder, "$PromiseAllSettled_StateMachine", "iterable",
            MethodAttributes.Private);
        var awaiterField = shell.Type.DefineField("<>u__1", awaiterType, FieldAttributes.Private);

        return new PromiseAllSettledStateMachine
        {
            Type = shell.Type,
            StateField = shell.StateField,
            BuilderField = shell.BuilderField,
            IterableField = shell.InputField,
            ConstructorField = shell.ConstructorField,
            AwaiterField = awaiterField,
            MoveNextMethod = shell.MoveNextMethod,
            BuilderType = shell.BuilderType,
            AwaiterType = awaiterType
        };
    }

    /// <summary>
    /// Emits the wrapper method for PromiseAllSettled.
    /// </summary>
    private void EmitPromiseAllSettledWrapper(ILGenerator il, PromiseAllSettledStateMachine sm, MethodBuilder processElementSettled, EmittedRuntime runtime)
        => EmitCombinatorWrapper(il, sm.Type, sm.StateField, sm.IterableField, sm.BuilderField, sm.BuilderType,
            () =>
            {
                // Normalize inside MoveNext's try block; see PromiseAll.
                il.Emit(OpCodes.Ldarg_0);
            }, sm.ConstructorField, () => il.Emit(OpCodes.Ldarg_1));

    /// <summary>
    /// Emits the MoveNext body for PromiseAllSettled state machine.
    /// Maps elements to ProcessElementSettled helper, uses WhenAll pattern.
    /// </summary>
    private void EmitPromiseAllSettledMoveNext(PromiseAllSettledStateMachine sm, MethodBuilder processElementSettled, EmittedRuntime runtime)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var listType = typeof(List<object?>);
        var taskListType = typeof(List<Task<object?>>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        var setResultLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // Begin outer try block
        il.BeginExceptionBlock();

        // State dispatch: if (this.<>1__state == 0) goto state0Label
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, state0Label);

        // ========== STATE -1: Initial execution ==========

        EmitNormalizeCombinatorIterable(il, runtime, sm.IterableField, sm.ConstructorField);

        // ECMA-262 §27.2.4.3 Promise.allSettled: non-iterable → reject with TypeError.
        var listLocal = EmitCombinatorIterableGuard(il, runtime, sm.IterableField, "allSettled");

        // Check for empty list
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // Empty list - return empty list immediately
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        il.MarkLabel(notEmptyLabel);

        // Create tasks list
        var tasksLocal = il.DeclareLocal(taskListType);
        il.Emit(OpCodes.Newobj, taskListType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tasksLocal);

        // Loop through input list and call ProcessElementSettled for each —
        // unlike All/Race's raw task conversion, every element (task or not)
        // routes through the helper so per-element failures become
        // {status:"rejected"} entries instead of failing the whole combinator.
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

        // element = list[index]
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        // tasks.Add(ProcessElementSettled(element))
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Call, processElementSettled);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Call Task.WhenAll<object?>(tasks.ToArray())
        var whenAllMethod = EmitGenerics.MakeGenericMethod(typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "WhenAll" && m.IsGenericMethod &&
                   m.GetParameters().Length == 1 &&
                   m.GetParameters()[0].ParameterType.IsArray), typeof(object));
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("ToArray")!);
        il.Emit(OpCodes.Call, whenAllMethod);

        // Await the WhenAll task (suspends at state 0 when not yet complete)
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectArrayGetAwaiter);
        EmitCombinatorAwaitSuspend(il, sm.Type, sm.StateField, sm.BuilderField, sm.BuilderType,
            sm.AwaiterField, sm.AwaiterType, 0, continueLabel, returnLabel);

        // ========== STATE 0: Resume after await ==========
        EmitCombinatorResumeState(il, state0Label, sm.StateField);

        // ========== continueLabel: Completed synchronously or resumed ==========
        il.MarkLabel(continueLabel);

        // GetResult
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.AwaiterType, "GetResult")!);

        // Convert object?[] to List<object?>
        var arrayResultLocal = il.DeclareLocal(typeof(object?[]));
        il.Emit(OpCodes.Stloc, arrayResultLocal);
        il.Emit(OpCodes.Ldloc, arrayResultLocal);
        il.Emit(OpCodes.Newobj, listType.GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // ========== setResultLabel: Success path ==========
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
