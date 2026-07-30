using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// The shared shell of a promise-combinator async state machine: the struct
/// type, the state/builder/input fields, and the MoveNext/SetStateMachine
/// method shells. Combinators add their own awaiter (and other) fields on
/// <see cref="Type"/> before constructing their per-combinator descriptor.
/// </summary>
internal sealed class CombinatorStateMachineShell
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }
    public required FieldBuilder BuilderField { get; init; }
    public required FieldBuilder InputField { get; init; }
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
}

public partial class RuntimeEmitter
{
    /// <summary>
    /// Shared IL scaffolding for the Promise combinator state machines
    /// (All / AllSettled / Any / Race + the ProcessElementSettled helper).
    /// Each combinator's MoveNext keeps its distinctive control flow inline
    /// (WhenAll vs two-stage WhenAny vs ContinueWith aggregation, and the
    /// spec-mandated empty-iterable differences); only the genuinely
    /// structural pieces live here.
    /// </summary>
    /// <remarks>
    /// Defines the state-machine struct shell: a sealed value type
    /// implementing IAsyncStateMachine with the <c>&lt;&gt;1__state</c> /
    /// <c>&lt;&gt;t__builder</c> / input fields and the MoveNext +
    /// SetStateMachine method shells (SetStateMachine body is an empty
    /// <c>ret</c> — value-type machines don't box). MoveNext's body is
    /// emitted later by the combinator.
    /// </remarks>
    private CombinatorStateMachineShell DefineCombinatorStateMachineShell(
        ModuleBuilder moduleBuilder, string typeName, string inputFieldName,
        MethodAttributes methodVisibility)
    {
        var builderType = _types.AsyncTaskMethodBuilderOfObject;

        var smType = moduleBuilder.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.ValueType,
            [_types.IAsyncStateMachine]
        );

        var stateField = smType.DefineField("<>1__state", _types.Int32, FieldAttributes.Public);
        var builderField = smType.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var inputField = smType.DefineField(inputFieldName, _types.Object, FieldAttributes.Public);

        var moveNext = smType.DefineMethod(
            "MoveNext",
            methodVisibility | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            Type.EmptyTypes
        );
        smType.DefineMethodOverride(moveNext, _types.AsyncStateMachineMoveNext);

        var setStateMachine = smType.DefineMethod(
            "SetStateMachine",
            methodVisibility | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.IAsyncStateMachine]
        );
        smType.DefineMethodOverride(setStateMachine, _types.AsyncStateMachineSetStateMachine);
        setStateMachine.GetILGenerator().Emit(OpCodes.Ret);

        return new CombinatorStateMachineShell
        {
            Type = smType,
            StateField = stateField,
            BuilderField = builderField,
            InputField = inputField,
            MoveNextMethod = moveNext,
            BuilderType = builderType
        };
    }

    /// <summary>
    /// Emits a combinator wrapper body: initialize the state-machine struct,
    /// set state to -1, store the input value (pushed by
    /// <paramref name="emitInputValue"/>), create + start the builder, and
    /// return <c>builder.Task</c>.
    /// </summary>
    private void EmitCombinatorWrapper(ILGenerator il, TypeBuilder smType, FieldBuilder stateField,
        FieldBuilder inputField, FieldBuilder builderField, Type builderType, System.Action emitInputValue)
    {
        var smLocal = il.DeclareLocal(smType);

        // var sm = default(SM);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, smType);

        // sm.<>1__state = -1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, stateField);

        // sm.<input> = <emitInputValue()>;
        il.Emit(OpCodes.Ldloca, smLocal);
        emitInputValue();
        il.Emit(OpCodes.Stfld, inputField);

        // sm.<>t__builder = AsyncTaskMethodBuilder<object>.Create();
        il.Emit(OpCodes.Ldloca, smLocal);
        var createMethod = builderType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        il.Emit(OpCodes.Call, createMethod);
        il.Emit(OpCodes.Stfld, builderField);

        // sm.<>t__builder.Start(ref sm);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, builderField);
        il.Emit(OpCodes.Ldloca, smLocal);
        var startMethod = EmitGenerics.MakeGenericMethod(builderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Start" && m.IsGenericMethod), smType);
        il.Emit(OpCodes.Call, startMethod);

        // return sm.<>t__builder.Task;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, builderField);
        var taskGetter = builderType.GetProperty("Task", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
        il.Emit(OpCodes.Call, taskGetter);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the iterable brand check + cast: if the (normalized) iterable
    /// field is not a <c>List&lt;object?&gt;</c>, throw a guest TypeError
    /// (ECMA-262 §27.2.4 — the outer catch turns it into a rejection);
    /// otherwise cast and store it. Returns the list local.
    /// </summary>
    private LocalBuilder EmitCombinatorIterableGuard(ILGenerator il, EmittedRuntime runtime,
        FieldBuilder iterableField, string combinatorName)
    {
        var listType = typeof(List<object?>);

        var iterableOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iterableField);
        il.Emit(OpCodes.Isinst, listType);
        il.Emit(OpCodes.Brtrue, iterableOkLabel);
        il.Emit(OpCodes.Ldstr, $"Promise.{combinatorName} argument is not iterable");
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
        il.MarkLabel(iterableOkLabel);

        var listLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iterableField);
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Stloc, listLocal);
        return listLocal;
    }

    /// <summary>
    /// Emits the element→task conversion loop shared by All and Race: builds
    /// a <c>List&lt;Task&lt;object?&gt;&gt;</c> where each element is added
    /// as-is when it already is a <c>Task&lt;object?&gt;</c> and wrapped via
    /// <c>Task.FromResult</c> otherwise. Returns the tasks local.
    /// </summary>
    private LocalBuilder EmitCombinatorTaskListLoop(ILGenerator il, LocalBuilder listLocal)
    {
        var listType = typeof(List<object?>);
        var taskListType = typeof(List<Task<object?>>);

        var tasksLocal = il.DeclareLocal(taskListType);
        il.Emit(OpCodes.Newobj, taskListType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tasksLocal);

        var indexLocal = il.DeclareLocal(typeof(int));
        var countLocal = il.DeclareLocal(typeof(int));
        var elementLocal = il.DeclareLocal(typeof(object));

        // count = list.Count
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // index = 0
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
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, typeof(object)));
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);
        il.Emit(OpCodes.Br, afterAddLabel);

        // Is a task - add directly
        il.MarkLabel(isTaskLabel);
        var taskTemp = il.DeclareLocal(typeof(Task<object?>));
        il.Emit(OpCodes.Stloc, taskTemp);
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Ldloc, taskTemp);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);

        il.MarkLabel(afterAddLabel);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        return tasksLocal;
    }

    /// <summary>
    /// Emits the awaiter suspend dance. Expects the awaiter VALUE on the
    /// stack (the combinator emits its own <c>GetAwaiter</c> call): spill it
    /// to <paramref name="awaiterField"/>, branch to
    /// <paramref name="continueLabel"/> when already completed, otherwise set
    /// state to <paramref name="suspendState"/>, call
    /// <c>builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)</c>, and
    /// <c>leave</c> to <paramref name="returnLabel"/>.
    /// </summary>
    private void EmitCombinatorAwaitSuspend(ILGenerator il, TypeBuilder smType, FieldBuilder stateField,
        FieldBuilder builderField, Type builderType, FieldBuilder awaiterField, Type awaiterType,
        int suspendState, Label continueLabel, Label returnLabel)
    {
        var awaiterLocal = il.DeclareLocal(awaiterType);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, awaiterLocal);
        il.Emit(OpCodes.Stfld, awaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, awaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continueLabel);

        // Not completed - suspend at the given state
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, suspendState);
        il.Emit(OpCodes.Stfld, stateField);

        // builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, builderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, awaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod = EmitGenerics.MakeGenericMethod(builderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod), awaiterType, smType);
        il.Emit(OpCodes.Call, awaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);
    }

    /// <summary>
    /// Marks the resume label for a suspend state and resets
    /// <c>&lt;&gt;1__state</c> to -1.
    /// </summary>
    private void EmitCombinatorResumeState(ILGenerator il, Label stateLabel, FieldBuilder stateField)
    {
        il.MarkLabel(stateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, stateField);
    }

    /// <summary>
    /// Emits the success epilogue inside the try block: state = -2,
    /// <c>builder.SetResult(result)</c>, <c>leave returnLabel</c>.
    /// </summary>
    private void EmitCombinatorSetResult(ILGenerator il, FieldBuilder stateField, FieldBuilder builderField,
        Type builderType, LocalBuilder resultLocal, Label returnLabel)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, stateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, builderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, builderType.GetMethod("SetResult")!);
        il.Emit(OpCodes.Leave, returnLabel);
    }

    /// <summary>
    /// Emits the catch handler that converts any exception into a rejection:
    /// <c>catch (Exception e) { state = -2; builder.SetException(e); }</c>.
    /// The caller still emits <c>EndExceptionBlock</c>.
    /// </summary>
    private void EmitCombinatorCatchSetException(ILGenerator il, FieldBuilder stateField, FieldBuilder builderField,
        Type builderType, LocalBuilder exceptionLocal, Label returnLabel)
    {
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, exceptionLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, stateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, builderField);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, builderType.GetMethod("SetException")!);
        il.Emit(OpCodes.Leave, returnLabel);
    }
}
