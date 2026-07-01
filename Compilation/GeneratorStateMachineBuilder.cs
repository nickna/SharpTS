using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Builds a state machine struct for a generator function.
/// The state machine implements IEnumerator&lt;object?&gt; and contains:
/// - State field for tracking execution position
/// - Current field for the yielded value
/// - Hoisted parameter and local variable fields
/// </summary>
public class GeneratorStateMachineBuilder : IIteratorStateMachineBuilder
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

    // The value passed to next(v); read by a resumed yield expression (ECMA-262
    // §27.5.3.3 — `yield expr` evaluates to the argument of the resuming next).
    public FieldBuilder SentField { get; private set; } = null!;

    // True only while the body is running inside MoveNext (set around the call in next()).
    // A re-entrant next()/return()/throw() — the generator advancing itself — observes it
    // set and is rejected with a TypeError per ECMA-262 §27.5.3.3 (#521). Defined only when
    // the $IGenerator methods are emitted (see DefineGeneratorMethods).
    public FieldBuilder? ExecutingField { get; private set; }

    // An external return()/throw() on a *suspended* generator injects an abrupt completion at the
    // yield point so active try/finally(/catch) run (ECMA-262 §27.5.3.4, #526). return()/throw() set
    // these and drive MoveNext, which the yield-resume code consults: InjectKindReturn behaves as a
    // `return InjectedValue`, InjectKindThrow as a `throw InjectedValue`, routed through the
    // flag-based try machinery. Defined alongside the $IGenerator methods. Null kind = InjectKindNone.
    public const int InjectKindNone = 0;
    public const int InjectKindReturn = 1;
    public const int InjectKindThrow = 2;

    public FieldBuilder? InjectedKindField { get; private set; }
    public FieldBuilder? InjectedValueField { get; private set; }

    // Hoisted variables (become struct fields) - delegated to HoistingManager.
    // Captured outer-scope variables are intentionally NOT hoisted: a generator reads them
    // live from their enclosing storage (entry-point display class / top-level static fields)
    // in MoveNext, mirroring JS closure semantics and the async-generator path (#541).
    public Dictionary<string, FieldBuilder> HoistedParameters => _hoisting.HoistedParameters;
    public Dictionary<string, FieldBuilder> HoistedLocals => _hoisting.HoistedLocals;

    // 'this' field for instance generator methods
    public FieldBuilder? ThisField { get; private set; }

    // Function-level display class field for captured-and-mutated locals (#674).
    // An arrow inside the generator body that WRITES a variable captured from the
    // generator scope shares storage with the generator through this reference-typed
    // display class (mirrors AsyncStateMachineBuilder.FunctionDCField). Null when the
    // generator has no such write-capture.
    public FieldBuilder? FunctionDCField { get; private set; }

    // Delegated enumerator field for yield* expressions
    public FieldBuilder? DelegatedEnumeratorField { get; private set; }

    // Stack-spill fields for yield*'s that appear in expression contexts with pre-existing
    // stack items. A yield*'s resume label is the target of both fall-through (stack=N) and
    // state-dispatch (stack=0), which CLR rejects as a stack-imbalance. We spill pre-yield*
    // stack items into these fields, run setup/resume with an empty stack, and restore them
    // at loop-end so callers observe the expected stack shape.
    private readonly Dictionary<(int StateNumber, int Slot), FieldBuilder> _yieldStarSpillFields = new();

    public FieldBuilder GetOrDefineYieldStarSpillField(int stateNumber, int slot)
    {
        var key = (stateNumber, slot);
        if (!_yieldStarSpillFields.TryGetValue(key, out var f))
        {
            f = _stateMachineType.DefineField(
                $"<>s__{stateNumber}_{slot}",
                _types.Object,
                FieldAttributes.Private);
            _yieldStarSpillFields[key] = f;
        }
        return f;
    }

    // Constructor
    public ConstructorBuilder Constructor { get; private set; } = null!;

    // Methods
    public MethodBuilder MoveNextMethod { get; private set; } = null!;
    public MethodBuilder CurrentGetMethod { get; private set; } = null!;
    public MethodBuilder ResetMethod { get; private set; } = null!;
    public MethodBuilder DisposeMethod { get; private set; } = null!;
    public MethodBuilder NonGenericCurrentGetMethod { get; private set; } = null!;
    public MethodBuilder GetEnumeratorMethod { get; private set; } = null!;
    public MethodBuilder NonGenericGetEnumeratorMethod { get; private set; } = null!;

    // $IGenerator methods for return/throw support
    public MethodBuilder NextMethod { get; private set; } = null!;
    public MethodBuilder ReturnMethod { get; private set; } = null!;
    public MethodBuilder ThrowMethod { get; private set; } = null!;

    // Runtime reference for $IGenerator interface
    private EmittedRuntime? _runtime;

    public GeneratorStateMachineBuilder(ModuleBuilder moduleBuilder, TypeProvider types, int counter = 0)
    {
        _moduleBuilder = moduleBuilder;
        _types = types;
        _counter = counter;
    }

    /// <summary>
    /// Defines the complete state machine struct type with all fields and method stubs.
    /// </summary>
    /// <param name="methodName">Name of the generator method (used in type name)</param>
    /// <param name="analysis">Analysis results from GeneratorStateAnalyzer</param>
    /// <param name="isInstanceMethod">True if this is an instance method (needs 'this' hoisting)</param>
    /// <param name="hasDynamicThis">True for a free-function stub lifted from a <c>HasOwnThis</c> generator
    /// expression / object generator method (#775): it carries its own dynamic receiver in a leading
    /// <c>__this</c> stub argument rather than an IL instance <c>this</c>, so it still needs the
    /// <c>&lt;&gt;4__this</c> field even though it is a static stub.</param>
    /// <param name="runtime">Optional runtime reference for $IGenerator interface</param>
    public void DefineStateMachine(
        string methodName,
        GeneratorStateAnalyzer.GeneratorFunctionAnalysis analysis,
        bool isInstanceMethod = false,
        EmittedRuntime? runtime = null,
        bool hasDynamicThis = false)
    {
        _runtime = runtime;

        // Build list of interfaces to implement
        var interfaces = new List<Type>
        {
            _types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
            _types.IEnumerableOfObject, _types.IEnumerable
        };

        // Add $IGenerator interface if runtime is available
        if (runtime?.GeneratorInterfaceType != null)
        {
            interfaces.Add(runtime.GeneratorInterfaceType);
        }

        // Define the state machine class (using class for reference semantics with IEnumerable)
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
        DefineSentField();

        // Define hoisted variables using HoistingManager.
        // Captured outer-scope variables are deliberately not hoisted into state-machine
        // fields: doing so snapshotted their value at generator-creation time, diverging
        // from JS closure semantics (#541). They are instead read live from their enclosing
        // storage in MoveNext, the same way the async-generator path already works.
        _hoisting = new HoistingManager(_stateMachineType, _types.Object);
        _hoisting.DefineHoistedParameters(analysis.HoistedParameters);
        _hoisting.DefineHoistedLocals(analysis.HoistedLocals);
        _hoisting.DefineHoistedEnumerators(analysis.ForOfLoopsWithYield, _types.IEnumerator);
        _hoisting.DefineHoistedForInState(analysis.ForInLoopsWithYield, _types.ListOfObject, _types.Int32);

        // Define 'this' field for instance methods that use 'this', and for a free-function generator
        // stub that binds its own dynamic receiver (#775 — a `HasOwnThis` generator expression / object
        // generator method, threaded in via a leading `__this` stub argument).
        if ((isInstanceMethod || hasDynamicThis) && analysis.UsesThis)
        {
            ThisField = _stateMachineType.DefineField(
                "<>4__this",
                _types.Object,
                FieldAttributes.Public
            );
        }

        // Define delegated enumerator field for yield* expressions
        if (analysis.HasYieldStar)
        {
            DelegatedEnumeratorField = _stateMachineType.DefineField(
                "<>7__wrap1",
                _types.IEnumerator,
                FieldAttributes.Private
            );
        }

        // Define constructor
        DefineConstructor();

        // Define the IEnumerator methods
        DefineMoveNextMethod();
        DefineCurrentProperty();
        DefineResetMethod();
        DefineDisposeMethod();

        // Define IEnumerable methods
        DefineGetEnumeratorMethods();

        // Define $IGenerator methods if runtime is available
        if (_runtime?.GeneratorInterfaceType != null)
        {
            DefineGeneratorMethods();
        }
    }

    private void DefineStateField()
    {
        // <>1__state - tracks execution position
        // -1 = not started, -2 = completed, 0+ = yielded at specific point
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

    private void DefineSentField()
    {
        // <>3__sent - the value passed to next(v), delivered to the resumed yield.
        // Stored by next() before driving MoveNext; read on the resume path.
        SentField = _stateMachineType.DefineField(
            "<>3__sent",
            _types.Object,
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
        // Initialize state to -1 (not started)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, StateField);
        // Seed the sent value with undefined so a yield resumed without an explicit
        // next(v) — for...of and yield* delegation drive MoveNext directly, never
        // setting SentField — evaluates to undefined rather than the null default.
        if (_runtime?.UndefinedInstance != null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldsfld, _runtime.UndefinedInstance);
            il.Emit(OpCodes.Stfld, SentField);
        }
        il.Emit(OpCodes.Ret);
    }

    private void DefineMoveNextMethod()
    {
        // bool IEnumerator.MoveNext()
        MoveNextMethod = _stateMachineType.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Boolean,
            Type.EmptyTypes
        );

        // Mark as implementing IEnumerator.MoveNext
        var interfaceMethod = _types.GetMethodNoParams(_types.IEnumerator, "MoveNext");
        _stateMachineType.DefineMethodOverride(MoveNextMethod, interfaceMethod);
    }

    private void DefineCurrentProperty()
    {
        // object IEnumerator<object>.Current { get; }
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

        // Mark as implementing IEnumerator<object>.Current
        var interfaceMethod = _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current");
        _stateMachineType.DefineMethodOverride(CurrentGetMethod, interfaceMethod);

        // Also implement non-generic IEnumerator.Current
        var nonGenericCurrentProp = _stateMachineType.DefineProperty(
            "System.Collections.IEnumerator.Current",
            PropertyAttributes.None,
            _types.Object,
            Type.EmptyTypes
        );

        NonGenericCurrentGetMethod = _stateMachineType.DefineMethod(
            "System.Collections.IEnumerator.get_Current",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot,
            _types.Object,
            Type.EmptyTypes
        );

        var il2 = NonGenericCurrentGetMethod.GetILGenerator();
        il2.Emit(OpCodes.Ldarg_0);
        il2.Emit(OpCodes.Ldfld, CurrentField);
        il2.Emit(OpCodes.Ret);

        nonGenericCurrentProp.SetGetMethod(NonGenericCurrentGetMethod);

        var nonGenericInterfaceMethod = _types.GetPropertyGetter(_types.IEnumerator, "Current");
        _stateMachineType.DefineMethodOverride(NonGenericCurrentGetMethod, nonGenericInterfaceMethod);
    }

    private void DefineResetMethod()
    {
        // void IEnumerator.Reset() - throws NotSupportedException
        ResetMethod = _stateMachineType.DefineMethod(
            "Reset",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            Type.EmptyTypes
        );

        var il = ResetMethod.GetILGenerator();
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.NotSupportedException));
        il.Emit(OpCodes.Throw);

        var interfaceMethod = _types.GetMethodNoParams(_types.IEnumerator, "Reset");
        _stateMachineType.DefineMethodOverride(ResetMethod, interfaceMethod);
    }

    private void DefineDisposeMethod()
    {
        // void IDisposable.Dispose()
        DisposeMethod = _stateMachineType.DefineMethod(
            "Dispose",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            Type.EmptyTypes
        );

        // Empty dispose for now (could set state to -2)
        var il = DisposeMethod.GetILGenerator();
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.GetMethodNoParams(_types.IDisposable, "Dispose");
        _stateMachineType.DefineMethodOverride(DisposeMethod, interfaceMethod);
    }

    private void DefineGetEnumeratorMethods()
    {
        // IEnumerator<object> IEnumerable<object>.GetEnumerator()
        // Returns 'this' since the generator IS the enumerator
        GetEnumeratorMethod = _stateMachineType.DefineMethod(
            "GetEnumerator",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.IEnumeratorOfObject,
            Type.EmptyTypes
        );

        var il = GetEnumeratorMethod.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.GetMethodNoParams(_types.IEnumerableOfObject, "GetEnumerator");
        _stateMachineType.DefineMethodOverride(GetEnumeratorMethod, interfaceMethod);

        // IEnumerator IEnumerable.GetEnumerator()
        NonGenericGetEnumeratorMethod = _stateMachineType.DefineMethod(
            "System.Collections.IEnumerable.GetEnumerator",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.IEnumerator,
            Type.EmptyTypes
        );

        var il2 = NonGenericGetEnumeratorMethod.GetILGenerator();
        il2.Emit(OpCodes.Ldarg_0);
        il2.Emit(OpCodes.Ret);

        var nonGenericInterfaceMethod = _types.GetMethodNoParams(_types.IEnumerable, "GetEnumerator");
        _stateMachineType.DefineMethodOverride(NonGenericGetEnumeratorMethod, nonGenericInterfaceMethod);
    }

    /// <summary>
    /// Adds the field that holds the function display class instance for closure mutation
    /// sharing (#674). Mirrors <see cref="AsyncStateMachineBuilder.DefineFunctionDisplayClassField"/>.
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
    public FieldBuilder? GetVariableField(string name) => _hoisting.GetVariableField(name);

    /// <summary>
    /// Gets the hoisted enumerator field for a for...of loop, or null if not hoisted.
    /// </summary>
    public FieldBuilder? GetEnumeratorField(Parsing.Stmt.ForOf loop) => _hoisting.GetEnumeratorField(loop);

    /// <summary>
    /// Gets the hoisted key-list / index fields for a for...in loop containing yields, or null if not hoisted (#547).
    /// </summary>
    public FieldBuilder? GetForInKeysField(Parsing.Stmt.ForIn loop) => _hoisting.GetForInKeysField(loop);
    public FieldBuilder? GetForInIndexField(Parsing.Stmt.ForIn loop) => _hoisting.GetForInIndexField(loop);

    /// <summary>
    /// Finalizes the type after MoveNext body has been emitted.
    /// </summary>
    public Type CreateType()
    {
        ILLabelValidator.SweepAllTypes(new[] { _stateMachineType });
        ILLabelValidator.SweepConstructors(new[] { _stateMachineType });
        return _stateMachineType.CreateType()!;
    }

    /// <summary>
    /// Defines the $IGenerator interface methods: Next, Return, Throw.
    /// </summary>
    private void DefineGeneratorMethods()
    {
        // We need to emit an iterator result object with { value, done } properties
        // For simplicity, we'll use a Dictionary<string, object> as the result

        // Re-entrancy flag: set only while the body runs inside MoveNext (see next()).
        // The single observable window for a guest call is re-entrancy — the generator
        // advancing itself — which ECMA-262 §27.5.3.3 (GeneratorValidate) rejects with a
        // TypeError. Without this the compiled state machine would recurse into MoveNext
        // and overflow the stack rather than throw (#521).
        var executingField = _stateMachineType.DefineField(
            "<>5__executing",
            _types.Boolean,
            FieldAttributes.Private);
        ExecutingField = executingField;

        // Injection state for external return()/throw() on a suspended generator (#526).
        InjectedKindField = _stateMachineType.DefineField(
            "<>6__injectedKind", _types.Int32, FieldAttributes.Private);
        InjectedValueField = _stateMachineType.DefineField(
            "<>6__injectedValue", _types.Object, FieldAttributes.Private);

        // next() method - wraps MoveNext/Current into iterator result
        // Using lowercase to match JavaScript API
        NextMethod = _stateMachineType.DefineMethod(
            "next",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            [_types.Object]
        );

        var nextIL = NextMethod.GetILGenerator();
        var doneLabel = nextIL.DefineLabel();
        var endLabel = nextIL.DefineLabel();

        // Reject a re-entrant next() before touching any state (ECMA-262 §27.5.3.3).
        EmitThrowIfExecuting(nextIL, executingField);

        // Stash the sent value so the resumed yield (read of SentField in MoveNext)
        // sees it. Set before MoveNext so the resume path observes the new value.
        nextIL.Emit(OpCodes.Ldarg_0);
        nextIL.Emit(OpCodes.Ldarg_1);
        nextIL.Emit(OpCodes.Stfld, SentField);

        // Run the body with the executing flag set so a re-entrant next()/return()/throw()
        // from inside it hits the guards and throws "already running" instead of recursing
        // into MoveNext. The try/finally clears the flag on suspension, completion, AND an
        // uncaught body throw — a leaked flag would make later calls falsely report it.
        nextIL.Emit(OpCodes.Ldarg_0);
        nextIL.Emit(OpCodes.Ldc_I4_1);
        nextIL.Emit(OpCodes.Stfld, executingField);
        var movedLocal = nextIL.DeclareLocal(_types.Boolean);
        nextIL.BeginExceptionBlock();
        nextIL.Emit(OpCodes.Ldarg_0);
        nextIL.Emit(OpCodes.Call, MoveNextMethod);
        nextIL.Emit(OpCodes.Stloc, movedLocal);
        nextIL.BeginFinallyBlock();
        nextIL.Emit(OpCodes.Ldarg_0);
        nextIL.Emit(OpCodes.Ldc_I4_0);
        nextIL.Emit(OpCodes.Stfld, executingField);
        nextIL.EndExceptionBlock();
        nextIL.Emit(OpCodes.Ldloc, movedLocal);
        nextIL.Emit(OpCodes.Brfalse, doneLabel);

        // Not done: create { value: Current, done: false }
        nextIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        nextIL.Emit(OpCodes.Dup);
        nextIL.Emit(OpCodes.Ldstr, "value");
        nextIL.Emit(OpCodes.Ldarg_0);
        nextIL.Emit(OpCodes.Ldfld, CurrentField);
        nextIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        nextIL.Emit(OpCodes.Dup);
        nextIL.Emit(OpCodes.Ldstr, "done");
        nextIL.Emit(OpCodes.Ldc_I4_0);
        nextIL.Emit(OpCodes.Box, _types.Boolean);
        nextIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        nextIL.Emit(OpCodes.Br, endLabel);

        // Done: create { value: <completion>, done: true }
        // ECMA-262 27.3.2: the completion value is the generator's return expression result,
        // stored in CurrentField by EmitReturn. Emit `this.CurrentField` instead of `null`.
        nextIL.MarkLabel(doneLabel);
        nextIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        nextIL.Emit(OpCodes.Dup);
        nextIL.Emit(OpCodes.Ldstr, "value");
        nextIL.Emit(OpCodes.Ldarg_0);
        nextIL.Emit(OpCodes.Ldfld, CurrentField);
        nextIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        nextIL.Emit(OpCodes.Dup);
        nextIL.Emit(OpCodes.Ldstr, "done");
        nextIL.Emit(OpCodes.Ldc_I4_1);
        nextIL.Emit(OpCodes.Box, _types.Boolean);
        nextIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));

        nextIL.MarkLabel(endLabel);
        nextIL.Emit(OpCodes.Ret);

        _stateMachineType.DefineMethodOverride(NextMethod, _runtime!.GeneratorNextMethod);

        // return(value) method - closes generator and returns { value, done: true }
        // Using lowercase to match JavaScript API
        ReturnMethod = _stateMachineType.DefineMethod(
            "return",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            [_types.Object]
        );

        var returnIL = ReturnMethod.GetILGenerator();

        // Reject a re-entrant return() (ECMA-262 §27.5.3.3) before mutating state.
        EmitThrowIfExecuting(returnIL, executingField);

        // Suspended at a yield (state >= 0): inject a return so the body resumes there as an abrupt
        // completion, running active finally(s), then settle from the resulting state (#526).
        var returnNotSuspended = returnIL.DefineLabel();
        returnIL.Emit(OpCodes.Ldarg_0);
        returnIL.Emit(OpCodes.Ldfld, StateField);
        returnIL.Emit(OpCodes.Ldc_I4_0);
        returnIL.Emit(OpCodes.Blt, returnNotSuspended);
        EmitInjectAndResume(returnIL, InjectKindReturn);

        // Not started or already completed: close without running the body — its finally never runs
        // (ECMA-262 §27.5.3.4) — and report { value: arg, done: true }.
        returnIL.MarkLabel(returnNotSuspended);
        returnIL.Emit(OpCodes.Ldarg_0);
        returnIL.Emit(OpCodes.Ldc_I4, -2);
        returnIL.Emit(OpCodes.Stfld, StateField);

        // Create { value: arg, done: true }
        returnIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        returnIL.Emit(OpCodes.Dup);
        returnIL.Emit(OpCodes.Ldstr, "value");
        returnIL.Emit(OpCodes.Ldarg_1);
        returnIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        returnIL.Emit(OpCodes.Dup);
        returnIL.Emit(OpCodes.Ldstr, "done");
        returnIL.Emit(OpCodes.Ldc_I4_1);
        returnIL.Emit(OpCodes.Box, _types.Boolean);
        returnIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        returnIL.Emit(OpCodes.Ret);

        _stateMachineType.DefineMethodOverride(ReturnMethod, _runtime!.GeneratorReturnMethod);

        // throw(error) method - closes generator and throws
        // Using lowercase to match JavaScript API
        ThrowMethod = _stateMachineType.DefineMethod(
            "throw",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            [_types.Object]
        );

        var throwIL = ThrowMethod.GetILGenerator();

        // Reject a re-entrant throw() (ECMA-262 §27.5.3.3): the "already running" guard
        // takes precedence over injecting the caller's error into the running body.
        EmitThrowIfExecuting(throwIL, executingField);

        // Suspended at a yield (state >= 0): inject the error so the body resumes there as a throw,
        // giving an enclosing catch/finally a chance to run (#526). If nothing handles it, MoveNext
        // rethrows and the error propagates out of throw() — exactly the not-suspended behaviour below.
        var throwNotSuspended = throwIL.DefineLabel();
        throwIL.Emit(OpCodes.Ldarg_0);
        throwIL.Emit(OpCodes.Ldfld, StateField);
        throwIL.Emit(OpCodes.Ldc_I4_0);
        throwIL.Emit(OpCodes.Blt, throwNotSuspended);
        EmitInjectAndResume(throwIL, InjectKindThrow);

        // Not started or already completed: close and throw without running the body.
        throwIL.MarkLabel(throwNotSuspended);
        throwIL.Emit(OpCodes.Ldarg_0);
        throwIL.Emit(OpCodes.Ldc_I4, -2);
        throwIL.Emit(OpCodes.Stfld, StateField);

        // If error is already an Exception, rethrow it directly
        // Otherwise, use CreateException to properly wrap the value with __tsValue
        var isExceptionLabel = throwIL.DefineLabel();
        var createExceptionLabel = throwIL.DefineLabel();

        throwIL.Emit(OpCodes.Ldarg_1);
        throwIL.Emit(OpCodes.Isinst, _types.Exception);
        throwIL.Emit(OpCodes.Dup);
        throwIL.Emit(OpCodes.Brtrue, isExceptionLabel);

        // Not an exception - use CreateException to wrap with __tsValue
        throwIL.Emit(OpCodes.Pop);
        throwIL.Emit(OpCodes.Ldarg_1);
        throwIL.Emit(OpCodes.Call, _runtime!.CreateException);
        throwIL.Emit(OpCodes.Throw);

        // Already an exception - rethrow it
        throwIL.MarkLabel(isExceptionLabel);
        throwIL.Emit(OpCodes.Throw);

        _stateMachineType.DefineMethodOverride(ThrowMethod, _runtime!.GeneratorThrowMethod);
    }

    /// <summary>
    /// Emits <c>if (this.&lt;&gt;5__executing) throw new TypeError("Generator is already running");</c>
    /// at the head of next()/return()/throw(). The error is wrapped via CreateException so the guest
    /// observes a catchable TypeError (ECMA-262 §27.5.3.3). Only called from DefineGeneratorMethods,
    /// where <see cref="_runtime"/> is guaranteed non-null.
    /// </summary>
    private void EmitThrowIfExecuting(ILGenerator il, FieldBuilder executingField)
    {
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, executingField);
        il.Emit(OpCodes.Brfalse, okLabel);
        il.Emit(OpCodes.Ldstr, "Generator is already running");
        il.Emit(OpCodes.Newobj, _runtime!.TSTypeErrorCtor);
        il.Emit(OpCodes.Call, _runtime!.CreateException);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(okLabel);
    }

    /// <summary>
    /// Emits the suspended-generator arm of return()/throw() (#526): stash the injected kind and the
    /// argument (arg1), drive MoveNext under the executing guard (so the resumed body sees the
    /// injection at its yield point and runs active finally/catch), then settle as
    /// <c>{ value: Current, done: !moved }</c>. A finally that yields makes MoveNext return true, so
    /// the abrupt completion is reported as a non-done yield and finished on a later call — mirroring
    /// the interpreter. If MoveNext rethrows an uncaught injected throw, it propagates out unchanged.
    /// Ends with <c>ret</c>; only called when <see cref="InjectedKindField"/> et al. are defined.
    /// </summary>
    private void EmitInjectAndResume(ILGenerator il, int injectKind)
    {
        // injectedValue = arg1; injectedKind = injectKind
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, InjectedValueField!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, injectKind);
        il.Emit(OpCodes.Stfld, InjectedKindField!);

        // executing = true; try { moved = MoveNext(); } finally { executing = false; }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, ExecutingField!);
        var movedLocal = il.DeclareLocal(_types.Boolean);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, MoveNextMethod);
        il.Emit(OpCodes.Stloc, movedLocal);
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, ExecutingField!);
        il.EndExceptionBlock();

        // return { value: Current, done: (moved ? false : true) }
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, CurrentField);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldloc, movedLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);   // !moved
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ret);
    }
}
