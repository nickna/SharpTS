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

    public GeneratorStateMachineBuilder(ModuleBuilder moduleBuilder, int counter = 0)
    {
        _moduleBuilder = moduleBuilder;
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
            typeof(object),
            [typeof(IEnumerator<object>), typeof(IEnumerator), typeof(IDisposable),
             typeof(IEnumerable<object>), typeof(IEnumerable)]
        );

        // Define core fields
        DefineStateField();
        DefineCurrentField();

        // Define hoisted parameter fields
        foreach (var paramName in analysis.HoistedParameters)
        {
            var field = _stateMachineType.DefineField(
                paramName,
                typeof(object),
                FieldAttributes.Public
            );
            HoistedParameters[paramName] = field;
        }

        // Define hoisted local fields
        foreach (var localName in analysis.HoistedLocals)
        {
            var field = _stateMachineType.DefineField(
                localName,
                typeof(object),
                FieldAttributes.Public
            );
            HoistedLocals[localName] = field;
        }

        // Define 'this' field for instance methods that use 'this'
        if (isInstanceMethod && analysis.UsesThis)
        {
            ThisField = _stateMachineType.DefineField(
                "<>4__this",
                typeof(object),
                FieldAttributes.Public
            );
        }

        // Define delegated enumerator field for yield* expressions
        if (analysis.HasYieldStar)
        {
            DelegatedEnumeratorField = _stateMachineType.DefineField(
                "<>7__wrap1",
                typeof(System.Collections.IEnumerator),
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
            typeof(int),
            FieldAttributes.Public
        );
    }

    private void DefineCurrentField()
    {
        // <>2__current - the current yielded value
        CurrentField = _stateMachineType.DefineField(
            "<>2__current",
            typeof(object),
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
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
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
            typeof(bool),
            Type.EmptyTypes
        );

        // Mark as implementing IEnumerator.MoveNext
        var interfaceMethod = typeof(IEnumerator).GetMethod("MoveNext")!;
        _stateMachineType.DefineMethodOverride(MoveNextMethod, interfaceMethod);
    }

    private void DefineCurrentProperty()
    {
        // object IEnumerator<object>.Current { get; }
        var currentProp = _stateMachineType.DefineProperty(
            "Current",
            PropertyAttributes.None,
            typeof(object),
            Type.EmptyTypes
        );

        CurrentGetMethod = _stateMachineType.DefineMethod(
            "get_Current",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot,
            typeof(object),
            Type.EmptyTypes
        );

        var il = CurrentGetMethod.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, CurrentField);
        il.Emit(OpCodes.Ret);

        currentProp.SetGetMethod(CurrentGetMethod);

        // Mark as implementing IEnumerator<object>.Current
        var interfaceMethod = typeof(IEnumerator<object>).GetProperty("Current")!.GetGetMethod()!;
        _stateMachineType.DefineMethodOverride(CurrentGetMethod, interfaceMethod);

        // Also implement non-generic IEnumerator.Current
        var nonGenericCurrentProp = _stateMachineType.DefineProperty(
            "System.Collections.IEnumerator.Current",
            PropertyAttributes.None,
            typeof(object),
            Type.EmptyTypes
        );

        NonGenericCurrentGetMethod = _stateMachineType.DefineMethod(
            "System.Collections.IEnumerator.get_Current",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot,
            typeof(object),
            Type.EmptyTypes
        );

        var il2 = NonGenericCurrentGetMethod.GetILGenerator();
        il2.Emit(OpCodes.Ldarg_0);
        il2.Emit(OpCodes.Ldfld, CurrentField);
        il2.Emit(OpCodes.Ret);

        nonGenericCurrentProp.SetGetMethod(NonGenericCurrentGetMethod);

        var nonGenericInterfaceMethod = typeof(IEnumerator).GetProperty("Current")!.GetGetMethod()!;
        _stateMachineType.DefineMethodOverride(NonGenericCurrentGetMethod, nonGenericInterfaceMethod);
    }

    private void DefineResetMethod()
    {
        // void IEnumerator.Reset() - throws NotSupportedException
        ResetMethod = _stateMachineType.DefineMethod(
            "Reset",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes
        );

        var il = ResetMethod.GetILGenerator();
        il.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Throw);

        var interfaceMethod = typeof(IEnumerator).GetMethod("Reset")!;
        _stateMachineType.DefineMethodOverride(ResetMethod, interfaceMethod);
    }

    private void DefineDisposeMethod()
    {
        // void IDisposable.Dispose()
        DisposeMethod = _stateMachineType.DefineMethod(
            "Dispose",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes
        );

        // Empty dispose for now (could set state to -2)
        var il = DisposeMethod.GetILGenerator();
        il.Emit(OpCodes.Ret);

        var interfaceMethod = typeof(IDisposable).GetMethod("Dispose")!;
        _stateMachineType.DefineMethodOverride(DisposeMethod, interfaceMethod);
    }

    private void DefineGetEnumeratorMethods()
    {
        // IEnumerator<object> IEnumerable<object>.GetEnumerator()
        // Returns 'this' since the generator IS the enumerator
        GetEnumeratorMethod = _stateMachineType.DefineMethod(
            "GetEnumerator",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(IEnumerator<object>),
            Type.EmptyTypes
        );

        var il = GetEnumeratorMethod.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = typeof(IEnumerable<object>).GetMethod("GetEnumerator")!;
        _stateMachineType.DefineMethodOverride(GetEnumeratorMethod, interfaceMethod);

        // IEnumerator IEnumerable.GetEnumerator()
        NonGenericGetEnumeratorMethod = _stateMachineType.DefineMethod(
            "System.Collections.IEnumerable.GetEnumerator",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(IEnumerator),
            Type.EmptyTypes
        );

        var il2 = NonGenericGetEnumeratorMethod.GetILGenerator();
        il2.Emit(OpCodes.Ldarg_0);
        il2.Emit(OpCodes.Ret);

        var nonGenericInterfaceMethod = typeof(IEnumerable).GetMethod("GetEnumerator")!;
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
