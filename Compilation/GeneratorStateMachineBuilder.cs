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
public class GeneratorStateMachineBuilder
{
    private readonly ModuleBuilder _moduleBuilder;
    private readonly TypeProvider _types;
    private TypeBuilder _stateMachineType = null!;
    private int _counter;

    // The type being built
    public TypeBuilder StateMachineType => _stateMachineType;

    // Core state machine fields
    public FieldBuilder StateField { get; private set; } = null!;
    public FieldBuilder CurrentField { get; private set; } = null!;

    // Hoisted variables (become struct fields)
    public Dictionary<string, FieldBuilder> HoistedParameters { get; } = [];
    public Dictionary<string, FieldBuilder> HoistedLocals { get; } = [];

    // 'this' field for instance generator methods
    public FieldBuilder? ThisField { get; private set; }

    // Delegated enumerator field for yield* expressions
    public FieldBuilder? DelegatedEnumeratorField { get; private set; }

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
    /// <param name="runtime">Optional runtime reference for $IGenerator interface</param>
    public void DefineStateMachine(
        string methodName,
        GeneratorStateAnalyzer.GeneratorFunctionAnalysis analysis,
        bool isInstanceMethod = false,
        EmittedRuntime? runtime = null)
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

        // Define hoisted parameter fields
        foreach (var paramName in analysis.HoistedParameters)
        {
            var field = _stateMachineType.DefineField(
                paramName,
                _types.Object,
                FieldAttributes.Public
            );
            HoistedParameters[paramName] = field;
        }

        // Define hoisted local fields
        foreach (var localName in analysis.HoistedLocals)
        {
            var field = _stateMachineType.DefineField(
                localName,
                _types.Object,
                FieldAttributes.Public
            );
            HoistedLocals[localName] = field;
        }

        // Define 'this' field for instance methods that use 'this'
        if (isInstanceMethod && analysis.UsesThis)
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
    /// Gets a field for a variable by name, checking both parameters and locals.
    /// </summary>
    public FieldBuilder? GetVariableField(string name)
    {
        if (HoistedParameters.TryGetValue(name, out var paramField))
            return paramField;
        if (HoistedLocals.TryGetValue(name, out var localField))
            return localField;
        return null;
    }

    /// <summary>
    /// Checks if a variable is hoisted to the state machine.
    /// </summary>
    public bool IsHoisted(string name)
    {
        return HoistedParameters.ContainsKey(name) || HoistedLocals.ContainsKey(name);
    }

    /// <summary>
    /// Finalizes the type after MoveNext body has been emitted.
    /// </summary>
    public Type CreateType()
    {
        return _stateMachineType.CreateType()!;
    }

    /// <summary>
    /// Defines the $IGenerator interface methods: Next, Return, Throw.
    /// </summary>
    private void DefineGeneratorMethods()
    {
        // We need to emit an iterator result object with { value, done } properties
        // For simplicity, we'll use a Dictionary<string, object> as the result

        // next() method - wraps MoveNext/Current into iterator result
        // Using lowercase to match JavaScript API
        NextMethod = _stateMachineType.DefineMethod(
            "next",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            Type.EmptyTypes
        );

        var nextIL = NextMethod.GetILGenerator();
        var doneLabel = nextIL.DefineLabel();
        var endLabel = nextIL.DefineLabel();

        // Call MoveNext()
        nextIL.Emit(OpCodes.Ldarg_0);
        nextIL.Emit(OpCodes.Call, MoveNextMethod);
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

        // Done: create { value: undefined, done: true }
        nextIL.MarkLabel(doneLabel);
        nextIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        nextIL.Emit(OpCodes.Dup);
        nextIL.Emit(OpCodes.Ldstr, "value");
        nextIL.Emit(OpCodes.Ldnull);
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

        // Set state to -2 (completed)
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

        // Set state to -2 (completed)
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
}
