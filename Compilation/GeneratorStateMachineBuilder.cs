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
    public void DefineStateMachine(
        string methodName,
        GeneratorStateAnalyzer.GeneratorFunctionAnalysis analysis,
        bool isInstanceMethod = false)
    {
        // Define the state machine class (using class for reference semantics with IEnumerable)
        // Name follows C# compiler convention: <MethodName>d__N
        _stateMachineType = _moduleBuilder.DefineType(
            $"<{methodName}>d__{_counter}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
             _types.IEnumerableOfObject, _types.IEnumerable]
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
}
