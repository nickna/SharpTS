using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

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
public class AsyncGeneratorStateMachineBuilder : StateMachineBuilderBase, IIteratorStateMachineBuilder
{
    private readonly ModuleBuilder _moduleBuilder;
    private readonly TypeProvider _types;
    private TypeBuilder _stateMachineType = null!;
    private int _counter;
    private HoistingManager _hoisting = null!;

    // The type being built
    public override TypeBuilder StateMachineType => _stateMachineType;

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

    // Function-level display class field for captured-and-mutated locals (#725). A sync arrow inside
    // the async generator body that WRITES a variable captured from the generator scope shares storage
    // with the generator through this reference-typed display class — the same mechanism the sync
    // generator state machine uses (#674). Null when the async generator has no such write-capture.
    public FieldBuilder? FunctionDCField { get; private set; }

    // Delegated async enumerator field for yield* expressions
    public FieldBuilder? DelegatedAsyncEnumeratorField { get; private set; }

    // Flag set by return() to trigger finally blocks during MoveNextAsync resume
    public FieldBuilder ReturnRequestedField { get; private set; } = null!;

    // Re-entrancy flag: true only while the generator body is synchronously advancing (the window of a
    // MoveNextAsync call before it suspends or completes). A guest next()/return()/throw() observing it
    // means the body is advancing itself; without the guard that synchronous re-entry recurses into
    // MoveNextAsync until the stack overflows (#542). Set/cleared by <DriveOnce> and return()'s drive.
    public FieldBuilder ExecutingField { get; private set; } = null!;

    // The value passed to next(v) — delivered as the result of the yield expression that suspended the
    // generator (ECMA-262 §27.6.3.6, #473). Stored by next() before driving; read in MoveNextAsync on the
    // yield resume path. Seeded to $Undefined in the ctor so bare next() / for-await-of yields undefined.
    public FieldBuilder SentField { get; private set; } = null!;

    // Tail of the request chain (ECMA-262 §27.6.3 AsyncGeneratorQueue, modeled as a Task chain): the
    // Task<object> of the most recently enqueued next(). A new next() awaits this before driving, so
    // overlapping next() calls run in FIFO order instead of re-entering MoveNextAsync concurrently and
    // corrupting state. Lets next() be truly asynchronous — it never blocks the event-loop thread on a
    // pending await (#631) — and serializes concurrent next() requests (#542). Null until the first next().
    public FieldBuilder PendingTailField { get; private set; } = null!;

    // <DriveOnce>(): drives one MoveNextAsync step (under the executing guard) and returns its
    // { value, done } Task<object>; <DriveContinuation>(antecedent, state): static trampoline that
    // calls <DriveOnce> on a queued next() once the prior request settles.
    private MethodBuilder _driveOnceMethod = null!;
    private MethodBuilder _driveContinuationMethod = null!;

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
    /// <param name="hasDynamicThis">True for a free-function async-generator stub that binds its own
    /// dynamic receiver (#775): it captures the active thread-local <c>this</c> into <c>&lt;&gt;4__this</c>
    /// at creation time even though it is a static stub.</param>
    public void DefineStateMachine(
        string methodName,
        AsyncGeneratorStateAnalyzer.AsyncGeneratorFunctionAnalysis analysis,
        bool isInstanceMethod = false,
        EmittedRuntime? runtime = null,
        bool hasDynamicThis = false)
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

        // Define 'this' field for instance methods that use 'this', and for a free-function async
        // generator stub that binds its own dynamic receiver (#775 — a `HasOwnThis` async generator
        // expression / object method, captured from the thread-local receiver at creation time).
        if ((isInstanceMethod || hasDynamicThis) && analysis.UsesThis)
        {
            ThisField = _stateMachineType.DefineField(
                "<>4__this",
                _types.Object,
                FieldAttributes.Public
            );
        }

        // Define __returnRequested flag field for generator.return() to trigger finally blocks
        ReturnRequestedField = _stateMachineType.DefineField(
            "__returnRequested",
            _types.Boolean,
            FieldAttributes.Public
        );

        // Define the re-entrancy guard flag (#542); see EmitThrowIfExecutingAsync.
        ExecutingField = _stateMachineType.DefineField(
            "<>5__executing",
            _types.Boolean,
            FieldAttributes.Private
        );

        // Tail of the request chain for truly-async, serialized next() (#631/#542).
        PendingTailField = _stateMachineType.DefineField(
            "<>5__pendingTail",
            _types.Task,
            FieldAttributes.Private
        );

        // <>3__sent — value delivered to the resumed yield expression (#473); seeded to undefined in ctor.
        SentField = _stateMachineType.DefineField(
            "<>3__sent",
            _types.Object,
            FieldAttributes.Private
        );

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
        // Seed <>3__sent to $Undefined so bare next() / for-await-of delivers undefined to the resumed
        // yield expression, not null (#473, mirrors sync-gen ctor — GeneratorStateMachineBuilder).
        if (SentField != null && _runtime?.UndefinedInstance != null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldsfld, _runtime.UndefinedInstance);
            il.Emit(OpCodes.Stfld, SentField);
        }
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
        // Internal drive helpers, defined before next() so its body can reference them.
        // <DriveOnce>() : Task<object> — drive one step under the executing guard.
        _driveOnceMethod = _stateMachineType.DefineMethod(
            "<DriveOnce>",
            MethodAttributes.Private | MethodAttributes.HideBySig,
            _types.TaskOfObject,
            Type.EmptyTypes
        );
        // static <DriveContinuation>(Task antecedent, object state) : Task<object> — queue trampoline.
        _driveContinuationMethod = _stateMachineType.DefineMethod(
            "<DriveContinuation>",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            _types.TaskOfObject,
            [_types.Task, _types.Object]
        );

        // next(object sentValue) method - returns Task<object> with { value, done }
        // sentValue is delivered as the result of the suspended yield expression (#473).
        NextMethod = _stateMachineType.DefineMethod(
            "next",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]
        );

        EmitNextMethodBody();
        EmitDriveOnceBody();
        EmitDriveContinuationBody();
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

        // Reject only a *synchronously* re-entrant next() — the body calling next() on itself before it
        // suspends, which would recurse into MoveNextAsync and overflow the stack (#542). A next() issued
        // while the body is suspended is NOT rejected: it queues on the request chain below (and, for the
        // self-reentrant case, that queued request can never run before the suspended body it sits behind —
        // a deadlock, matching Node, rather than the old stack overflow).
        EmitThrowIfExecutingAsync(il);

        // Stash the sent value so the resumed yield expression reads the right value (#473).
        // Stored before either the fast or slow drive path runs MoveNextAsync.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, SentField);

        var prevLocal = il.DeclareLocal(_types.Task);
        var resultLocal = il.DeclareLocal(_types.TaskOfObject);
        var fastPath = il.DefineLabel();
        var setTail = il.DefineLabel();

        // prev = this._pendingTail
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, PendingTailField);
        il.Emit(OpCodes.Stloc, prevLocal);

        // if (prev == null || prev.IsCompleted) drive immediately; else queue behind it.
        il.Emit(OpCodes.Ldloc, prevLocal);
        il.Emit(OpCodes.Brfalse, fastPath);
        il.Emit(OpCodes.Ldloc, prevLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Task, "IsCompleted"));
        il.Emit(OpCodes.Brtrue, fastPath);

        // Slow path — a prior request is still in flight:
        //   result = prev.ContinueWith<Task<object>>(<DriveContinuation>, this, ExecuteSynchronously).Unwrap();
        // ExecuteSynchronously runs the continuation inline on whichever (event-loop) thread settles prev,
        // so the body is never advanced on a thread-pool thread. The continuation fires regardless of how
        // prev settled (a faulted prior next() does not stall the queue).
        il.Emit(OpCodes.Ldloc, prevLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, _driveContinuationMethod);
        var funcType = _types.MakeGenericType(typeof(Func<,,>), _types.Task, _types.Object, _types.TaskOfObject);
        il.Emit(OpCodes.Newobj, funcType.GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Ldarg_0);  // state = this
        il.Emit(OpCodes.Ldc_I4, (int)System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
        il.Emit(OpCodes.Callvirt, ResolveContinueWithFuncState());
        il.Emit(OpCodes.Call, ResolveTaskUnwrap());
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, setTail);

        // Fast path — no in-flight request: drive one step now.
        il.MarkLabel(fastPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _driveOnceMethod);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(setTail);
        // this._pendingTail = result; return result;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Stfld, PendingTailField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>&lt;DriveOnce&gt;()</c>: sets the executing guard, calls <c>MoveNextAsync()</c> (which runs
    /// the body's synchronous segment up to the next suspension), clears the guard, then hands the resulting
    /// ValueTask to <c>AsyncGeneratorBuildResult</c> to produce the <c>{ value, done }</c> Task&lt;object&gt;.
    /// The executing flag is therefore set only for the synchronous-advance window — once the body suspends
    /// it is cleared, so a concurrent next() observes "not executing" and queues rather than rejecting.
    /// An uncaught synchronous throw from the body becomes a faulted Task (a rejected next() promise, #566).
    /// </summary>
    private void EmitDriveOnceBody()
    {
        var il = _driveOnceMethod.GetILGenerator();
        var vtLocal = il.DeclareLocal(_types.ValueTaskOfBool);
        var resultLocal = il.DeclareLocal(_types.TaskOfObject);
        var exLocal = il.DeclareLocal(_types.Exception);
        var doneLabel = il.DefineLabel();

        il.BeginExceptionBlock();
        // executing = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, ExecutingField);
        // vt = this.MoveNextAsync()  — runs the synchronous segment; may throw synchronously
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, MoveNextAsyncMethod);
        il.Emit(OpCodes.Stloc, vtLocal);
        // executing = false  (normal path: body suspended or completed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, ExecutingField);
        // result = AsyncGeneratorBuildResult(vt, (IAsyncEnumerator<object>)this)
        il.Emit(OpCodes.Ldloc, vtLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.IAsyncEnumeratorOfObject);
        il.Emit(OpCodes.Call, _runtime!.AsyncGeneratorBuildResult);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, doneLabel);

        // catch (Exception e) { executing = false; result = Task.FromException<object>(e); }
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, ExecutingField);
        il.Emit(OpCodes.Ldloc, exLocal);
        var fromException = EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!, _types.Object);
        il.Emit(OpCodes.Call, fromException);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, doneLabel);
        il.EndExceptionBlock();

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the static trampoline <c>&lt;DriveContinuation&gt;(Task antecedent, object state)</c> used as the
    /// ContinueWith body for a queued next(): once the prior request settles, it drives the next step by
    /// calling the private instance <c>&lt;DriveOnce&gt;</c> on the generator passed via <paramref name="state"/>.
    /// </summary>
    private void EmitDriveContinuationBody()
    {
        var il = _driveContinuationMethod.GetILGenerator();
        // return ((<thisSM>)state).<DriveOnce>();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _stateMachineType);
        il.Emit(OpCodes.Call, _driveOnceMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Resolves <c>Task.ContinueWith&lt;Task&lt;object&gt;&gt;(Func&lt;Task,object,Task&lt;object&gt;&gt;, object, TaskContinuationOptions)</c>.</summary>
    private MethodInfo ResolveContinueWithFuncState() => EmitGenerics.MakeGenericMethod(
        typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "ContinueWith" && m.IsGenericMethodDefinition
                && m.GetParameters() is { Length: 3 } p
                && p[0].ParameterType.IsGenericType
                && p[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<,,>)
                && p[1].ParameterType == typeof(object)
                && p[2].ParameterType == typeof(System.Threading.Tasks.TaskContinuationOptions)), _types.TaskOfObject);

    /// <summary>Resolves <c>TaskExtensions.Unwrap&lt;object&gt;(Task&lt;Task&lt;object&gt;&gt;)</c>.</summary>
    private MethodInfo ResolveTaskUnwrap() => EmitGenerics.MakeGenericMethod(
        typeof(System.Threading.Tasks.TaskExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Unwrap" && m.IsGenericMethodDefinition), _types.Object);

    private void EmitReturnMethodBody()
    {
        var il = ReturnMethod.GetILGenerator();

        // Reject return() while a request is in flight (the body executing, or a next() still pending on
        // the request chain): return() drives MoveNextAsync synchronously to run finallys, which would
        // re-enter a suspended/advancing state machine and corrupt it. Full per-spec queuing of return()
        // behind pending next()s is a follow-up; in a `for await…of` early-exit (the common return() path)
        // each next() is awaited before return() is called, so this fast-rejects only pathological overlap (#542).
        EmitThrowIfBusyAsync(il);

        // If state >= 0 (suspended at a yield point), we need to trigger finally blocks
        // by setting __returnRequested and calling MoveNextAsync
        var simpleReturnLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, StateField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, simpleReturnLabel); // state < 0 → simple return

        // Set __returnRequested = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, ReturnRequestedField);

        // Store return value in CurrentField for later retrieval
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, CurrentField);

        // Drive MoveNextAsync to run the finally blocks, with the executing flag set so a re-entrant
        // call from inside a finally hits the guard (#542). The try/finally clears it even if a finally
        // throws.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, ExecutingField);
        il.BeginExceptionBlock();

        // Call MoveNextAsync() to resume the generator and trigger finally blocks
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, MoveNextAsyncMethod);

        // Await the ValueTask<bool>: .AsTask().GetAwaiter().GetResult()
        var vtLocal = il.DeclareLocal(_types.ValueTaskOfBool);
        il.Emit(OpCodes.Stloc, vtLocal);
        il.Emit(OpCodes.Ldloca, vtLocal);
        var asTask = _types.GetMethodNoParams(_types.ValueTaskOfBool, "AsTask");
        il.Emit(OpCodes.Call, asTask);
        var taskBoolType = typeof(Task<bool>);
        var getAwaiter = _types.GetMethodNoParams(taskBoolType, "GetAwaiter");
        il.Emit(OpCodes.Call, getAwaiter);
        var awaiterType = typeof(TaskAwaiter<bool>);
        var getResult = _types.GetMethodNoParams(awaiterType, "GetResult");
        var awaiterLocal = il.DeclareLocal(awaiterType);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldloca, awaiterLocal);
        il.Emit(OpCodes.Call, getResult);
        il.Emit(OpCodes.Pop); // Discard result

        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, ExecutingField);
        il.EndExceptionBlock();

        il.MarkLabel(simpleReturnLabel);

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
        var fromResultMethod = EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, _types.Object);
        il.Emit(OpCodes.Call, fromResultMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitThrowMethodBody()
    {
        var il = ThrowMethod.GetILGenerator();

        // Reject throw() while a request is in flight (the body executing, or a next() still pending):
        // throw() completes the generator immediately, so letting it run concurrently with a pending
        // next() would settle that next() against an already-closed generator. (#542)
        EmitThrowIfBusyAsync(il);

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
        var fromExceptionMethod = EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!, _types.Object);
        il.Emit(OpCodes.Call, fromExceptionMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits, at the head of next(), a guard that rejects a *synchronously* re-entrant call — the body
    /// calling next() on itself before it suspends — by returning a faulted Task&lt;object&gt; carrying a
    /// TypeError. Without it that call would recurse into MoveNextAsync and overflow the stack (#542). A
    /// next() issued while the body is *suspended* is not rejected here; it queues on the request chain.
    /// The error is wrapped via CreateException so the guest observes a catchable TypeError, surfaced as a
    /// rejected promise (not a synchronous throw) since next() returns a Task. _runtime is non-null here —
    /// DefineAsyncGeneratorMethods only runs when the runtime is present.
    /// </summary>
    private void EmitThrowIfExecutingAsync(ILGenerator il)
    {
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ExecutingField);
        il.Emit(OpCodes.Brfalse, okLabel);

        EmitRejectAlreadyRunning(il);

        il.MarkLabel(okLabel);
    }

    /// <summary>
    /// Emits, at the head of return()/throw(), a guard that rejects when ANY request is in flight: the
    /// body is synchronously advancing (executing), OR a next() is still pending on the request chain
    /// (<see cref="PendingTailField"/> non-null and not yet completed). Unlike next(), return()/throw()
    /// do not queue — they mutate/close the generator immediately, so they must not overlap a pending
    /// next(). Reuses the same catchable "already running" TypeError rejection.
    /// </summary>
    private void EmitThrowIfBusyAsync(ILGenerator il)
    {
        var okLabel = il.DefineLabel();
        var rejectLabel = il.DefineLabel();

        // if (executing) reject;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ExecutingField);
        il.Emit(OpCodes.Brtrue, rejectLabel);

        // if (pendingTail == null) ok;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, PendingTailField);
        il.Emit(OpCodes.Brfalse, okLabel);

        // if (pendingTail.IsCompleted) ok; else reject;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, PendingTailField);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Task, "IsCompleted"));
        il.Emit(OpCodes.Brtrue, okLabel);

        il.MarkLabel(rejectLabel);
        EmitRejectAlreadyRunning(il);

        il.MarkLabel(okLabel);
    }

    /// <summary>
    /// Emits <c>return Task.FromException&lt;object&gt;(CreateException(new TypeError("Async generator is
    /// already running")));</c> — the shared rejection used by both re-entrancy guards.
    /// </summary>
    private void EmitRejectAlreadyRunning(ILGenerator il)
    {
        il.Emit(OpCodes.Ldstr, "Async generator is already running");
        il.Emit(OpCodes.Newobj, _runtime!.TSTypeErrorCtor);
        il.Emit(OpCodes.Call, _runtime!.CreateException);
        var fromException = EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!, _types.Object);
        il.Emit(OpCodes.Call, fromException);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Adds the field that holds the function display class instance for closure mutation
    /// sharing (#725). Mirrors <see cref="GeneratorStateMachineBuilder.DefineFunctionDisplayClassField"/>.
    /// </summary>
    public void DefineFunctionDisplayClassField(Type dcType)
    {
        FunctionDCField = _stateMachineType.DefineField(
            "<>__functionDC",
            dcType,
            FieldAttributes.Public);
    }

    /// <summary>
    /// Gets a field for a variable by name, checking both parameters and locals.
    /// </summary>
    public override FieldBuilder? GetVariableField(string name) => _hoisting.GetVariableField(name);

    /// <summary>
    /// Gets the hoisted enumerator field for a for...of loop containing suspension points.
    /// </summary>
    public FieldBuilder? GetEnumeratorField(Parsing.Stmt.ForOf loop) => _hoisting.GetEnumeratorField(loop);

    /// <summary>
    /// Finalizes the type after MoveNextAsync body has been emitted.
    /// </summary>
    public override Type CreateType()
    {
        ILLabelValidator.SweepAllTypes(new[] { _stateMachineType });
        ILLabelValidator.SweepConstructors(new[] { _stateMachineType });
        return _stateMachineType.CreateType()!;
    }
}
