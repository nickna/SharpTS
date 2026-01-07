using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Builds a state machine struct for an async function.
/// The state machine implements IAsyncStateMachine and contains:
/// - State field for tracking execution position
/// - Builder field for Task creation and completion
/// - Hoisted parameter and local variable fields
/// - Awaiter fields for each await point
/// </summary>
public class AsyncStateMachineBuilder
{
    private readonly ModuleBuilder _moduleBuilder;
    private readonly TypeProvider _types;
    private TypeBuilder _stateMachineType = null!;
    private int _counter;

    // The type being built
    public TypeBuilder StateMachineType => _stateMachineType;

    // Core state machine fields
    public FieldBuilder StateField { get; private set; } = null!;
    public FieldBuilder BuilderField { get; private set; } = null!;

    // Hoisted variables (become struct fields)
    public Dictionary<string, FieldBuilder> HoistedParameters { get; } = [];
    public Dictionary<string, FieldBuilder> HoistedLocals { get; } = [];

    // Awaiter fields (one per await point)
    public Dictionary<int, FieldBuilder> AwaiterFields { get; } = [];

    // 'this' field for instance async methods
    public FieldBuilder? ThisField { get; private set; }

    // Self-reference field (boxed) for passing to nested async arrows
    public FieldBuilder? SelfBoxedField { get; private set; }

    // Methods
    public MethodBuilder MoveNextMethod { get; private set; } = null!;
    public MethodBuilder SetStateMachineMethod { get; private set; } = null!;

    // Builder type (Task vs Task<T>)
    public Type BuilderType { get; private set; } = null!;
    public Type TaskType { get; private set; } = null!;
    public Type AwaiterType { get; private set; } = null!;

    public AsyncStateMachineBuilder(ModuleBuilder moduleBuilder, TypeProvider types, int counter = 0)
    {
        _moduleBuilder = moduleBuilder;
        _types = types;
        _counter = counter;
        AwaiterType = _types.TaskAwaiterOfObject;
    }

    /// <summary>
    /// Defines the complete state machine struct type with all fields and method stubs.
    /// </summary>
    /// <param name="methodName">Name of the async method (used in type name)</param>
    /// <param name="analysis">Analysis results from AsyncStateAnalyzer</param>
    /// <param name="returnType">The inner type of Task&lt;T&gt; (use typeof(object) for Task&lt;object&gt;)</param>
    /// <param name="isInstanceMethod">True if this is an instance method (needs 'this' hoisting)</param>
    /// <param name="hasAsyncArrows">True if this function contains async arrow functions (needs self-boxed field)</param>
    public void DefineStateMachine(
        string methodName,
        AsyncStateAnalyzer.AsyncFunctionAnalysis analysis,
        Type returnType,
        bool isInstanceMethod = false,
        bool hasAsyncArrows = false)
    {
        // Determine builder and task types based on return type
        if (returnType == _types.Void)
        {
            BuilderType = _types.AsyncTaskMethodBuilder;
            TaskType = _types.Task;
        }
        else
        {
            BuilderType = _types.MakeGenericType(_types.AsyncTaskMethodBuilderOpen, returnType);
            TaskType = _types.MakeGenericType(_types.TaskOpen, returnType);
        }

        // Define the state machine struct
        // Name follows C# compiler convention: <MethodName>d__N
        _stateMachineType = _moduleBuilder.DefineType(
            $"<{methodName}>d__{_counter}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.ValueType,
            [_types.IAsyncStateMachine]
        );

        // Define core fields
        DefineStateField();
        DefineBuilderField();

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

        // Define awaiter fields (one per await point)
        foreach (var awaitPoint in analysis.AwaitPoints)
        {
            var field = _stateMachineType.DefineField(
                $"<>u__{awaitPoint.StateNumber + 1}",
                AwaiterType,
                FieldAttributes.Private
            );
            AwaiterFields[awaitPoint.StateNumber] = field;
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

        // Define self-boxed field for functions with async arrows
        // This stores a reference to the boxed state machine so async arrows
        // can access the same instance (not a copy)
        if (hasAsyncArrows)
        {
            SelfBoxedField = _stateMachineType.DefineField(
                "<>__selfBoxed",
                _types.Object,
                FieldAttributes.Public
            );
        }

        // Define the IAsyncStateMachine methods
        DefineMoveNextMethod();
        DefineSetStateMachineMethod();
    }

    private void DefineStateField()
    {
        // <>1__state - tracks execution position
        // -1 = initial/running, -2 = completed, 0+ = awaiting at specific point
        StateField = _stateMachineType.DefineField(
            "<>1__state",
            _types.Int32,
            FieldAttributes.Public
        );
    }

    private void DefineBuilderField()
    {
        // <>t__builder - AsyncTaskMethodBuilder or AsyncTaskMethodBuilder<T>
        BuilderField = _stateMachineType.DefineField(
            "<>t__builder",
            BuilderType,
            FieldAttributes.Public
        );
    }

    private void DefineMoveNextMethod()
    {
        // void IAsyncStateMachine.MoveNext()
        MoveNextMethod = _stateMachineType.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            Type.EmptyTypes
        );

        // Mark as implementing IAsyncStateMachine.MoveNext
        var interfaceMethod = _types.GetMethodNoParams(_types.IAsyncStateMachine, "MoveNext");
        _stateMachineType.DefineMethodOverride(MoveNextMethod, interfaceMethod);
    }

    private void DefineSetStateMachineMethod()
    {
        // void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
        SetStateMachineMethod = _stateMachineType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.IAsyncStateMachine]
        );

        // Emit empty body (modern .NET doesn't use this for structs)
        var il = SetStateMachineMethod.GetILGenerator();
        il.Emit(OpCodes.Ret);

        // Mark as implementing IAsyncStateMachine.SetStateMachine
        var interfaceMethod = _types.GetMethod(_types.IAsyncStateMachine, "SetStateMachine", [_types.IAsyncStateMachine]);
        _stateMachineType.DefineMethodOverride(SetStateMachineMethod, interfaceMethod);
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

    #region Helper Methods for IL Emission

    /// <summary>
    /// Gets the Create method for the specific builder type.
    /// </summary>
    public MethodInfo GetBuilderCreateMethod()
    {
        return BuilderType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
    }

    /// <summary>
    /// Gets the Task property getter for the specific builder type.
    /// </summary>
    public MethodInfo GetBuilderTaskGetter()
    {
        return BuilderType.GetProperty("Task", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
    }

    /// <summary>
    /// Gets the Start method for the specific builder type.
    /// </summary>
    public MethodInfo GetBuilderStartMethod()
    {
        // Start<TStateMachine>(ref TStateMachine)
        var methods = BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var startMethod = methods.First(m => m.Name == "Start" && m.IsGenericMethod);
        return startMethod.MakeGenericMethod(_stateMachineType);
    }

    /// <summary>
    /// Gets the SetResult method for the specific builder type.
    /// </summary>
    public MethodInfo GetBuilderSetResultMethod()
    {
        if (BuilderType == _types.AsyncTaskMethodBuilder)
        {
            return BuilderType.GetMethod("SetResult", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)!;
        }
        else
        {
            var innerType = BuilderType.GetGenericArguments()[0];
            return BuilderType.GetMethod("SetResult", BindingFlags.Public | BindingFlags.Instance, null, [innerType], null)!;
        }
    }

    /// <summary>
    /// Gets the SetException method for the specific builder type.
    /// </summary>
    public MethodInfo GetBuilderSetExceptionMethod()
    {
        return BuilderType.GetMethod("SetException", BindingFlags.Public | BindingFlags.Instance)!;
    }

    /// <summary>
    /// Gets the AwaitUnsafeOnCompleted method for the specific builder type.
    /// </summary>
    public MethodInfo GetBuilderAwaitUnsafeOnCompletedMethod()
    {
        // AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter, ref TStateMachine)
        var methods = BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var awaitMethod = methods.First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod);
        return awaitMethod.MakeGenericMethod(AwaiterType, _stateMachineType);
    }

    /// <summary>
    /// Gets the IsCompleted property getter for TaskAwaiter.
    /// </summary>
    public MethodInfo GetAwaiterIsCompletedGetter()
    {
        return AwaiterType.GetProperty("IsCompleted", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
    }

    /// <summary>
    /// Gets the GetResult method for TaskAwaiter.
    /// </summary>
    public MethodInfo GetAwaiterGetResultMethod()
    {
        return AwaiterType.GetMethod("GetResult", BindingFlags.Public | BindingFlags.Instance)!;
    }

    /// <summary>
    /// Gets the GetAwaiter method for Task&lt;object&gt;.
    /// </summary>
    public MethodInfo GetTaskGetAwaiterMethod()
    {
        return _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
    }

    #endregion
}
