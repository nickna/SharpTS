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
        var capabilityField = shell.Type.DefineField("capability", _types.Object, FieldAttributes.Public);
        var stablePrimitiveField = shell.Type.DefineField(
            "stablePrimitive", _types.Boolean, FieldAttributes.Public);

        return new EmittedStateMachine
        {
            Type = shell.Type,
            StateField = shell.StateField,
            BuilderField = shell.BuilderField,
            IterableField = shell.InputField,
            ConstructorField = shell.ConstructorField,
            CapabilityField = capabilityField,
            StablePrimitiveField = stablePrimitiveField,
            AwaiterField = awaiterField,
            MoveNextMethod = shell.MoveNextMethod,
            BuilderType = shell.BuilderType,
            AwaiterType = awaiterType
        };
    }

    /// <summary>
    /// Emits the PromiseAll wrapper method that creates and starts the state machine.
    /// </summary>
    private void EmitPromiseAllWrapper(
        ILGenerator il,
        EmittedStateMachine sm,
        EmittedRuntime runtime,
        bool stablePrimitive)
        => EmitCombinatorWrapper(il, sm.Type, sm.StateField, sm.IterableField, sm.BuilderField, sm.BuilderType,
            () =>
            {
                // Store the raw input. NormalizePromiseList runs inside
                // MoveNext's try block so an abrupt Promise.resolve call rejects.
                il.Emit(OpCodes.Ldarg_0);
            }, sm.ConstructorField, () => il.Emit(OpCodes.Ldarg_1),
            sm.CapabilityField, () => il.Emit(OpCodes.Ldarg_2),
            markNonAutoAwaitMethod: runtime.MarkNonAutoAwaitPromiseMethod,
            adoptResultMethod: runtime.AdoptPromiseCombinatorResultMethod,
            stablePrimitiveField: sm.StablePrimitiveField,
            emitStablePrimitiveValue: () => il.Emit(
                stablePrimitive ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));

    /// <summary>
    /// Emits the MoveNext body for PromiseAll state machine.
    /// Implements: convert list to tasks, await Task.WhenAll, return List.
    /// </summary>
    private void EmitPromiseAllMoveNext(EmittedStateMachine sm, EmittedRuntime runtime)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var listType = typeof(List<object?>);
        var taskListType = typeof(List<Task<object?>>);
        var taskArrayType = typeof(Task<object?>[]);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));
        var taskArrayLocal = il.DeclareLocal(taskArrayType);

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

        EmitNormalizeCombinatorIterable(il, runtime, sm.IterableField, sm.ConstructorField,
            sm.CapabilityField, combinatorKind: 3,
            stablePrimitiveField: sm.StablePrimitiveField);

        // NormalizePromiseList returns an exact task array for the intrinsic
        // Promise.all case. It either checked every element's observable own
        // `then` or received the compiler proof that those checks are inert.
        var genericListLabel = il.DefineLabel();
        var haveTaskArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.IterableField);
        il.Emit(OpCodes.Isinst, taskArrayType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, genericListLabel);
        il.Emit(OpCodes.Stloc, taskArrayLocal);
        il.Emit(OpCodes.Br, haveTaskArrayLabel);

        il.MarkLabel(genericListLabel);
        il.Emit(OpCodes.Pop);

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
        var emptyOrdinaryLabel = il.DefineLabel();
        var emptyDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StablePrimitiveField!);
        il.Emit(OpCodes.Brfalse, emptyOrdinaryLabel);
        il.Emit(OpCodes.Newobj, typeof(List<double>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Br, emptyDoneLabel);
        il.MarkLabel(emptyOrdinaryLabel);
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.MarkLabel(emptyDoneLabel);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        il.MarkLabel(notEmptyLabel);

        // Convert elements to tasks
        var tasksLocal = EmitCombinatorTaskListLoop(il, listLocal);

        // Call Task.WhenAll<object?>(tasks.ToArray())
        // Find the generic WhenAll<TResult>(Task<TResult>[]) and specialize it
        var whenAllMethod = EmitGenerics.MakeGenericMethod(typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "WhenAll" && m.IsGenericMethod &&
                   m.GetParameters().Length == 1 &&
                   m.GetParameters()[0].ParameterType.IsArray), typeof(object));
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("ToArray")!);
        il.Emit(OpCodes.Stloc, taskArrayLocal);

        il.MarkLabel(haveTaskArrayLabel);
        il.Emit(OpCodes.Ldloc, taskArrayLocal);
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
        il.Emit(OpCodes.Call, _types.GetMethod(sm.AwaiterType, "GetResult")!);

        // Convert object?[] to the ordinary boxed List<object?>, or to the
        // internal List<double> carrier selected only for a proven non-escaping
        // Promise<number>[] result.
        var arrayResultLocal = il.DeclareLocal(typeof(object?[]));
        il.Emit(OpCodes.Stloc, arrayResultLocal);
        var ordinaryResultLabel = il.DefineLabel();
        var resultDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StablePrimitiveField!);
        il.Emit(OpCodes.Brfalse, ordinaryResultLabel);

        var doubleListType = typeof(List<double>);
        var doubleListLocal = il.DeclareLocal(doubleListType);
        var resultIndexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldloc, arrayResultLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, doubleListType.GetConstructor([typeof(int)])!);
        il.Emit(OpCodes.Stloc, doubleListLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, resultIndexLocal);
        var resultLoopLabel = il.DefineLabel();
        var resultLoopDoneLabel = il.DefineLabel();
        il.MarkLabel(resultLoopLabel);
        il.Emit(OpCodes.Ldloc, resultIndexLocal);
        il.Emit(OpCodes.Ldloc, arrayResultLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, resultLoopDoneLabel);
        il.Emit(OpCodes.Ldloc, doubleListLocal);
        il.Emit(OpCodes.Ldloc, arrayResultLocal);
        il.Emit(OpCodes.Ldloc, resultIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ConvertToNumber);
        il.Emit(OpCodes.Callvirt, doubleListType.GetMethod("Add")!);
        il.Emit(OpCodes.Ldloc, resultIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, resultIndexLocal);
        il.Emit(OpCodes.Br, resultLoopLabel);
        il.MarkLabel(resultLoopDoneLabel);
        il.Emit(OpCodes.Ldloc, doubleListLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, resultDoneLabel);

        il.MarkLabel(ordinaryResultLabel);
        il.Emit(OpCodes.Ldloc, arrayResultLocal);
        il.Emit(OpCodes.Newobj, listType.GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.MarkLabel(resultDoneLabel);

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
