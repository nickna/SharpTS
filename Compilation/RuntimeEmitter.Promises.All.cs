using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    #region PromiseAll State Machine

    /// <summary>
    /// Defines the PromiseAll state machine type structure.
    /// </summary>
    private EmittedStateMachine DefinePromiseAllStateMachine(ModuleBuilder moduleBuilder)
    {
        var awaiterType = _types.TaskAwaiterOfObjectArray;
        var shell = DefineCombinatorStateMachineShell(moduleBuilder, "$PromiseAll_SM", "iterable",
            MethodAttributes.Public);
        var awaiterField = shell.Type.DefineField("<>u__1", awaiterType, FieldAttributes.Private);

        return new EmittedStateMachine
        {
            Type = shell.Type,
            StateField = shell.StateField,
            BuilderField = shell.BuilderField,
            IterableField = shell.InputField,
            AwaiterField = awaiterField,
            MoveNextMethod = shell.MoveNextMethod,
            BuilderType = shell.BuilderType,
            AwaiterType = awaiterType
        };
    }

    /// <summary>
    /// Emits the PromiseAll wrapper method that creates and starts the state machine.
    /// </summary>
    private void EmitPromiseAllWrapper(ILGenerator il, EmittedStateMachine sm, EmittedRuntime runtime)
        => EmitCombinatorWrapper(il, sm.Type, sm.StateField, sm.IterableField, sm.BuilderField, sm.BuilderType,
            () =>
            {
                // sm.iterable = NormalizePromiseList(arg0); — $Promise elements
                // (#242 subclasses) become their wrapped Task so the SM awaits them.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.NormalizePromiseListMethod);
            });

    /// <summary>
    /// Emits the MoveNext body for PromiseAll state machine.
    /// Implements: convert list to tasks, await Task.WhenAll, return List.
    /// </summary>
    private void EmitPromiseAllMoveNext(EmittedStateMachine sm, EmittedRuntime runtime)
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
        var setResultLabel = il.DefineLabel();  // success path (empty list jumps here too)
        var returnLabel = il.DefineLabel();

        // Begin outer try block
        il.BeginExceptionBlock();

        // State dispatch: if (this.<>1__state == 0) goto state0Label
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, state0Label);

        // ========== STATE -1: Initial execution ==========

        // ECMA-262 §27.2.4.1 Promise.all: If iterable is not Object → throw TypeError.
        // Without this, a non-iterable arg falls through to Castclass which throws
        // InvalidCastException, failing test262 `err instanceof TypeError`.
        var listLocal = EmitCombinatorIterableGuard(il, runtime, sm.IterableField, "all");

        // Check for empty list
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // Empty list - return empty list immediately (jump to success path)
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        il.MarkLabel(notEmptyLabel);

        // Convert elements to tasks
        var tasksLocal = EmitCombinatorTaskListLoop(il, listLocal);

        // Call Task.WhenAll<object?>(tasks.ToArray())
        // Find the generic WhenAll<TResult>(Task<TResult>[]) and specialize it
        var whenAllMethod = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "WhenAll" && m.IsGenericMethod &&
                   m.GetParameters().Length == 1 &&
                   m.GetParameters()[0].ParameterType.IsArray)
            .MakeGenericMethod(typeof(object));
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("ToArray")!);
        il.Emit(OpCodes.Call, whenAllMethod);

        // Await the WhenAll task (suspends at state 0 when not yet complete)
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectArrayGetAwaiter);
        EmitCombinatorAwaitSuspend(il, sm.Type, sm.StateField, sm.BuilderField, sm.BuilderType,
            sm.AwaiterField, sm.AwaiterType, 0, continueLabel, returnLabel);

        // ========== STATE 0: Resume after await ==========
        EmitCombinatorResumeState(il, state0Label, sm.StateField);

        // ========== Continue (completed synchronously or resumed) ==========
        il.MarkLabel(continueLabel);

        // GetResult
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, sm.AwaiterType.GetMethod("GetResult")!);

        // Convert object?[] to List<object?> using constructor
        var arrayResultLocal = il.DeclareLocal(typeof(object?[]));
        il.Emit(OpCodes.Stloc, arrayResultLocal);
        il.Emit(OpCodes.Ldloc, arrayResultLocal);
        il.Emit(OpCodes.Newobj, listType.GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // ========== Success path - both normal and empty list converge here ==========
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
