using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Builds a state machine class for an async generator function.
/// The state machine implements IAsyncEnumerable&lt;object&gt;, IAsyncEnumerator&lt;object&gt;, and IAsyncDisposable.
/// Contains:
/// - State field for tracking execution position
/// - Current field for the yielded value
/// - Awaiter field for async await points
/// - Hoisted parameter and local variable fields
/// </summary>
public class AsyncGeneratorStateMachineBuilder
{
    private readonly ModuleBuilder _moduleBuilder;
    private readonly TypeProvider _types;
    private TypeBuilder _stateMachineType = null!;
    private int _counter;
    private HoistingManager _hoisting = null!;

    // The type being built
    public TypeBuilder StateMachineType => _stateMachineType;

    // Core state machine fields
    public FieldBuilder StateField { get; private set; } = null!;
    public FieldBuilder CurrentField { get; private set; } = null!;

    // Async infrastructure fields
    public FieldBuilder AwaiterField { get; private set; } = null!;
    public FieldBuilder AwaitedTaskField { get; private set; } = null!;      // Task<object> being awaited (for continuation)
    public FieldBuilder ValueTaskSourceField { get; private set; } = null!;  // ManualResetValueTaskSourceCore<bool>
    public FieldBuilder PendingValueField { get; private set; } = null!;     // For storing value before completing

    // Hoisted variables (become class fields) - delegated to HoistingManager
    public Dictionary<string, FieldBuilder> HoistedParameters => _hoisting.HoistedParameters;
    public Dictionary<string, FieldBuilder> HoistedLocals => _hoisting.HoistedLocals;

    // 'this' field for instance async generator methods
    public FieldBuilder? ThisField { get; private set; }

    // Delegated async enumerator field for yield* expressions
    public FieldBuilder? DelegatedAsyncEnumeratorField { get; private set; }

    // Constructor
    public ConstructorBuilder Constructor { get; private set; } = null!;

    // Methods
    public MethodBuilder MoveNextAsyncMethod { get; private set; } = null!;
    public MethodBuilder CurrentGetMethod { get; private set; } = null!;
    public MethodBuilder DisposeAsyncMethod { get; private set; } = null!;
    public MethodBuilder GetAsyncEnumeratorMethod { get; private set; } = null!;

    // $IAsyncGenerator methods for return/throw support
    public MethodBuilder NextMethod { get; private set; } = null!;
    public MethodBuilder ReturnMethod { get; private set; } = null!;
    public MethodBuilder ThrowMethod { get; private set; } = null!;

    // Runtime reference for $IAsyncGenerator interface
    private EmittedRuntime? _runtime;

    public AsyncGeneratorStateMachineBuilder(ModuleBuilder moduleBuilder, TypeProvider types, int counter = 0)
    {
        _moduleBuilder = moduleBuilder;
        _types = types;
        _counter = counter;
    }

    /// <summary>
    /// Defines the complete state machine class type with all fields and method stubs.
    /// </summary>
    public void DefineStateMachine(
        string methodName,
        AsyncGeneratorStateAnalyzer.AsyncGeneratorFunctionAnalysis analysis,
        bool isInstanceMethod = false,
        EmittedRuntime? runtime = null)
    {
        _runtime = runtime;

        // Build list of interfaces to implement
        var interfaces = new List<Type>
        {
            _types.IAsyncEnumerableOfObject,
            _types.IAsyncEnumeratorOfObject,
            _types.IAsyncDisposable
        };

        // Add $IAsyncGenerator interface if runtime is available
        if (runtime?.AsyncGeneratorInterfaceType != null)
        {
            interfaces.Add(runtime.AsyncGeneratorInterfaceType);
        }

        // Define the state machine class (using class for reference semantics)
        // Name follows C# compiler convention: <MethodName>d__N
        _stateMachineType = _moduleBuilder.DefineType(
            $"<{methodName}>d__{_counter}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            interfaces.ToArray()
        );

        // Define core fields
        DefineStateField();
        DefineCurrentField();
        DefineAsyncInfrastructureFields();

        // Define hoisted variables using HoistingManager
        _hoisting = new HoistingManager(_stateMachineType, _types.Object);
        _hoisting.DefineHoistedParameters(analysis.HoistedParameters);
        _hoisting.DefineHoistedLocals(analysis.HoistedLocals);

        // Define hoisted enumerators for for...of loops containing suspensions (yield/await)
        _hoisting.DefineHoistedEnumerators(analysis.ForOfLoopsWithSuspension, _types.IEnumerator);

        // Define 'this' field for instance methods that use 'this'
        if (isInstanceMethod && analysis.UsesThis)
        {
            ThisField = _stateMachineType.DefineField(
                "<>4__this",
                _types.Object,
                FieldAttributes.Public
            );
        }

        // Define delegated enumerator field for yield* expressions (typed as object to hold either sync or async enumerators)
        if (analysis.HasYieldStar)
        {
            DelegatedAsyncEnumeratorField = _stateMachineType.DefineField(
                "<>7__wrap1",
                _types.Object,
                FieldAttributes.Private
            );
        }

        // Define constructor
        DefineConstructor();

        // Define the IAsyncEnumerator methods
        DefineMoveNextAsyncMethod();
        DefineCurrentProperty();
        DefineDisposeAsyncMethod();

        // Define IAsyncEnumerable method
        DefineGetAsyncEnumeratorMethod();

        // Define $IAsyncGenerator methods if runtime is available
        if (_runtime?.AsyncGeneratorInterfaceType != null)
        {
            DefineAsyncGeneratorMethods();
        }
    }

    private void DefineStateField()
    {
        // <>1__state - tracks execution position
        // -1 = running, -2 = completed, 0+ = suspended at specific point
        StateField = _stateMachineType.DefineField(
            "<>1__state",
            _types.Int32,
            FieldAttributes.Public
        );
    }

    private void DefineCurrentField()
    {
        // <>2__current - the current yielded value
        CurrentField = _stateMachineType.DefineField(
            "<>2__current",
            _types.Object,
            FieldAttributes.Private
        );
    }

    private void DefineAsyncInfrastructureFields()
    {
        // Awaiter field for async operations
        AwaiterField = _stateMachineType.DefineField(
            "<>u__1",
            _types.TaskAwaiterOfObject,
            FieldAttributes.Private
        );

        // Task field to store the awaited task (needed for continuation in EmitAwaitSuspensionReturn)
        AwaitedTaskField = _stateMachineType.DefineField(
            "<>__awaitedTask",
            _types.TaskOfObject,
            FieldAttributes.Private
        );

        // For simple implementation, we'll track whether we have a value pending
        PendingValueField = _stateMachineType.DefineField(
            "<>__hasPendingValue",
            _types.Boolean,
            FieldAttributes.Private
        );
    }

    private void DefineConstructor()
    {
        // Define default constructor
        Constructor = _stateMachineType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = Constructor.GetILGenerator();
        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // Initialize state to -1 (not started/running)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, StateField);
        il.Emit(OpCodes.Ret);
    }

    private void DefineMoveNextAsyncMethod()
    {
        // ValueTask<bool> MoveNextAsync()
        MoveNextAsyncMethod = _stateMachineType.DefineMethod(
            "MoveNextAsync",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.ValueTaskOfBool,
            Type.EmptyTypes
        );

        // Mark as implementing IAsyncEnumerator<object>.MoveNextAsync
        var interfaceMethod = _types.GetMethodNoParams(_types.IAsyncEnumeratorOfObject, "MoveNextAsync");
        _stateMachineType.DefineMethodOverride(MoveNextAsyncMethod, interfaceMethod);
    }

    private void DefineCurrentProperty()
    {
        // object IAsyncEnumerator<object>.Current { get; }
        var currentProp = _stateMachineType.DefineProperty(
            "Current",
            PropertyAttributes.None,
            _types.Object,
            Type.EmptyTypes
        );

        CurrentGetMethod = _stateMachineType.DefineMethod(
            "get_Current",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot,
            _types.Object,
            Type.EmptyTypes
        );

        var il = CurrentGetMethod.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, CurrentField);
        il.Emit(OpCodes.Ret);

        currentProp.SetGetMethod(CurrentGetMethod);

        // Mark as implementing IAsyncEnumerator<object>.Current
        var interfaceMethod = _types.GetPropertyGetter(_types.IAsyncEnumeratorOfObject, "Current");
        _stateMachineType.DefineMethodOverride(CurrentGetMethod, interfaceMethod);
    }

    private void DefineDisposeAsyncMethod()
    {
        // ValueTask DisposeAsync()
        DisposeAsyncMethod = _stateMachineType.DefineMethod(
            "DisposeAsync",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.ValueTask,
            Type.EmptyTypes
        );

        var il = DisposeAsyncMethod.GetILGenerator();

        // Set state to -2 (completed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, StateField);

        // Return default ValueTask (completed)
        var vtLocal = il.DeclareLocal(_types.ValueTask);
        il.Emit(OpCodes.Ldloca, vtLocal);
        il.Emit(OpCodes.Initobj, _types.ValueTask);
        il.Emit(OpCodes.Ldloc, vtLocal);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.GetMethodNoParams(_types.IAsyncDisposable, "DisposeAsync");
        _stateMachineType.DefineMethodOverride(DisposeAsyncMethod, interfaceMethod);
    }

    private void DefineGetAsyncEnumeratorMethod()
    {
        // IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        GetAsyncEnumeratorMethod = _stateMachineType.DefineMethod(
            "GetAsyncEnumerator",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.IAsyncEnumeratorOfObject,
            [_types.CancellationToken]
        );

        // Return 'this' since the async generator IS the enumerator
        var il = GetAsyncEnumeratorMethod.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.GetMethod(_types.IAsyncEnumerableOfObject, "GetAsyncEnumerator", _types.CancellationToken);
        _stateMachineType.DefineMethodOverride(GetAsyncEnumeratorMethod, interfaceMethod);
    }

    /// <summary>
    /// Defines the $IAsyncGenerator interface methods: next, return, throw (async versions).
    /// </summary>
    private void DefineAsyncGeneratorMethods()
    {
        // next() method - returns Task<object> with { value, done }
        NextMethod = _stateMachineType.DefineMethod(
            "next",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            Type.EmptyTypes
        );

        EmitNextMethodBody();
        _stateMachineType.DefineMethodOverride(NextMethod, _runtime!.AsyncGeneratorNextMethod);

        // return(value) method - returns Task<object> with { value, done: true }
        ReturnMethod = _stateMachineType.DefineMethod(
            "return",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]
        );

        EmitReturnMethodBody();
        _stateMachineType.DefineMethodOverride(ReturnMethod, _runtime!.AsyncGeneratorReturnMethod);

        // throw(error) method - returns Task<object>
        ThrowMethod = _stateMachineType.DefineMethod(
            "throw",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]
        );

        EmitThrowMethodBody();
        _stateMachineType.DefineMethodOverride(ThrowMethod, _runtime!.AsyncGeneratorThrowMethod);
    }

    private void EmitNextMethodBody()
    {
        var il = NextMethod.GetILGenerator();
        var doneLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Call MoveNextAsync() and await it
        // var moveNextTask = this.MoveNextAsync();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, MoveNextAsyncMethod);

        // Convert ValueTask<bool> to Task<bool> via AsTask()
        var vtLocal = il.DeclareLocal(_types.ValueTaskOfBool);
        il.Emit(OpCodes.Stloc, vtLocal);
        il.Emit(OpCodes.Ldloca, vtLocal);
        var asTaskMethod = _types.GetMethodNoParams(_types.ValueTaskOfBool, "AsTask");
        il.Emit(OpCodes.Call, asTaskMethod);

        // Now we have Task<bool> on stack
        // We need to create a continuation that builds the result dictionary
        // For simplicity, we'll use ContinueWith pattern

        // Store Task<bool>
        var taskBoolLocal = il.DeclareLocal(_types.MakeGenericType(_types.TaskOpen, _types.Boolean));
        il.Emit(OpCodes.Stloc, taskBoolLocal);

        // For a simpler initial implementation, we'll just get the result synchronously
        // (This works for already-completed tasks which is the common case for generators)
        // A full implementation would use async continuation

        // Get the result: task.GetAwaiter().GetResult()
        il.Emit(OpCodes.Ldloc, taskBoolLocal);
        var getAwaiterMethod = _types.GetMethodNoParams(_types.MakeGenericType(_types.TaskOpen, _types.Boolean), "GetAwaiter");
        il.Emit(OpCodes.Call, getAwaiterMethod);
        var awaiterType = _types.MakeGenericType(_types.TaskAwaiterOpen, _types.Boolean);
        var awaiterLocal = il.DeclareLocal(awaiterType);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldloca, awaiterLocal);
        var getResultMethod = _types.GetMethodNoParams(awaiterType, "GetResult");
        il.Emit(OpCodes.Call, getResultMethod);

        // Stack now has bool (true = has value, false = done)
        il.Emit(OpCodes.Brfalse, doneLabel);

        // Not done: create { value: Current, done: false }
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, CurrentField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Br, endLabel);

        // Done: create { value: CurrentField (return value), done: true }
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, CurrentField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));

        il.MarkLabel(endLabel);
        // Wrap in Task.FromResult
        var fromResultMethod = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);
        il.Emit(OpCodes.Call, fromResultMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReturnMethodBody()
    {
        var il = ReturnMethod.GetILGenerator();

        // Set state to -2 (completed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, StateField);

        // Create { value: arg, done: true }
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));

        // Wrap in Task.FromResult
        var fromResultMethod = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);
        il.Emit(OpCodes.Call, fromResultMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitThrowMethodBody()
    {
        var il = ThrowMethod.GetILGenerator();

        // Set state to -2 (completed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, StateField);

        // Create a faulted task with the exception
        var isExceptionLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Exception);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, isExceptionLabel);

        // Not an exception - wrap it using CreateException
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime!.CreateException);

        il.MarkLabel(isExceptionLabel);
        // Stack now has Exception

        // Use Task.FromException<object>(exception)
        var fromExceptionMethod = typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!.MakeGenericMethod(_types.Object);
        il.Emit(OpCodes.Call, fromExceptionMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Gets a field for a variable by name, checking both parameters and locals.
    /// </summary>
    public FieldBuilder? GetVariableField(string name) => _hoisting.GetVariableField(name);

    /// <summary>
    /// Checks if a variable is hoisted to the state machine.
    /// </summary>
    public bool IsHoisted(string name) => _hoisting.IsHoisted(name);

    /// <summary>
    /// Gets the hoisted enumerator field for a for...of loop containing suspension points.
    /// </summary>
    public FieldBuilder? GetEnumeratorField(Parsing.Stmt.ForOf loop) => _hoisting.GetEnumeratorField(loop);

    /// <summary>
    /// Finalizes the type after MoveNextAsync body has been emitted.
    /// </summary>
    public Type CreateType()
    {
        return _stateMachineType.CreateType()!;
    }
}
