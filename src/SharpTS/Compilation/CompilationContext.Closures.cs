using System.Reflection.Emit;
using SharpTS.Parsing;
using static SharpTS.Parsing.Expr;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Closure Support
    // ============================================

    // Closure analyzer for detecting captured variables
    public ClosureAnalyzer? ClosureAnalyzer { get; set; }

    // Arrow function methods (arrow node -> method info)
    public Dictionary<ArrowFunction, MethodBuilder> ArrowMethods { get; set; } = [];

    // Boxed-callback adapters for annotated-param array HOF callbacks (#861).
    // Lazily created (no constructor plumbing needed); emits per-arrow
    // object(object[,object]) adapters on $Program so a typed arrow can bind to
    // the Func<object,…> the Array*Direct helpers expect.
    private ArrowBoxedAdapterEmitter? _arrowBoxedAdapters;
    internal ArrowBoxedAdapterEmitter ArrowBoxedAdapters => _arrowBoxedAdapters ??= new ArrowBoxedAdapterEmitter();

    // Module-scope const → literal-arrow bindings. Iterator-helper fast paths
    // look up `Expr.Variable` callbacks here so `const sq = x => x*x; arr.map(sq)`
    // gets the same direct-delegate dispatch as the inline-arrow form.
    public Dictionary<string, ArrowFunction> ConstArrowBindings { get; set; } = [];

    // Non-escaping `const NAME = (args) => …` local bindings (#858). Populated by
    // NonEscapingArrowLocalAnalyzer. EmitVarDeclaration stores the bare display instance in a typed
    // local for these (capturing arrows only), and the function-value call fast path emits a direct
    // `callvirt Invoke` instead of the per-call $TSFunction wrapper + reflective InvokeMethodValue.
    public Dictionary<string, ArrowFunction> DirectCallArrowBindings { get; set; } = [];

    // Generated value-type "shape" structs for promoted object-literal locals (#862). Shared
    // program-wide (populated by DefineObjectShapeTypes after analysis). EmitVarDeclaration resolves the
    // struct by canonical shape key for a promoted local; the property get/set fast paths recognise a
    // promoted local from its slot's CLR type via ByClrType. See TryGetObjectShapeType /
    // TryGetPromotedObjectLocal.
    public ObjectShapeRegistry? ObjectShapes { get; set; }

    // ============================================
    // Self-referential capture write-back (issue #421)
    // ============================================

    /// <summary>
    /// Name of the variable currently being declared whose initializer may
    /// create a closure capturing that same variable (e.g.
    /// <c>const s = make(() =&gt; s)</c>). Set by the variable-declaration emitter
    /// around the initializer, consulted by the arrow display-class populator.
    /// Null when not emitting a self-capturable declaration initializer.
    /// </summary>
    public string? SelfCaptureVarName { get; set; }

    /// <summary>
    /// Display-class instances + fields that captured <see cref="SelfCaptureVarName"/>
    /// during the current initializer. Each closure populates its field with a
    /// SNAPSHOT of the local taken before the assignment (so it sees the stale
    /// value); the declaration emitter writes the freshly-assigned value back into
    /// these fields after the store. Preserves per-iteration fresh-binding because
    /// each iteration creates a distinct display-class instance.
    /// </summary>
    public List<(LocalBuilder DcInstance, FieldBuilder Field)>? SelfCaptureWriteBacks { get; set; }

    // Display classes for capturing closures (arrow node -> type builder)
    public Dictionary<ArrowFunction, TypeBuilder> DisplayClasses { get; set; } = [];

    // Display class fields (arrow node -> field mapping)
    public Dictionary<ArrowFunction, Dictionary<string, FieldBuilder>> DisplayClassFields { get; set; } = [];

    // Display class constructors (arrow node -> constructor)
    public Dictionary<ArrowFunction, ConstructorBuilder> DisplayClassConstructors { get; set; } = [];

    // For capturing closures: current display class instance local
    public LocalBuilder? DisplayClassLocal { get; set; }

    // For capturing closures: field mapping (variable name -> field)
    public Dictionary<string, FieldBuilder>? CapturedFields { get; set; }

    // ============================================
    // Entry-Point Display Class (for captured top-level variables)
    // ============================================

    /// <summary>
    /// Display class type for entry-point captured variables.
    /// When top-level variables are captured by closures, they're stored here
    /// instead of static fields, so modifications in closures are visible to outer code.
    /// </summary>
    public TypeBuilder? EntryPointDisplayClass { get; set; }

    /// <summary>
    /// Constructor for the entry-point display class.
    /// </summary>
    public ConstructorBuilder? EntryPointDisplayClassCtor { get; set; }

    /// <summary>
    /// Fields in the entry-point display class (variable name -> field).
    /// </summary>
    public Dictionary<string, FieldBuilder>? EntryPointDisplayClassFields { get; set; }

    /// <summary>
    /// Initialization flags for visible top-level let/const bindings. A false
    /// flag means the binding is still in its temporal dead zone.
    /// </summary>
    public Dictionary<string, FieldBuilder>? TopLevelLexicalInitFields { get; set; }

    /// <summary>
    /// Emits a temporal-dead-zone check for a top-level lexical binding, if
    /// <paramref name="name"/> has an initialization flag in this module.
    /// The check is stack-neutral, so callers may use it after staging an
    /// assignment value as well as before a read.
    /// </summary>
    internal bool EmitTopLevelLexicalTdzCheck(ILGenerator il, string name)
    {
        if (TopLevelLexicalInitFields?.TryGetValue(name, out var initField) != true || initField == null)
            return false;

        if (!EmitTopLevelLexicalInitFlagLoad(il, initField))
            return false;

        var initialized = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, initialized);
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Call, Runtime!.ThrowUndefinedVariable);
        il.MarkLabel(initialized);
        return true;
    }

    /// <summary>Marks a top-level let/const binding initialized after its declaration stores.</summary>
    internal bool EmitMarkTopLevelLexicalInitialized(ILGenerator il, string name)
    {
        if (TopLevelLexicalInitFields?.TryGetValue(name, out var initField) != true || initField == null)
            return false;

        if (initField.IsStatic)
        {
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stsfld, initField);
            return true;
        }

        if (!EmitEntryPointDisplayClassLoad(il))
            return false;

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, initField);
        return true;
    }

    private bool EmitTopLevelLexicalInitFlagLoad(ILGenerator il, FieldBuilder initField)
    {
        if (initField.IsStatic)
        {
            il.Emit(OpCodes.Ldsfld, initField);
            return true;
        }

        if (!EmitEntryPointDisplayClassLoad(il))
            return false;

        il.Emit(OpCodes.Ldfld, initField);
        return true;
    }

    private bool EmitEntryPointDisplayClassLoad(ILGenerator il)
    {
        if (EntryPointDisplayClassLocal != null)
        {
            il.Emit(OpCodes.Ldloc, EntryPointDisplayClassLocal);
            return true;
        }

        if (CurrentArrowEntryPointDCField != null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, CurrentArrowEntryPointDCField);
            return true;
        }

        if (EntryPointDisplayClassStaticField != null)
        {
            il.Emit(OpCodes.Ldsfld, EntryPointDisplayClassStaticField);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Local variable holding the entry-point display class instance.
    /// Used in entry point methods for direct access.
    /// </summary>
    public LocalBuilder? EntryPointDisplayClassLocal { get; set; }

    /// <summary>
    /// Static field on $Program that holds the entry-point display class instance.
    /// Used by module init methods to access captured top-level variables.
    /// </summary>
    public FieldBuilder? EntryPointDisplayClassStaticField { get; set; }

    /// <summary>
    /// Set of top-level variable names that are captured by closures.
    /// These use the entry-point display class instead of static fields.
    /// </summary>
    public HashSet<string>? CapturedTopLevelVars { get; set; }

    /// <summary>
    /// #1201: names of top-level BLOCK-scoped let/const bindings lifted onto the entry-point
    /// display class for this module (subset of <see cref="CapturedTopLevelVars"/>). The
    /// declaration emitter routes these to their DC field even though they sit in a nested
    /// scope — every lifted name is declared exactly once in the module, so the name-keyed
    /// check cannot collide with a shadowing declaration.
    /// </summary>
    public HashSet<string>? LiftedBlockScopedTopLevelVars { get; set; }

    /// <summary>
    /// Maps arrow functions to their $entryPointDC field (if they capture top-level vars).
    /// Used when creating capturing arrows to populate the reference to the entry-point display class.
    /// </summary>
    public Dictionary<ArrowFunction, FieldBuilder>? ArrowEntryPointDCFields { get; set; }

    /// <summary>
    /// When inside an arrow body, this is the field that holds the reference to the entry-point display class.
    /// Used for accessing captured top-level variables through the $entryPointDC field.
    /// </summary>
    public FieldBuilder? CurrentArrowEntryPointDCField { get; set; }

    // ============================================
    // Function-Level Display Class (for captured function-local variables)
    // ============================================

    /// <summary>
    /// Local variable holding the current function's display class instance.
    /// Used when function-local variables are captured by inner closures.
    /// </summary>
    public LocalBuilder? FunctionDisplayClassLocal { get; set; }

    /// <summary>
    /// Fields in the current function's display class (variable name -> field).
    /// These are local variables that are captured by inner arrow functions.
    /// </summary>
    public Dictionary<string, FieldBuilder>? FunctionDisplayClassFields { get; set; }

    /// <summary>
    /// Set of variable names that are stored in the function display class.
    /// Used to redirect local variable access to display class fields.
    /// </summary>
    public HashSet<string>? CapturedFunctionLocals { get; set; }

    /// <summary>
    /// Maps arrow functions to their $functionDC field (if they capture function-level vars).
    /// Used when creating capturing arrows to populate the reference to the function display class.
    /// </summary>
    public Dictionary<ArrowFunction, FieldBuilder>? ArrowFunctionDCFields { get; set; }

    /// <summary>
    /// When inside an arrow body, this is the field that holds the reference to the function display class.
    /// Used for accessing captured function-level variables through the $functionDC field.
    /// </summary>
    public FieldBuilder? CurrentArrowFunctionDCField { get; set; }

    /// <summary>
    /// When inside an arrow body, maps a captured variable's source name to the renamed storage key of
    /// its function-display-class field (#838). A nested-block <c>let</c>/<c>const</c> that shadows an
    /// enclosing binding and is WRITTEN by this arrow is lifted into the shared <c>$functionDC</c> under a
    /// disambiguated storage name (<c>&lt;r&gt;__bsN</c>), while the outer same-named binding keeps the
    /// field <c>r</c>. Without this remap the arrow's <c>r</c> would resolve to the outer field and the
    /// two bindings would collide. <see cref="LocalVariableResolver"/> maps the name through this table
    /// before the <see cref="CapturedFunctionLocals"/> / <see cref="FunctionDisplayClassFields"/> lookup.
    /// Null when the arrow captures no renamed write-shadow (the common case).
    /// </summary>
    public Dictionary<string, string>? CurrentArrowFunctionDCFieldRenames { get; set; }

    /// <summary>
    /// Maps a captured variable's source name to the key under which its function-display-class field is
    /// registered (#838). In an arrow body this redirects a write-captured block-scope shadow to its
    /// renamed storage (<c>&lt;r&gt;__bsN</c>) so reads and writes reach the shadow's field instead of the
    /// outer same-named binding's. Returns <paramref name="name"/> unchanged when no remap applies (the
    /// common case). Consulted by every function-DC load/store site — <see cref="LocalVariableResolver"/>
    /// and the hand-rolled assignment / increment / declaration paths in <c>ILEmitter</c>.
    /// </summary>
    public string ResolveFunctionDCFieldName(string name) =>
        CurrentArrowFunctionDCFieldRenames is { } m && m.TryGetValue(name, out var storage) ? storage : name;

    /// <summary>
    /// For an async arrow's MoveNext: the <c>&lt;&gt;__functionDC</c> field on the enclosing async
    /// function's state machine. Combined with <see cref="FunctionDisplayClassFields"/>, it lets the
    /// arrow read/write captured locals that were promoted into the (reference-type) function display
    /// class — `outer.functionDC.field` — instead of mutating a field of the boxed value-type state
    /// machine in place, which is unverifiable (`unbox` yields a readonly managed pointer; #625).
    /// </summary>
    public FieldBuilder? OuterFunctionDCField { get; set; }

    // ============================================
    // Arrow Scope Display Class (for arrow-local vars captured by nested arrows)
    // ============================================

    /// <summary>
    /// Local variable holding the current arrow's scope display class instance.
    /// Used when arrow-local variables are captured by inner closures.
    /// </summary>
    public LocalBuilder? ArrowScopeDisplayClassLocal { get; set; }

    /// <summary>
    /// Fields in the current arrow's scope display class (variable name -> field).
    /// These are local variables that are captured by inner arrow functions.
    /// </summary>
    public Dictionary<string, FieldBuilder>? ArrowScopeDisplayClassFields { get; set; }

    /// <summary>
    /// Set of variable names that are stored in the arrow scope display class.
    /// Used to redirect local variable access to display class fields.
    /// </summary>
    public HashSet<string>? CapturedArrowLocals { get; set; }

    /// <summary>
    /// Maps arrow functions to their $arrowDC field (if they capture arrow-scope vars).
    /// Used when creating capturing arrows to populate the reference to the arrow scope display class.
    /// </summary>
    public Dictionary<ArrowFunction, FieldBuilder>? ArrowScopeDCFields { get; set; }

    /// <summary>
    /// When inside a nested arrow body, this is the field that holds the reference to the parent arrow scope display class.
    /// Used for accessing captured arrow-level variables through the $arrowDC field.
    /// </summary>
    public FieldBuilder? CurrentArrowScopeDCField { get; set; }

    /// <summary>
    /// Field map of the PARENT arrow's scope display class (variable name -> field),
    /// accessible via <see cref="CurrentArrowScopeDCField"/>. Set independently of
    /// <see cref="ArrowScopeDisplayClassFields"/> (which holds the CURRENT arrow's
    /// own DC fields) so that arrows which both own a DC and chain through a
    /// parent's DC can resolve captured variables from either scope.
    /// </summary>
    public Dictionary<string, FieldBuilder>? ParentArrowScopeDisplayClassFields { get; set; }

    /// <summary>
    /// Set of variable names that are stored in the PARENT arrow's scope display
    /// class, reachable through <see cref="CurrentArrowScopeDCField"/>.
    /// </summary>
    public HashSet<string>? ParentArrowCapturedLocals { get; set; }

    /// <summary>
    /// A captured variable's live access path when it lives on an ancestor
    /// arrow's scope DC referenced by an EXTRA (non-primary) reference field:
    /// load <see cref="RefField"/> off the current display class (arg0), then
    /// load/store <see cref="VarField"/> on that scope DC instance.
    /// </summary>
    public readonly record struct ExtraScopeBinding(FieldBuilder RefField, FieldBuilder VarField);

    /// <summary>
    /// Per-name live bindings for captured variables whose source scope DC is
    /// referenced by an extra reference field on the current closure's display
    /// class. Populated from the analyzer's capture-source record, so entries
    /// are shadow-correct for THIS closure. Checked by LocalVariableResolver
    /// after the primary parent-DC path.
    /// </summary>
    public Dictionary<string, ExtraScopeBinding>? ExtraArrowScopeBindings { get; set; }

    /// <summary>
    /// The current closure's own EXTRA ancestor-scope reference fields, keyed
    /// by source callable node (arrow or inner function declaration). Used to
    /// chain references into closures created inside this body (type-matched
    /// against the new closure's reference fields).
    /// </summary>
    public Dictionary<object, FieldBuilder>? CurrentArrowScopeDCExtraFields { get; set; }

    /// <summary>
    /// Global map of every arrow's EXTRA ancestor-scope reference fields
    /// (closure arrow → source callable node → field). The creation-site
    /// emitter populates these alongside the primary $arrowDC field.
    /// </summary>
    public Dictionary<ArrowFunction, Dictionary<object, FieldBuilder>>? ArrowScopeDCExtraFieldsByArrow { get; set; }

    // ============================================
    // Per-iteration loop-binding reference cells (#650)
    // ============================================

    /// <summary>
    /// For a <c>for (let/const …)</c> binding that needs a per-iteration reference
    /// cell, the local holding the CURRENT iteration's <c>StrongBox&lt;object&gt;</c>.
    /// Populated by the for-loop emitter while emitting the loop body and increment,
    /// and consulted FIRST by <see cref="LocalVariableResolver"/> so loop-body reads
    /// and writes go through <c>StrongBox.Value</c> rather than the (now dead) plain
    /// local. Scoped: the emitter removes the entry on loop exit. Empty outside a
    /// cell-bearing loop body.
    /// </summary>
    public Dictionary<string, LocalBuilder> CellBindingLocals { get; } = [];

    /// <summary>
    /// When emitting a closure body, the captured field names that hold a
    /// per-iteration cell (a <c>StrongBox&lt;object&gt;</c>) rather than a plain
    /// value. The resolver loads the captured field, then reads <c>StrongBox.Value</c>
    /// for these names (and writes through it). Set from
    /// <see cref="PerIterationCellAnalyzer.ClosureCellFields"/> when the closure
    /// body's emit context is established.
    /// </summary>
    public HashSet<string>? CellCapturedFieldNames { get; set; }

    // ============================================
    // Inner Function Support
    // ============================================

    /// <summary>
    /// Maps inner Stmt.Function nodes to their compiled methods.
    /// </summary>
    public Dictionary<Stmt.Function, MethodBuilder>? InnerFunctionMethods { get; set; }

    /// <summary>
    /// Maps inner Stmt.Function nodes to their display class types (capturing only).
    /// </summary>
    public Dictionary<Stmt.Function, TypeBuilder>? InnerFunctionDisplayClasses { get; set; }

    /// <summary>
    /// Maps inner Stmt.Function nodes to their display class field mappings.
    /// </summary>
    public Dictionary<Stmt.Function, Dictionary<string, FieldBuilder>>? InnerFunctionDCFields { get; set; }

    /// <summary>
    /// Maps inner Stmt.Function nodes to their display class constructors.
    /// </summary>
    public Dictionary<Stmt.Function, ConstructorBuilder>? InnerFunctionDCCtors { get; set; }

    /// <summary>
    /// Maps inner Stmt.Function nodes to their $entryPointDC fields.
    /// </summary>
    public Dictionary<Stmt.Function, FieldBuilder>? InnerFunctionEntryPointDCFields { get; set; }

    /// <summary>
    /// Maps inner Stmt.Function nodes to their $functionDC fields.
    /// </summary>
    public Dictionary<Stmt.Function, FieldBuilder>? InnerFunctionFunctionDCFields { get; set; }

    /// <summary>
    /// Maps inner function names to their compiled methods for the current scope.
    /// Used for direct calls and variable references within inner function bodies.
    /// </summary>
    public Dictionary<string, MethodBuilder>? InnerFunctionMethodsByName { get; set; }

    /// <summary>
    /// Maps inner function names to their display class types (for capturing inner functions).
    /// Used together with InnerFunctionMethodsByName for proper call dispatch.
    /// </summary>
    public Dictionary<string, TypeBuilder>? InnerFunctionDisplayClassesByName { get; set; }

    /// <summary>
    /// Inner function declarations already materialized by the callable-body hoisting pre-pass
    /// (<c>EmitInnerFunctionHoisting</c>), keyed by AST reference identity. A declaration nested in
    /// a block/loop/if is NOT in this set — the pre-pass only scans the callable body's top-level
    /// statements — so <see cref="EmitBlockScopedInnerFunction"/> materializes it in place instead.
    /// Guards against emitting a top-level declaration twice (once hoisted, once in place).
    /// </summary>
    public HashSet<Stmt.Function>? HoistedInnerFunctions { get; set; }

    /// <summary>
    /// Materializes a block-scoped inner <c>function</c> declaration (create its TSFunction, snapshot
    /// captures, bind a block-scoped local) at its textual position. Set by <c>EmitInnerFunctionHoisting</c>
    /// so the statement emitter can create declarations nested in blocks/loops — which the top-level
    /// hoisting pre-pass skips — with correct per-iteration capture. No-op for declarations already in
    /// <see cref="HoistedInnerFunctions"/> or not collected as inner functions (#1230).
    /// </summary>
    public Action<ILGenerator, CompilationContext, Stmt.Function>? EmitBlockScopedInnerFunction { get; set; }
}
