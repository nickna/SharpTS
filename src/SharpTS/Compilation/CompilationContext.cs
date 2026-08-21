using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Compilation.Emitters.Modules;
using SharpTS.Compilation.Registries;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Represents the type currently on top of the IL evaluation stack.
/// Used for unboxed numeric optimization to avoid unnecessary boxing/unboxing.
/// </summary>
public enum StackType
{
    /// <summary>Object reference - could be any boxed type or reference type.</summary>
    Unknown,
    /// <summary>Native double (float64) - unboxed numeric value.</summary>
    Double,
    /// <summary>Native bool (int32 as 0/1) - unboxed boolean value.</summary>
    Boolean,
    /// <summary>String reference.</summary>
    String,
    /// <summary>Null reference.</summary>
    Null
}

/// <summary>
/// Entry in the hoisted array cache: a typed local variable and its descriptor.
/// </summary>
public record struct HoistedArrayEntry(LocalBuilder TypedLocal, ArrayElementsDescriptor Descriptor);

/// <summary>
/// Entry in the hoisted typed-array cache (#928): a loop-invariant variable statically typed as a
/// numeric TypedArray, cast to its concrete <c>$XArray</c> type ONCE before the loop. The element
/// index fast paths load <see cref="TypedLocal"/> directly instead of re-emitting
/// <c>ldloc; castclass $XArray</c> on every access.
/// </summary>
public record struct HoistedTypedArrayEntry(LocalBuilder TypedLocal, Type XArrayType, string ElementType);

public record struct HoistedCompactRecordEntry(
    LocalBuilder TypedLocal,
    string Fingerprint,
    bool IsExact,
    bool RequiresMaterializationGuard = false);

/// <summary>
/// Holds compilation state passed between ILCompiler and ILEmitter.
/// </summary>
/// <remarks>
/// Central state container for IL compilation. Provides access to the current
/// <see cref="ILGenerator"/>, <see cref="TypeMapper"/>, <see cref="LocalsManager"/>,
/// and various lookup tables for functions, classes, static members, closures,
/// and enums. Also tracks parameters, loop labels for break/continue, and
/// display class state for closure capture. Passed to <see cref="ILEmitter"/> methods.
/// </remarks>
/// <seealso cref="ILCompiler"/>
/// <seealso cref="ILEmitter"/>
/// <seealso cref="LocalsManager"/>
public partial class CompilationContext
{
    // ============================================
    // Core Compilation Infrastructure
    // ============================================

    public ILGenerator IL { get; }
    public TypeMapper TypeMapper { get; }
    public LocalsManager Locals { get; }

    /// <summary>
    /// Validated IL builder that wraps the ILGenerator with compile-time checks.
    /// Use this for new code to catch label, stack, and exception block errors early.
    /// </summary>
    public ValidatedILBuilder ILBuilder { get; private set; }

    /// <summary>
    /// Type provider for resolving .NET types (runtime or reference assembly mode).
    /// Use this instead of typeof() for type resolution to support --ref-asm compilation.
    /// </summary>
    public TypeProvider Types { get; }

    // Integer loop-counter prototype (#928, gated by SHARPTS_INT_LOOP_COUNTER): names of locals
    // currently backed by a native Int64 slot because they are provably-integer monotonic loop
    // counters. Reads convert to double on load; the increment and recognized index sites consume
    // the int directly. Populated/cleared per loop scope by EmitFor/EmitVarStatement.
    public HashSet<string> IntegerCounterLocals { get; } = new();

    /// <summary>
    /// Function parameters whose exact compact-record type test was hoisted to
    /// the prologue. Entries are limited to parameters never reassigned in the
    /// body, so property reads may reuse the typed local safely.
    /// </summary>
    public Dictionary<string, HoistedCompactRecordEntry> HoistedCompactRecordParameters { get; } = [];

    /// <summary>
    /// Compact-record fingerprints whose global materialization flag has been
    /// checked on the current emitted control-flow branch. Property reads on an
    /// exact typed local may skip repeating that global guard.
    /// </summary>
    public HashSet<string> HoistedCompactRecordMaterializationGuards { get; } = [];

    // Emitted runtime types and methods (for standalone DLLs)
    public EmittedRuntime? Runtime { get; set; }

    // Whole-program feature analysis used by semantic optimization guards.
    public RuntimeFeatureSet? RuntimeFeatures { get; set; }

    /// <summary>
    /// Expression-only literal eval programs parsed during callable discovery.
    /// Reusing these exact AST nodes during emission is required because arrow
    /// method registries are keyed by AST identity.
    /// </summary>
    internal Dictionary<SharpTS.Parsing.Expr.Call, List<SharpTS.Parsing.Stmt>>?
        StaticDirectEvalStatements { get; set; }

    /// <summary>Calls proven during discovery to use an unchanged top-level eval alias.</summary>
    internal HashSet<SharpTS.Parsing.Expr.Call>? StaticIndirectEvalCalls { get; set; }

    // Type emitter registry for type-first method dispatch
    public TypeEmitterRegistry? TypeEmitterRegistry { get; set; }

    // Built-in module emitter registry for fs, path, os, etc.
    public BuiltInModuleEmitterRegistry? BuiltInModuleEmitterRegistry { get; set; }

    // Built-in module namespace variables (variable name -> module name)
    // Tracks which local variables are built-in module namespaces for direct dispatch
    public Dictionary<string, string>? BuiltInModuleNamespaces { get; set; }

    // Built-in module method bindings (variable name -> (module name, method name))
    // Tracks which local variables are bound to built-in module methods for direct dispatch
    // Example: import { readFile } from 'fs/promises' -> readFile -> ("fs/promises", "readFile")
    public Dictionary<string, (string ModuleName, string MethodName)>? BuiltInModuleMethodBindings { get; set; }

    // All imported names from any module (builtin, primitive, stdlib TS, or user).
    // Call handlers for globally-intercepted names (TimerHandler, FetchHandler, etc.)
    // check this set to avoid shadowing imports. Stdlib TS modules like
    // 'timers' re-export setTimeout/setInterval as TS functions that must
    // win over the global handler.
    public HashSet<string>? ImportedNames { get; set; }

    // ============================================
    // Registry Services
    // ============================================

    /// <summary>
    /// Registry for class-related compilation state lookups.
    /// Provides centralized methods for resolving class names, constructors,
    /// instance/static members, and inheritance chains.
    /// </summary>
    public ClassRegistry? ClassRegistry { get; set; }

    // ============================================
    // Enum Support
    // ============================================

    // Enum support: enum name -> member name -> value (double or string)
    public Dictionary<string, Dictionary<string, object>>? EnumMembers { get; set; }

    // Enum reverse mapping: enum name -> value -> member name (only numeric values)
    public Dictionary<string, Dictionary<double, string>>? EnumReverse { get; set; }

    // Enum kinds: enum name -> kind
    public Dictionary<string, EnumKind>? EnumKinds { get; set; }

    // ============================================
    // Generic Type Parameters
    // ============================================

    // Current scope's generic type parameters (name -> GenericTypeParameterBuilder or Type)
    public Dictionary<string, Type> GenericTypeParameters { get; set; } = [];

    // ============================================
    // Miscellaneous State
    // ============================================

    /// <summary>
    /// The return type of the current method being compiled.
    /// Used for typed return optimization to avoid unnecessary boxing.
    /// When null, defaults to object (boxed return).
    /// </summary>
    public Type? CurrentMethodReturnType { get; set; }

    /// <summary>
    /// The method whose body <see cref="IL"/> emits into. Sequence points are recorded against it,
    /// so any context emitting a body users can step through has to set it.
    /// </summary>
    internal System.Reflection.MethodBase? CurrentMethod { get; set; }

    /// <summary>
    /// Source document and symbol sink for the code being emitted, or null when the build is not
    /// producing debug symbols. See <see cref="Symbols.DebugEmitScope"/>.
    /// </summary>
    internal Symbols.DebugEmitScope? DebugScope { get; set; }

    /// <summary>
    /// Whether the current compilation context is in JavaScript strict mode.
    /// Affects property assignment behavior on frozen/sealed objects.
    /// </summary>
    public bool IsStrictMode { get; set; }

    /// <summary>
    /// Optional strictness of the surrounding function's established this
    /// binding when emitting nested eval code. Direct eval may enable strict
    /// syntax for its own source, but it inherits the caller's already-bound
    /// this value rather than rebinding it under the eval source's strictness.
    /// </summary>
    public bool? ThisBindingIsStrictOverride { get; set; }

    /// <summary>
    /// True when emitting code inside a static constructor (class initializer).
    /// In this context, 'this' refers to the class type, not an instance.
    /// </summary>
    public bool IsStaticConstructorContext { get; set; }

    /// <summary>
    /// Parameter bindings that are still uninitialized while a default initializer is
    /// being evaluated. Reads must throw ReferenceError instead of observing the raw
    /// argument slot; the current and all later parameters are in this TDZ.
    /// </summary>
    internal IReadOnlySet<string>? DefaultParameterTdzNames { get; set; }

    /// <summary>Names of lexical bindings whose captured storage can hold the TDZ sentinel.</summary>
    internal IReadOnlySet<string>? LexicalTdzNames { get; set; }

    /// <summary>
    /// Lexical declaration whose initializer is currently being emitted. Its
    /// own binding exists but is uninitialized even when an outer binding has
    /// the same name.
    /// </summary>
    internal string? LexicalInitializerTdzName { get; set; }

    /// <summary>
    /// True only when emitting the module's top-level statements (entry-point Main,
    /// module/script <c>$Initialize</c>). A <c>var</c>/<c>let</c>/<c>const</c> declared
    /// here is a genuine module-level binding and is routed to its static field
    /// (<see cref="TopLevelStaticVars"/>) or entry-point display-class field
    /// (<see cref="CapturedTopLevelVars"/>) so all functions can read it.
    /// <para>
    /// Function/method/arrow bodies receive those same dictionaries for READ access
    /// but set this flag to <c>false</c>: a same-named declaration inside a function
    /// body is a function-local that must shadow the module binding, not overwrite its
    /// storage. Without this gate a function-local <c>const x</c> whose name collides
    /// with a module-level <c>x</c> silently writes through to the module slot and the
    /// real local binding is never created (#562).
    /// </para>
    /// </summary>
    public bool IsModuleTopLevel { get; set; }

    /// <summary>
    /// True when the current top-level context represents a classic script
    /// global environment rather than an ES/CommonJS module. Top-level
    /// <c>var</c> bindings in this mode also materialize as properties of the
    /// global object; lexical declarations and module bindings do not.
    /// </summary>
    public bool IsScriptTopLevel { get; set; }

    // Namespace support: namespace path -> static field
    public Dictionary<string, FieldBuilder>? NamespaceFields { get; set; }

    /// <summary>
    /// The dotted path (e.g. <c>N</c> or <c>N.M</c>) of the namespace whose member body this
    /// context emits, or null outside any namespace. Threaded onto every namespace-member body
    /// context so <see cref="GetQualifiedFunctionName"/> / <see cref="ResolveFunctionName"/>
    /// namespace-qualify member functions — keeping <c>A.f</c> and <c>B.f</c> (and a top-level
    /// <c>f</c>) in distinct registry slots instead of colliding (#657). Also set on the
    /// namespace-emission context so member var initializers and sibling references resolve to
    /// the namespace's own backing fields rather than same-named module bindings.
    /// </summary>
    public string? CurrentNamespacePath { get; set; }

    // Namespace-level var/let/const backing fields: namespace path -> var name -> static field.
    // A namespace member variable is stored in its namespace object (for external `N.x` access)
    // AND in a static field so functions declared in the namespace can resolve the bare name —
    // the namespace object is not visible inside the function bodies (#567). Mirrors how
    // module top-level vars use TopLevelStaticVars.
    public Dictionary<string, Dictionary<string, FieldBuilder>>? NamespaceVarFields { get; set; }

    // Top-level variables captured by async functions (stored as static fields)
    public Dictionary<string, FieldBuilder>? TopLevelStaticVars { get; set; }

    // Type information from static analysis
    public TypeMap? TypeMap { get; set; }

    // Dead code analysis results
    public DeadCodeInfo? DeadCode { get; set; }

    // ============================================
    // Parameter Tracking
    // ============================================

    // Parameter tracking (name -> arg index)
    private readonly Dictionary<string, int> _parameters = [];
    private readonly Dictionary<string, Type> _parameterTypes = [];

    // ============================================
    // Loop and Exception Block Control
    // ============================================

    // Loop control labels. LabelNames carries every label a labeled break/continue can target —
    // usually zero or one, but a chain like `a: b: for` hands the loop both, so `continue a` and
    // `continue b` resolve to the same loop. Empty (NoLabels) for an unlabeled loop.
    public Stack<(Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)> LoopLabels { get; } = new();

    /// <summary>Shared empty label set for unlabeled loops (avoids per-loop allocation).</summary>
    public static readonly IReadOnlyList<string> NoLabels = [];

    // Labels parked by EmitLabeledStatement for the loop a chain of them directly wraps. The loop
    // drains them all at entry via TakePendingLoopLabels and treats a continue/break to any of them
    // as targeting itself, running the loop's own step (a for's increment, a while's re-test) rather
    // than restarting it — restarting a `for` would re-run its initializer forever (#558/#580).
    private readonly List<string> _pendingLoopLabels = [];

    /// <summary>Parks a label for the next loop to adopt. A chain parks several before the loop.</summary>
    public void AddPendingLoopLabel(string label) => _pendingLoopLabels.Add(label);

    /// <summary>Discards any parked labels the next loop didn't drain (defensive cleanup).</summary>
    public void ClearPendingLoopLabels() => _pendingLoopLabels.Clear();

    /// <summary>
    /// Returns the labels parked for the loop now being entered, and clears them, so they attach to
    /// exactly one loop.
    /// </summary>
    public IReadOnlyList<string> TakePendingLoopLabels()
    {
        if (_pendingLoopLabels.Count == 0) return NoLabels;
        var labels = _pendingLoopLabels.ToArray();
        _pendingLoopLabels.Clear();
        return labels;
    }

    // Hoisted array type caches: stack of per-loop dictionaries mapping
    // variable name → (typed local, descriptor) for arrays whose isinst
    // check has been hoisted to the loop preamble.
    public Stack<Dictionary<string, HoistedArrayEntry>> HoistedArrayCaches { get; } = new();

    /// <summary>
    /// Looks up a hoisted array cache entry for the given variable name,
    /// searching from innermost to outermost loop scope.
    /// </summary>
    public HoistedArrayEntry? TryGetHoistedArray(string variableName)
    {
        foreach (var cache in HoistedArrayCaches)
        {
            if (cache.TryGetValue(variableName, out var entry))
                return entry;
        }
        return null;
    }

    /// <summary>
    /// Hoisted typed-array receiver caches (#928), innermost loop scope on top. Parallel to
    /// <see cref="HoistedArrayCaches"/> but for numeric TypedArray receivers cast to their concrete
    /// <c>$XArray</c> type once per loop.
    /// </summary>
    public Stack<Dictionary<string, HoistedTypedArrayEntry>> HoistedTypedArrayCaches { get; } = new();

    /// <summary>
    /// Looks up a hoisted typed-array receiver for the given variable name, innermost loop scope first.
    /// </summary>
    public HoistedTypedArrayEntry? TryGetHoistedTypedArray(string variableName)
    {
        foreach (var cache in HoistedTypedArrayCaches)
        {
            if (cache.TryGetValue(variableName, out var entry))
                return entry;
        }
        return null;
    }

    /// <summary>
    /// If <paramref name="variableName"/> currently binds to a promoted typed-array local
    /// (a slot whose CLR type is <c>List&lt;double&gt;</c>/<c>List&lt;bool&gt;</c>, declared by
    /// the #857/#860 promotion path), returns its <see cref="LocalBuilder"/> and descriptor;
    /// otherwise null. The slot's CLR type is the single source of truth, so this is
    /// automatically scope-correct under shadowing and never misfires for a captured/object
    /// local. No other code path declares a user local with a typed-list slot.
    /// </summary>
    public (LocalBuilder Local, ArrayElementsDescriptor Descriptor)? TryGetPromotedArrayLocal(string variableName)
    {
        if (!Locals.TryGetLocal(variableName, out var local)) return null;
        var slotType = Locals.GetLocalType(variableName);
        if (slotType == Types.ListOfDouble) return (local, ArrayElements.Double);
        if (slotType == Types.ListOfBool) return (local, ArrayElements.Bool);
        return null;
    }

    /// <summary>
    /// If <paramref name="variableName"/> currently binds to a promoted string-accumulator local
    /// (a slot whose CLR type is <c>StringBuilder</c>, declared by the #857 promotion path), returns its
    /// <see cref="LocalBuilder"/>; otherwise null. The slot's CLR type is the single source of truth, so
    /// this is automatically scope-correct under shadowing and never misfires for a captured/object local
    /// (no other code path declares a user local with a <c>StringBuilder</c> slot).
    /// </summary>
    public LocalBuilder? TryGetPromotedStringAccumulator(string variableName)
    {
        if (!Locals.TryGetLocal(variableName, out var local)) return null;
        return Locals.GetLocalType(variableName) == Types.StringBuilder ? local : null;
    }

    /// <summary>
    /// Resolves the generated shape struct for a promoted object-literal local by its canonical shape
    /// key (#862), or null if shapes are not threaded into this context / the key is unknown. Used at the
    /// declaration site to pick the struct type to declare the local with.
    /// </summary>
    public ObjectShapeTypeInfo? TryGetObjectShapeType(string canonicalKey) =>
        ObjectShapes?.ByKey.GetValueOrDefault(canonicalKey);

    /// <summary>
    /// If <paramref name="variableName"/> currently binds to a promoted object-literal local (a slot
    /// whose CLR type is one of the generated <c>$Shape_N</c> structs, #862), returns its
    /// <see cref="LocalBuilder"/> and shape info; otherwise null. The slot's CLR type is the single
    /// source of truth, so this is automatically scope-correct under shadowing and never misfires for a
    /// captured/object local — no other code path declares a user local with a shape-struct slot.
    /// </summary>
    public (LocalBuilder Local, ObjectShapeTypeInfo Shape)? TryGetPromotedObjectLocal(string variableName)
    {
        if (ObjectShapes == null) return null;
        if (!Locals.TryGetLocal(variableName, out var local)) return null;
        var slotType = Locals.GetLocalType(variableName);
        if (slotType != null && ObjectShapes.ByClrType.TryGetValue(slotType, out var shape))
            return (local, shape);
        return null;
    }

    // Exception block tracking for proper return handling
    public int ExceptionBlockDepth { get; set; } = 0;
    public LocalBuilder? ReturnValueLocal { get; set; }
    public Label ReturnLabel { get; set; }
    public bool HasDeferredVoidReturn { get; set; }

    // ============================================
    // Constructor and Core Methods
    // ============================================

    public CompilationContext(
        ILGenerator il,
        TypeMapper typeMapper,
        Dictionary<string, MethodBuilder> functions,
        Dictionary<string, TypeBuilder> classes,
        Dictionary<string, FieldBuilder>? namespaceFields,
        Dictionary<string, Dictionary<string, FieldBuilder>>? namespaceVarFields,
        TypeProvider? types = null)
    {
        IL = il;
        TypeMapper = typeMapper;
        Functions = functions;
        Classes = classes;
        // Namespace registries are whole-compilation globals (like Functions/Classes), so they
        // are threaded through the constructor — every emission context can resolve a bare
        // namespace name and a namespace-var backing field, not just the subset that used to set
        // these via object initializers. That subset gap was #656 (a non-member function body
        // threw "Undefined variable 'N'"). The maps are shared references, populated during the
        // define phase and observed live here.
        NamespaceFields = namespaceFields;
        NamespaceVarFields = namespaceVarFields;
        Types = types ?? TypeProvider.Runtime;
        Locals = new LocalsManager(il);
        ILBuilder = new ValidatedILBuilder(il);
    }

    public void DefineParameter(string name, int argIndex, Type? paramType = null)
    {
        _parameters[name] = argIndex;
        if (paramType != null)
        {
            _parameterTypes[name] = paramType;
        }
    }

    public bool TryGetParameter(string name, out int argIndex)
    {
        return _parameters.TryGetValue(name, out argIndex);
    }

    public bool TryGetParameterType(string name, out Type? paramType)
    {
        if (_parameterTypes.TryGetValue(name, out var type))
        {
            paramType = type;
            return true;
        }
        paramType = null;
        return false;
    }

    /// <summary>
    /// With a boxed object on the stack destined for a Starg into
    /// <paramref name="paramName"/>'s arg slot, converts it to the slot's
    /// declared type when the parameter is typed: Unbox_Any for value types,
    /// castclass for reference types. No-op for untyped (object) slots.
    /// Captured-parameter dual-writes need this — storing the boxed object
    /// straight into a double/string slot fails IL verification
    /// (StackUnexpected family, see #284).
    /// </summary>
    public void EmitConvertForParamSlot(ILGenerator il, string paramName)
    {
        if (!_parameterTypes.TryGetValue(paramName, out var pt) || pt == Types.Object)
            return;
        if (pt.IsValueType)
            il.Emit(OpCodes.Unbox_Any, pt);
        else
            il.Emit(OpCodes.Castclass, pt);
    }

    public void EnterLoop(Label breakLabel, Label continueLabel, string? labelName = null)
    {
        // An explicit label names this loop alone; otherwise the loop adopts whatever an enclosing
        // labeled statement parked — a chain hands it several — so its own continue/break targets
        // carry every label (#558/#580).
        var labels = labelName != null ? new[] { labelName } : TakePendingLoopLabels();
        LoopLabels.Push((breakLabel, continueLabel, labels));
    }

    /// <summary>
    /// Enters a loop carrying a pre-collected set of label names. Used where the labels are drained
    /// once up front and handed to each of several alternative runtime paths (e.g. for-of's iterator
    /// / index-based variants), so every path's break/continue targets resolve no matter which one
    /// runs at runtime (#558).
    /// </summary>
    public void EnterLoop(Label breakLabel, Label continueLabel, IReadOnlyList<string> labelNames)
        => LoopLabels.Push((breakLabel, continueLabel, labelNames));

    public void ExitLoop()
    {
        LoopLabels.Pop();
    }

    public (Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)? CurrentLoop =>
        LoopLabels.Count > 0 ? LoopLabels.Peek() : null;

    /// <summary>
    /// Find a loop scope that carries the given label name (for labeled break/continue).
    /// </summary>
    public (Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)? FindLabeledLoop(string labelName)
    {
        foreach (var entry in LoopLabels)
        {
            if (entry.LabelNames.Contains(labelName))
                return entry;
        }
        return null;
    }
}
