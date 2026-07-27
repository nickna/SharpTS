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
public class AsyncArrowStateMachineBuilder : AsyncBuilderBase
{
    private readonly ModuleBuilder _moduleBuilder;
    private readonly TypeProvider _types;
    private TypeBuilder _stateMachineType = null!;
    private int _counter;

    // The async arrow this builder is for
    public Expr.ArrowFunction Arrow { get; }

    // The type being built
    public override TypeBuilder StateMachineType => _stateMachineType;
    protected override TypeProvider Types => _types;

    // Whether this is a standalone (top-level) async arrow without an outer async function
    public bool IsStandalone { get; private set; }

    // Reference to outer state machine (for by-reference capture) - null for standalone arrows
    public FieldBuilder? OuterStateMachineField { get; private set; }
    public Type? OuterStateMachineType { get; private set; }

    // For nested arrows: the parent arrow's outer state machine info (for transitive captures)
    public FieldBuilder? ParentOuterStateMachineField { get; set; }
    public Type? GrandparentStateMachineType { get; set; }

    // Self-boxed field for sharing this state machine with nested async arrows
    public FieldBuilder? SelfBoxedField { get; private set; }

    // Dynamic `this` field for a standalone async FUNCTION EXPRESSION (HasOwnThis).
    // Unlike a true arrow (lexical this), its `this` is bound at call time via
    // fn.call/apply/bind, so it lives in its own field populated by the stub from
    // the thread-local — not in a lexical capture field. Null for true arrows.
    public FieldBuilder? OwnThisField { get; private set; }

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

    // For standalone arrows: fields in this state machine for captured variables
    // (similar to display class fields for non-async closures)
    public Dictionary<string, FieldBuilder> StandaloneCaptureFields { get; } = [];

    // For nested arrows: captures that require accessing through outer's outer reference
    // These are variables from a grandparent that the parent arrow also captured
    public HashSet<string> TransitiveCaptures { get; } = [];

    // The captures this arrow needs from outer scope
    public HashSet<string> Captures { get; }

    // Follow-up to #838: the <>__functionDC field holding this async arrow's own reference-type function
    // display class, shared with a nested SYNC arrow that writes one of the arrow's captured locals. Null
    // unless RegisterAsyncArrowFunctionDisplayClasses registered a DC for this arrow. The ctor is stored so
    // the stub can instantiate it once on entry.
    public FieldBuilder? FunctionDCField { get; private set; }
    public ConstructorBuilder? FunctionDCCtor { get; private set; }
    // The DC's (storage name → field) map, so this arrow's own MoveNext routes a DC-resident local's
    // read/write through `this.<>__functionDC.field`. Kept on the builder rather than the context so it
    // never collides with the OuterFunctionDCField relay's use of ctx.FunctionDisplayClassFields.
    public Dictionary<string, FieldBuilder> FunctionDCFieldMap { get; } = [];

    // Methods
    public MethodBuilder MoveNextMethod { get; private set; } = null!;
    public MethodBuilder SetStateMachineMethod { get; private set; } = null!;

    // The stub method that returns Task<object>
    public MethodBuilder StubMethod { get; private set; } = null!;

    // BuilderType and AwaiterType live in AsyncBuilderBase (#1125).

    public AsyncArrowStateMachineBuilder(
        ModuleBuilder moduleBuilder,
        TypeProvider types,
        Expr.ArrowFunction arrow,
        HashSet<string> captures,
        int counter = 0)
    {
        _moduleBuilder = moduleBuilder;
        _types = types;
        Arrow = arrow;
        Captures = captures;
        _counter = counter;
        BuilderType = _types.AsyncTaskMethodBuilderOfObject;
        AwaiterType = _types.TaskAwaiterOfObject;
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
            _types.ValueType,
            [_types.IAsyncStateMachine]
        );

        // Add outer reference field (stores reference to outer state machine)
        // We use object type and cast as needed, since the outer type might not be created yet
        OuterStateMachineField = _stateMachineType.DefineField(
            "<>__outer",
            _types.Object,
            FieldAttributes.Public
        );

        // Add self-boxed field if this arrow has nested async arrows
        if (hasNestedAsyncArrows)
        {
            SelfBoxedField = _stateMachineType.DefineField(
                "<>__selfBoxed",
                _types.Object,
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
            _types.Int32,
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
                _types.Object,
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
                _types.Object,
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
    /// Defines a standalone state machine for top-level async arrows (not inside async functions).
    /// These don't have an outer state machine reference.
    /// </summary>
    /// <param name="awaitCount">Number of await points in this arrow</param>
    /// <param name="arrowParameters">Parameters of this arrow function</param>
    /// <param name="hoistedLocals">Local variables that need hoisting for this arrow's awaits</param>
    public void DefineStateMachineStandalone(
        int awaitCount,
        List<Stmt.Parameter> arrowParameters,
        HashSet<string> hoistedLocals)
    {
        IsStandalone = true;
        OuterStateMachineType = null;
        OuterStateMachineField = null;

        // Define the state machine struct
        _stateMachineType = _moduleBuilder.DefineType(
            $"<>c__AsyncArrow_{_counter}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.ValueType,
            [_types.IAsyncStateMachine]
        );

        // No outer reference field for standalone arrows. A nested async-arrow expression inside a
        // standalone arrow is emitted as an independent TSFunction over its own stub (see
        // EmitNestedAsyncArrow), so no self-boxed field is needed here. (#615)

        // Define core fields
        StateField = _stateMachineType.DefineField(
            "<>1__state",
            _types.Int32,
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
                _types.Object,
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
                _types.Object,
                FieldAttributes.Public
            );
            LocalFields[localName] = field;
        }

        // Async function expressions bind `this` dynamically at call time, so give
        // them a dedicated field the stub fills from the thread-local receiver
        // (see DefineStubMethod). True arrows capture `this` lexically instead.
        if (Arrow.HasOwnThis)
        {
            OwnThisField = _stateMachineType.DefineField(
                "<>4__this",
                _types.Object,
                FieldAttributes.Public
            );
        }

        // Define capture fields for variables from the enclosing (non-async) function
        // These will be passed to the stub method and stored in the state machine
        foreach (var captureName in Captures)
        {
            // Skip parameters and hoisted locals (already have fields)
            if (ParameterFields.ContainsKey(captureName) || LocalFields.ContainsKey(captureName))
                continue;

            var field = _stateMachineType.DefineField(
                $"<>captured_{captureName}",
                _types.Object,
                FieldAttributes.Public
            );
            StandaloneCaptureFields[captureName] = field;
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
    /// Follow-up to #838: adds the <c>&lt;&gt;__functionDC</c> field that holds this async arrow's own
    /// reference-type function display class (shared with a nested sync arrow that writes one of the
    /// arrow's captured locals). Must be called after <see cref="DefineStateMachine"/> /
    /// <see cref="DefineStateMachineStandalone"/> (the state-machine type must exist) and before
    /// <c>CreateType</c>. The stub instantiates it once on entry; the ctor is stored here for that.
    /// </summary>
    public void DefineFunctionDisplayClassField(Type dcType, ConstructorBuilder dcCtor,
        IReadOnlyDictionary<string, FieldBuilder> dcFields)
    {
        FunctionDCField = _stateMachineType.DefineField("<>__functionDC", dcType, FieldAttributes.Public);
        FunctionDCCtor = dcCtor;
        foreach (var (name, field) in dcFields)
            FunctionDCFieldMap[name] = field;
    }

    /// <summary>
    /// Defines and emits the stub method that creates the state machine when the arrow is invoked.
    /// The stub takes (outer state machine boxed, params...) and returns Task&lt;object&gt;.
    /// For standalone arrows, there's no outer SM parameter but captures are passed.
    /// </summary>
    public void DefineStubMethod(TypeBuilder programType, EmittedRuntime? runtime = null)
    {
        // Build parameter types list
        var paramTypes = new List<Type>();

        // For non-standalone arrows, first parameter is the outer state machine (boxed)
        if (!IsStandalone)
        {
            paramTypes.Add(_types.Object); // Outer SM
        }

        // For standalone arrows with captures, a SINGLE leading object arg carries ALL captured
        // values packed into an object[] (passed by the call site as the $TSFunction "target").
        // $TSFunction prepends its target as one leading argument, so one slot must hold every
        // capture regardless of count — the stub unpacks the array into its fields below. Using
        // one slot for any capture count is what makes multi-capture work (#684); a per-capture
        // arg only ever lined up when there was exactly one (#641).
        // Ordinal ordering must match the call sites (ILEmitter / AsyncArrowMoveNextEmitter) and
        // the unpack loop below.
        var captureOrder = StandaloneCaptureFields.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList();
        bool hasStandaloneCaptures = IsStandalone && captureOrder.Count > 0;
        if (hasStandaloneCaptures)
        {
            paramTypes.Add(_types.Object); // object[] of captured values (the single target slot)
        }

        // Add arrow parameters
        foreach (var _ in ParameterOrder)
        {
            paramTypes.Add(_types.Object); // All arrow params are object
        }

        StubMethod = programType.DefineMethod(
            $"<>AsyncArrow_{_counter}_Stub",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.TaskOfObject,
            [.. paramTypes]
        );

        var il = StubMethod.GetILGenerator();
        var smLocal = il.DeclareLocal(_stateMachineType);

        // var sm = default(StateMachine);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, _stateMachineType);

        // For non-standalone: sm.<>__outer = arg0 (outer state machine boxed)
        int paramOffset = 0;
        if (!IsStandalone && OuterStateMachineField != null)
        {
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stfld, OuterStateMachineField);
            paramOffset = 1; // Skip outer SM arg when copying params
        }

        // For standalone arrows with captures, unpack the leading object[] (arg0) into the
        // state-machine capture fields: sm.<>captured_x = ((object[])arg0)[i]. The ordinal
        // capture order matches DefineStateMachineStandalone and the call sites.
        if (hasStandaloneCaptures)
        {
            for (int i = 0; i < captureOrder.Count; i++)
            {
                var captureField = StandaloneCaptureFields[captureOrder[i]];
                il.Emit(OpCodes.Ldloca, smLocal);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, _types.ObjectArray);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Stfld, captureField);
            }
            paramOffset = 1; // Skip the single capture-array arg when copying params
        }

        // Async function expressions (HasOwnThis) have a DYNAMIC `this` bound at
        // call time — `fn.call(receiver)`, `fn.apply(...)`, `fn.bind(...)` — not a
        // lexically-captured one. $TSFunction.InvokeWithThis stashes that receiver
        // in the thread-local `_currentFunctionThis` before dispatching here, so
        // snapshot it into the dedicated OwnThisField. Snapshotting at stub entry
        // (synchronously, before any await) is what makes it survive state-machine
        // suspension/resume on another thread. A null thread-local means a plain
        // call → sloppy `this` (globalThis sentinel), matching
        // LocalVariableResolver.LoadThis.
        if (IsStandalone && Arrow.HasOwnThis && runtime != null && OwnThisField != null)
        {
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldsfld, runtime.CurrentFunctionThisField);
            var thisNotNull = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, thisNotNull);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
            il.MarkLabel(thisNotNull);
            il.Emit(OpCodes.Stfld, OwnThisField);
        }

        // Copy parameters to state machine fields (in order!)
        for (int i = 0; i < ParameterOrder.Count; i++)
        {
            var paramName = ParameterOrder[i];
            var paramField = ParameterFields[paramName];
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldarg, i + paramOffset);
            il.Emit(OpCodes.Stfld, paramField);
        }

        // (Follow-up to #838: this arrow's own function DC is instantiated in the MoveNext prologue, not
        // here — the DC field is attached only in Phase 5/6, after this stub is defined in Phase 4 for a
        // nested arrow. The prologue null-guards so it runs exactly once on initial entry.)

        // sm.<>t__builder = AsyncTaskMethodBuilder<object>.Create();
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Call, GetBuilderCreateMethod());
        il.Emit(OpCodes.Stfld, BuilderField);

        // sm.<>1__state = -1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, StateField);

        // If this arrow has nested async arrows, box and store self reference before Start
        // so the nested arrows capture the one shared instance. Box once and run the box via
        // a verifiable ref (see helper for the #414 fix).
        if (SelfBoxedField != null)
        {
            StateMachineEmitHelpers.EmitSelfBoxedStartAndReturnTask(
                il,
                smLocal,
                _stateMachineType,
                SelfBoxedField,
                BuilderField,
                GetBuilderStartMethod(),
                GetBuilderTaskGetter(),
                _types);
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
            _types.Void,
            Type.EmptyTypes
        );

        var interfaceMethod = _types.GetMethodNoParams(_types.IAsyncStateMachine, "MoveNext");
        _stateMachineType.DefineMethodOverride(MoveNextMethod, interfaceMethod);
    }

    private void DefineSetStateMachineMethod()
    {
        SetStateMachineMethod = _stateMachineType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.IAsyncStateMachine]
        );

        // Emit empty body
        var il = SetStateMachineMethod.GetILGenerator();
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.GetMethod(_types.IAsyncStateMachine, "SetStateMachine", [_types.IAsyncStateMachine]);
        _stateMachineType.DefineMethodOverride(SetStateMachineMethod, interfaceMethod);
    }

    /// <summary>
    /// Gets a field for a variable by name, checking parameters, locals, and captures.
    /// </summary>
    public override FieldBuilder? GetVariableField(string name)
    {
        if (ParameterFields.TryGetValue(name, out var paramField))
            return paramField;
        if (LocalFields.TryGetValue(name, out var localField))
            return localField;
        if (StandaloneCaptureFields.TryGetValue(name, out var captureField))
            return captureField;
        return null;
    }

    /// <summary>
    /// Checks if a variable is from the outer state machine (captured).
    /// For standalone arrows, this returns false as captures are stored locally.
    /// </summary>
    public bool IsCaptured(string name)
    {
        return CapturedFieldMap.ContainsKey(name) || (name == "this" && Captures.Contains("this"));
    }

    // CreateType and the common builder accessors (Create/Task/Start/SetException/
    // AwaitUnsafeOnCompleted) live in AsyncBuilderBase; only SetResult differs per builder.

    #region Helper Methods for IL Emission

    /// <summary>
    /// Gets the SetResult method for the specific builder type. Specialized here (not in
    /// AsyncBuilderBase): the arrow builder is always the generic AsyncTaskMethodBuilder&lt;object&gt;,
    /// whose SetResult takes the result value.
    /// </summary>
    public MethodInfo GetBuilderSetResultMethod()
    {
        var innerType = BuilderType.GetGenericArguments()[0];
        return BuilderType.GetMethod("SetResult", BindingFlags.Public | BindingFlags.Instance, null, [innerType], null)!;
    }

    // GetAwaiterIsCompletedGetter / GetAwaiterGetResultMethod / GetTaskGetAwaiterMethod moved to the
    // shared AsyncBuilderBase (#1125): byte-identical with AsyncStateMachineBuilder.

    #endregion
}
