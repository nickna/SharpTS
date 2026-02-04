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
        var builderType = _types.AsyncTaskMethodBuilderOfObject;
        var awaiterType = _types.TaskAwaiterOfObjectArray;

        // Define state machine struct: $PromiseAll_SM
        var smType = moduleBuilder.DefineType(
            "$PromiseAll_SM",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.ValueType,
            [_types.IAsyncStateMachine]
        );

        // Define fields
        var stateField = smType.DefineField("<>1__state", _types.Int32, FieldAttributes.Public);
        var builderField = smType.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var iterableField = smType.DefineField("iterable", _types.Object, FieldAttributes.Public);
        var awaiterField = smType.DefineField("<>u__1", awaiterType, FieldAttributes.Private);

        // Define MoveNext method
        var moveNext = smType.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            Type.EmptyTypes
        );
        smType.DefineMethodOverride(moveNext, _types.GetMethodNoParams(_types.IAsyncStateMachine, "MoveNext"));

        // Define SetStateMachine method (empty body for value types)
        var setStateMachine = smType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.IAsyncStateMachine]
        );
        smType.DefineMethodOverride(setStateMachine, _types.GetMethod(_types.IAsyncStateMachine, "SetStateMachine", [_types.IAsyncStateMachine]));
        var setSmIL = setStateMachine.GetILGenerator();
        setSmIL.Emit(OpCodes.Ret);

        return new EmittedStateMachine
        {
            Type = smType,
            StateField = stateField,
            BuilderField = builderField,
            IterableField = iterableField,
            AwaiterField = awaiterField,
            MoveNextMethod = moveNext,
            BuilderType = builderType,
            AwaiterType = awaiterType
        };
    }

    /// <summary>
    /// Emits the PromiseAll wrapper method that creates and starts the state machine.
    /// </summary>
    private void EmitPromiseAllWrapper(ILGenerator il, EmittedStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);

        // Initialize state machine: var sm = default($PromiseAll_SM);
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
    /// Emits the MoveNext body for PromiseAll state machine.
    /// Implements: convert list to tasks, await Task.WhenAll, return List.
    /// </summary>
    private void EmitPromiseAllMoveNext(EmittedStateMachine sm)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var listType = typeof(List<object?>);
        var taskListType = typeof(List<Task<object?>>);
        var taskArrayType = typeof(Task<object?>[]);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        var setResultLabel = il.DefineLabel();  // New: for success path
        var returnLabel = il.DefineLabel();

        // Begin outer try block
        il.BeginExceptionBlock();

        // State dispatch: if (this.<>1__state == 0) goto state0Label
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, state0Label);

        // ========== STATE -1: Initial execution ==========

        // Cast iterable to List<object?>
        var listLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.IterableField);
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Stloc, listLocal);

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

        // Create tasks list
        var tasksLocal = il.DeclareLocal(taskListType);
        il.Emit(OpCodes.Newobj, taskListType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tasksLocal);

        // Loop through input list and convert to tasks
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

        // if (index >= count) goto loopEnd
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // element = list[index]
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        // Check if element is Task<object?>
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

        // goto loopStart
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

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

        // GetAwaiter and store to field
        var awaiterLocal = il.DeclareLocal(sm.AwaiterType);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectArrayGetAwaiter);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, awaiterLocal);
        il.Emit(OpCodes.Stfld, sm.AwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, sm.AwaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continueLabel);

        // Not completed - suspend
        // this.<>1__state = 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(sm.AwaiterType, sm.Type);
        il.Emit(OpCodes.Call, awaitMethod);

        // return (exit MoveNext)
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 0: Resume after await ==========
        il.MarkLabel(state0Label);

        // Reset state to -1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

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

        // Set state to -2 (completed with error)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // builder.SetException(exception)
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

