using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Builds a state machine struct for an async arrow function.
/// Similar to AsyncStateMachineBuilder but includes a reference to the outer
/// state machine for by-reference capture semantics.
/// </summary>
public class AsyncArrowStateMachineBuilder
{
    private readonly ModuleBuilder _moduleBuilder;
    private TypeBuilder _stateMachineType = null!;
    private int _counter;

    // The async arrow this builder is for
    public Expr.ArrowFunction Arrow { get; }

    // The type being built
    public TypeBuilder StateMachineType => _stateMachineType;

    // Reference to outer state machine (for by-reference capture)
    public FieldBuilder OuterStateMachineField { get; private set; } = null!;
    public Type OuterStateMachineType { get; private set; } = null!;

    // For nested arrows: the parent arrow's outer state machine info (for transitive captures)
    public FieldBuilder? ParentOuterStateMachineField { get; set; }
    public Type? GrandparentStateMachineType { get; set; }

    // Self-boxed field for sharing this state machine with nested async arrows
    public FieldBuilder? SelfBoxedField { get; private set; }

    // Core state machine fields
    public FieldBuilder StateField { get; private set; } = null!;
    public FieldBuilder BuilderField { get; private set; } = null!;

    // Awaiter fields (one per await point in the arrow)
    public Dictionary<int, FieldBuilder> AwaiterFields { get; } = [];

    // Parameter fields (arrow parameters need to be hoisted to the state machine)
    public Dictionary<string, FieldBuilder> ParameterFields { get; } = [];

    // Parameter names in order (for stub method parameter mapping)
    public List<string> ParameterOrder { get; } = [];

    // Local variable fields hoisted for this arrow's own awaits
    public Dictionary<string, FieldBuilder> LocalFields { get; } = [];

    // Maps captured var names to their fields in the outer state machine
    public Dictionary<string, FieldBuilder> CapturedFieldMap { get; } = [];

    // For nested arrows: captures that require accessing through outer's outer reference
    // These are variables from a grandparent that the parent arrow also captured
    public HashSet<string> TransitiveCaptures { get; } = [];

    // The captures this arrow needs from outer scope
    public HashSet<string> Captures { get; }

    // Methods
    public MethodBuilder MoveNextMethod { get; private set; } = null!;
    public MethodBuilder SetStateMachineMethod { get; private set; } = null!;

    // The stub method that returns Task<object>
    public MethodBuilder StubMethod { get; private set; } = null!;

    // Builder type
    public Type BuilderType { get; private set; } = typeof(AsyncTaskMethodBuilder<object>);
    public Type TaskType { get; private set; } = typeof(Task<object>);
    public Type AwaiterType { get; private set; } = typeof(TaskAwaiter<object>);

    public AsyncArrowStateMachineBuilder(
        ModuleBuilder moduleBuilder,
        Expr.ArrowFunction arrow,
        HashSet<string> captures,
        int counter = 0)
    {
        _moduleBuilder = moduleBuilder;
        Arrow = arrow;
        Captures = captures;
        _counter = counter;
    }

    /// <summary>
    /// Defines the complete state machine struct type with all fields and method stubs.
    /// </summary>
    /// <param name="outerStateMachineType">The outer async function's state machine type</param>
    /// <param name="outerHoistedFields">Fields in the outer state machine that we can capture from</param>
    /// <param name="awaitCount">Number of await points in this arrow</param>
    /// <param name="arrowParameters">Parameters of this arrow function</param>
    /// <param name="hoistedLocals">Local variables that need hoisting for this arrow's awaits</param>
    /// <param name="transitiveCaptures">Names of variables that parent arrow captured from its outer (need extra indirection)</param>
    /// <param name="parentOuterField">Parent arrow's outer reference field (for transitive captures)</param>
    /// <param name="grandparentType">Type of grandparent state machine (for transitive captures)</param>
    /// <param name="hasNestedAsyncArrows">True if this arrow contains nested async arrows</param>
    public void DefineStateMachine(
        Type outerStateMachineType,
        Dictionary<string, FieldBuilder> outerHoistedFields,
        int awaitCount,
        List<Stmt.Parameter> arrowParameters,
        HashSet<string> hoistedLocals,
        HashSet<string>? transitiveCaptures = null,
        FieldBuilder? parentOuterField = null,
        Type? grandparentType = null,
        bool hasNestedAsyncArrows = false)
    {
        OuterStateMachineType = outerStateMachineType;
        ParentOuterStateMachineField = parentOuterField;
        GrandparentStateMachineType = grandparentType;

        // Define the state machine struct
        _stateMachineType = _moduleBuilder.DefineType(
            $"<>c__AsyncArrow_{_counter}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]
        );

        // Add outer reference field (stores reference to outer state machine)
        // We use object type and cast as needed, since the outer type might not be created yet
        OuterStateMachineField = _stateMachineType.DefineField(
            "<>__outer",
            typeof(object),
            FieldAttributes.Public
        );

        // Add self-boxed field if this arrow has nested async arrows
        if (hasNestedAsyncArrows)
        {
            SelfBoxedField = _stateMachineType.DefineField(
                "<>__selfBoxed",
                typeof(object),
                FieldAttributes.Public
            );
        }

        // Map captures to outer fields and track transitive captures
        foreach (var capture in Captures)
        {
            if (capture != "this" && outerHoistedFields.TryGetValue(capture, out var field))
            {
                CapturedFieldMap[capture] = field;
                // Mark as transitive if it's in the parent's captured fields (not its own locals/params)
                if (transitiveCaptures?.Contains(capture) == true)
                {
                    TransitiveCaptures.Add(capture);
                }
            }
        }

        // Define core fields
        StateField = _stateMachineType.DefineField(
            "<>1__state",
            typeof(int),
            FieldAttributes.Public
        );

        BuilderField = _stateMachineType.DefineField(
            "<>t__builder",
            BuilderType,
            FieldAttributes.Public
        );

        // Define parameter fields (arrow parameters become state machine fields)
        foreach (var param in arrowParameters)
        {
            var field = _stateMachineType.DefineField(
                param.Name.Lexeme,
                typeof(object),
                FieldAttributes.Public
            );
            ParameterFields[param.Name.Lexeme] = field;
            ParameterOrder.Add(param.Name.Lexeme);
        }

        // Define local fields for variables that span await points
        foreach (var localName in hoistedLocals)
        {
            var field = _stateMachineType.DefineField(
                localName,
                typeof(object),
                FieldAttributes.Public
            );
            LocalFields[localName] = field;
        }

        // Define awaiter fields
        for (int i = 0; i < awaitCount; i++)
        {
            var field = _stateMachineType.DefineField(
                $"<>u__{i + 1}",
                AwaiterType,
                FieldAttributes.Private
            );
            AwaiterFields[i] = field;
        }

        // Define the IAsyncStateMachine methods
        DefineMoveNextMethod();
        DefineSetStateMachineMethod();
    }

    /// <summary>
    /// Defines and emits the stub method that creates the state machine when the arrow is invoked.
    /// The stub takes (outer state machine boxed, params...) and returns Task&lt;object&gt;.
    /// </summary>
    public void DefineStubMethod(TypeBuilder programType)
    {
        // First parameter is the outer state machine (boxed), rest are arrow parameters
        var paramTypes = new List<Type> { typeof(object) }; // Outer SM
        foreach (var _ in ParameterOrder)
        {
            paramTypes.Add(typeof(object)); // All arrow params are object
        }

        StubMethod = programType.DefineMethod(
            $"<>AsyncArrow_{_counter}_Stub",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(Task<object>),
            [.. paramTypes]
        );

        var il = StubMethod.GetILGenerator();
        var smLocal = il.DeclareLocal(_stateMachineType);

        // var sm = default(StateMachine);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, _stateMachineType);

        // sm.<>__outer = arg0 (outer state machine boxed)
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, OuterStateMachineField);

        // Copy parameters to state machine fields (in order!)
        for (int i = 0; i < ParameterOrder.Count; i++)
        {
            var paramName = ParameterOrder[i];
            var paramField = ParameterFields[paramName];
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldarg, i + 1); // +1 to skip outer SM arg
            il.Emit(OpCodes.Stfld, paramField);
        }

        // sm.<>t__builder = AsyncTaskMethodBuilder<object>.Create();
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Call, GetBuilderCreateMethod());
        il.Emit(OpCodes.Stfld, BuilderField);

        // sm.<>1__state = -1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, StateField);

        // If this arrow has nested async arrows, box and store self reference before Start
        if (SelfBoxedField != null)
        {
            // Box the state machine
            il.Emit(OpCodes.Ldloc, smLocal);
            il.Emit(OpCodes.Box, _stateMachineType);
            var boxedLocal = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Stloc, boxedLocal);

            // Store in sm.<>__selfBoxed (access through the boxed reference since we already boxed)
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, _stateMachineType);
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Stfld, SelfBoxedField);

            // Now call Start on the boxed instance's builder
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, _stateMachineType);
            il.Emit(OpCodes.Ldflda, BuilderField);
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, _stateMachineType);
            il.Emit(OpCodes.Call, GetBuilderStartMethod());

            // return builder.Task from boxed instance
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, _stateMachineType);
            il.Emit(OpCodes.Ldflda, BuilderField);
            il.Emit(OpCodes.Call, GetBuilderTaskGetter());
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // sm.<>t__builder.Start(ref sm);
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldflda, BuilderField);
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Call, GetBuilderStartMethod());

            // return sm.<>t__builder.Task;
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldflda, BuilderField);
            il.Emit(OpCodes.Call, GetBuilderTaskGetter());
            il.Emit(OpCodes.Ret);
        }
    }

    private void DefineMoveNextMethod()
    {
        MoveNextMethod = _stateMachineType.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes
        );

        var interfaceMethod = typeof(IAsyncStateMachine).GetMethod("MoveNext")!;
        _stateMachineType.DefineMethodOverride(MoveNextMethod, interfaceMethod);
    }

    private void DefineSetStateMachineMethod()
    {
        SetStateMachineMethod = _stateMachineType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(IAsyncStateMachine)]
        );

        // Emit empty body
        var il = SetStateMachineMethod.GetILGenerator();
        il.Emit(OpCodes.Ret);

        var interfaceMethod = typeof(IAsyncStateMachine).GetMethod("SetStateMachine")!;
        _stateMachineType.DefineMethodOverride(SetStateMachineMethod, interfaceMethod);
    }

    /// <summary>
    /// Gets a field for a variable by name, checking parameters, locals, and captures.
    /// </summary>
    public FieldBuilder? GetVariableField(string name)
    {
        if (ParameterFields.TryGetValue(name, out var paramField))
            return paramField;
        if (LocalFields.TryGetValue(name, out var localField))
            return localField;
        return null;
    }

    /// <summary>
    /// Checks if a variable is from the outer state machine (captured).
    /// </summary>
    public bool IsCaptured(string name)
    {
        return CapturedFieldMap.ContainsKey(name) || (name == "this" && Captures.Contains("this"));
    }

    /// <summary>
    /// Finalizes the type after MoveNext body has been emitted.
    /// </summary>
    public Type CreateType()
    {
        return _stateMachineType.CreateType()!;
    }

    #region Helper Methods for IL Emission

    public MethodInfo GetBuilderCreateMethod()
    {
        return BuilderType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
    }

    public MethodInfo GetBuilderTaskGetter()
    {
        return BuilderType.GetProperty("Task", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
    }

    public MethodInfo GetBuilderStartMethod()
    {
        var methods = BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var startMethod = methods.First(m => m.Name == "Start" && m.IsGenericMethod);
        return startMethod.MakeGenericMethod(_stateMachineType);
    }

    public MethodInfo GetBuilderSetResultMethod()
    {
        var innerType = BuilderType.GetGenericArguments()[0];
        return BuilderType.GetMethod("SetResult", BindingFlags.Public | BindingFlags.Instance, null, [innerType], null)!;
    }

    public MethodInfo GetBuilderSetExceptionMethod()
    {
        return BuilderType.GetMethod("SetException", BindingFlags.Public | BindingFlags.Instance)!;
    }

    public MethodInfo GetBuilderAwaitUnsafeOnCompletedMethod()
    {
        var methods = BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var awaitMethod = methods.First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod);
        return awaitMethod.MakeGenericMethod(AwaiterType, _stateMachineType);
    }

    public MethodInfo GetAwaiterIsCompletedGetter()
    {
        return AwaiterType.GetProperty("IsCompleted", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
    }

    public MethodInfo GetAwaiterGetResultMethod()
    {
        return AwaiterType.GetMethod("GetResult", BindingFlags.Public | BindingFlags.Instance)!;
    }

    public MethodInfo GetTaskGetAwaiterMethod()
    {
        return typeof(Task<object>).GetMethod("GetAwaiter", BindingFlags.Public | BindingFlags.Instance)!;
    }

    #endregion
}
