using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// State container classes for organizing ILCompiler's compilation state.
/// These group related dictionaries into semantic containers for better maintainability.
/// </summary>
public partial class ILCompiler
{
    #region State Container Classes

    /// <summary>
    /// State for class declaration compilation.
    /// </summary>
    private sealed class ClassCompilationState
    {
        public HashSet<Stmt.Class> Declarations { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<Stmt.Class> EmittedMethodBodies { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<string, TypeBuilder> Builders { get; } = [];
        public Dictionary<Stmt.Class, string> BlockScopedNames { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Stmt.Class, TypeBuilder> BlockScopedBuilders { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<string, Type> ExternalTypes { get; } = [];
        public Dictionary<string, string?> Superclass { get; } = [];
        /// <summary>
        /// Qualified names of classes that (directly or transitively) extend a built-in Error type.
        /// Set during DefineClass and used by HasFields to emit Error property fallback.
        /// </summary>
        public HashSet<string> ErrorSubclasses { get; } = [];
        /// <summary>
        /// Qualified names of classes that (directly or transitively) extend the
        /// built-in Array (#233). Set during DefineClass; used by constructor
        /// emission to chain to $Array's ctor-args constructor.
        /// </summary>
        public HashSet<string> ArraySubclasses { get; } = [];
        /// <summary>
        /// Qualified names of classes that (directly or transitively) extend the
        /// built-in Promise (#242). Set during DefineClass; used by constructor
        /// emission to chain to $Promise via PromiseFromExecutor and by static
        /// dispatch to inherit the Promise static side.
        /// </summary>
        public HashSet<string> PromiseSubclasses { get; } = [];
        public Dictionary<string, ConstructorBuilder> Constructors { get; } = [];
        public Dictionary<TypeBuilder, ConstructorBuilder> PrototypeConstructors { get; } = [];
        public Dictionary<string, List<ConstructorBuilder>> ConstructorOverloads { get; } = [];
        public Dictionary<string, Dictionary<string, FieldBuilder>> StaticFields { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> StaticMethods { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> InstanceMethods { get; } = [];
        /// <summary>
        /// Assembly-internal primitive-returning companions for eligible public
        /// instance methods. The public method remains the virtual object ABI.
        /// </summary>
        public Dictionary<string, Dictionary<string, MethodBuilder>> TypedPrimitiveInstanceMethodCores { get; } = [];
        public Dictionary<Stmt.Function, MethodBuilder> TypedPrimitiveMethodCoreBuilders { get; } =
            new(ReferenceEqualityComparer.Instance);
        public Dictionary<string, Dictionary<string, MethodBuilder>> InstanceGetters { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> InstanceSetters { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> StaticGetters { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> StaticSetters { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> PreDefinedMethods { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> PreDefinedAccessors { get; } = [];
        public Dictionary<Parsing.Stmt.Accessor, MethodBuilder> AccessorBuilders { get; } =
            new(ReferenceEqualityComparer.Instance);
        // Symbol-keyed computed accessors (#266): class name (typeBuilder.Name) ->
        // list of (accessor AST node, emitted getter/setter MethodBuilder). Used to
        // emit the bodies and to register them in the class .cctor.
        public Dictionary<string, List<(Parsing.Stmt.Accessor Accessor, MethodBuilder Method)>> SymbolAccessors { get; } = [];
        // Symbol-keyed computed methods (#647): class name (typeBuilder.Name) -> list of
        // (renamed method AST with a unique $symmethod_N name, computed key expression,
        // emitted MethodBuilder). The renamed method flows through the normal method-body
        // machinery (incl. generator/async state machines); the key drives the .cctor
        // RegisterSymbolMethod call.
        public Dictionary<string, List<(Parsing.Stmt.Function Method, Parsing.Expr Key, MethodBuilder Builder)>> SymbolMethods { get; } = [];
        public Dictionary<Stmt.Class, (MethodBuilder Method, IReadOnlyList<Expr> Keys)> DeferredComputedClassKeys { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<string, FieldBuilder> InstanceFieldsField { get; } = [];
        public Dictionary<string, GenericTypeParameterBuilder[]> GenericParams { get; } = [];

        // ES2022 Private Class Elements Support
        // Static field holding ConditionalWeakTable<object, Dictionary<string, object?>> for instance private fields
        public Dictionary<string, FieldBuilder> PrivateFieldStorage { get; } = [];
        // List of private field names per class for initialization order
        public Dictionary<string, List<string>> PrivateFieldNames { get; } = [];
        // Static private fields: class name -> field name (without #) -> FieldBuilder
        public Dictionary<string, Dictionary<string, FieldBuilder>> StaticPrivateFields { get; } = [];
        // Private instance methods: class name -> method name (without #) -> MethodBuilder
        public Dictionary<string, Dictionary<string, MethodBuilder>> PrivateMethods { get; } = [];
        // Static private methods: class name -> method name (without #) -> MethodBuilder
        public Dictionary<string, Dictionary<string, MethodBuilder>> StaticPrivateMethods { get; } = [];

        // $IHasFields interface method stubs (bodies emitted later after method definitions)
        public Dictionary<string, HasFieldsMethodStubs> HasFieldsStubs { get; } = [];
    }

    /// <summary>
    /// Holds the MethodBuilder stubs for $IHasFields interface methods.
    /// Bodies are emitted later when method definitions are available.
    /// </summary>
    private sealed class HasFieldsMethodStubs
    {
        public required MethodBuilder GetFields { get; init; }
        public required MethodBuilder GetProperty { get; init; }
        public required MethodBuilder SetProperty { get; init; }
        public required MethodBuilder HasProperty { get; init; }
        public required FieldInfo FieldsField { get; init; }
    }

    /// <summary>
    /// State for function declaration compilation.
    /// </summary>
    private sealed class FunctionCompilationState
    {
        public Dictionary<string, MethodBuilder> Builders { get; } = [];
        public Dictionary<string, List<MethodBuilder>> Overloads { get; } = [];
        public Dictionary<string, Dictionary<string, List<MethodBuilder>>> MethodOverloads { get; } = [];
        public Dictionary<string, (int RestParamIndex, int RegularParamCount)> RestParams { get; } = [];
        public Dictionary<string, GenericTypeParameterBuilder[]> GenericParams { get; } = [];
        public Dictionary<string, bool> IsGeneric { get; } = [];
        public Dictionary<MethodBase, int> Lengths { get; } = [];
        public Dictionary<MethodBase, string> Names { get; } = [];

        /// <summary>
        /// Qualified names of functions flagged at DefineFunction time as referencing
        /// <c>arguments</c>. Propagated to each body-emission <c>CompilationContext</c>
        /// so the direct-call emitter can publish caller args to the thread-static
        /// before <c>OpCodes.Call</c> (see #64). Populated during phase 3; consumed
        /// during phase 7 (body emission).
        /// </summary>
        public HashSet<string> CapturingArguments { get; } = [];

        /// <summary>
        /// Class/constructor MethodBuilders whose JavaScript body needs an
        /// <c>arguments</c> environment binding. Kept by builder identity so all
        /// statically resolved class-call paths share the same decision.
        /// </summary>
        public HashSet<MethodBase> MethodsCapturingArguments { get; } = [];
    }

    /// <summary>
    /// State for closure and arrow function compilation.
    /// </summary>
    private sealed class ClosureCompilationState
    {
        public ClosureAnalyzer Analyzer { get; set; } = null!;
        public Dictionary<Expr.ArrowFunction, MethodBuilder> ArrowMethods { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps `const NAME = (args) => …` (and `export const NAME = …`) at top-
        // level / module scope to the literal arrow's AST node. Iterator-helper
        // fast paths consult this when the callback argument is `Expr.Variable`,
        // letting `arr.map(myFn)` inline through the same delegate construction
        // as `arr.map((x) => …)`. Module-scope only: nested `const` bindings
        // would need scope-aware shadowing; punt to v2.
        public Dictionary<string, Expr.ArrowFunction> ConstArrowBindings { get; } = [];

        // `const NAME = (args) => …` local bindings whose arrow provably never escapes — only ever
        // invoked by name as `NAME(args)`. The emitter stores the bare display-class instance in a
        // typed local and the call site emits a direct `callvirt Invoke`, skipping the per-call
        // $TSFunction wrapper + reflective InvokeMethodValue dispatch (#858). Populated by
        // NonEscapingArrowLocalAnalyzer; the fast path only fires for capturing arrows whose in-scope
        // local slot type matches the display class (so a same-named binding elsewhere never hits it).
        public Dictionary<string, Expr.ArrowFunction> DirectCallArrowBindings { get; } = [];

        // Generated value-type "shape" structs for promoted object-literal locals (#862). Populated
        // after ObjectLocalPromotionAnalyzer by DefineObjectShapeTypes; the declaration site keys in by
        // canonical shape key, and the property get/set fast paths key in by the local slot's CLR type.
        public ObjectShapeRegistry ObjectShapes { get; } = new();
        public Dictionary<Expr.ArrowFunction, TypeBuilder> DisplayClasses { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ArrowFunction, Dictionary<string, FieldBuilder>> DisplayClassFields { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ArrowFunction, ConstructorBuilder> DisplayClassConstructors { get; } = new(ReferenceEqualityComparer.Instance);
        public int ArrowMethodCounter { get; set; }
        public int DisplayClassCounter { get; set; }

        // Entry-point display class for captured top-level variables
        // When a top-level variable is captured by a closure, it's stored here
        // so modifications in the closure are visible to the outer code.
        // The display class is shared across modules, but each module gets its
        // own fields (qualified by module path) to avoid two modules that both
        // declare `const foo` from clobbering each other.
        public TypeBuilder? EntryPointDisplayClass { get; set; }
        public ConstructorBuilder? EntryPointDisplayClassCtor { get; set; }

        // Per-module: which captured top-level var names belong to this module,
        // and which fields on the shared display class store them. Module mode
        // keys by module path; single-file/script mode uses the SingleFile
        // sentinel (Dictionary<string,...> rejects null keys).
        public Dictionary<string, HashSet<string>> ModuleCapturedTopLevelVars { get; } = [];
        public Dictionary<string, Dictionary<string, FieldBuilder>> ModuleEntryPointDisplayClassFields { get; } = [];
        // Per-module initialization flags for top-level let/const bindings.
        // Captured lexical bindings use instance bool fields on the entry-point
        // display class; non-captured bindings use static bool fields on $Program.
        public Dictionary<string, Dictionary<string, FieldBuilder>> ModuleTopLevelLexicalInitFields { get; } = [];

        // Sentinel key for single-file / script mode, where there's no module path.
        // Chosen so it can't clash with any real module path.
        public const string SingleFileKey = "<single-file>";

        // #1201: per-key names of top-level BLOCK-scoped let/const bindings lifted onto the
        // entry-point display class (subset of ModuleCapturedTopLevelVars[key]). The var/const
        // declaration emitter consults this so a lifted declaration in a nested block stores to
        // its DC field instead of a fresh local. Live reference — a later same-key registration
        // may revoke a lift before emission (RevokeCollidingBlockScopedLifts).
        public Dictionary<string, HashSet<string>> ModuleLiftedBlockScopedVars { get; } = [];

        // #1201: per-key union of every declared identifier seen by earlier registration calls
        // sharing the key (scripts merged into the single-file scope). A block-scope lift is
        // skipped/revoked when its name collides across those trees.
        public Dictionary<string, HashSet<string>> ModuleRegisteredDeclaredNames { get; } = [];

        // Flat union used only for cross-module decisions — e.g., "is this name
        // captured by SOMETHING so non-captured-var emission should skip it?"
        // Not consumed by emitters; those see module-scoped dicts.
        public HashSet<string> CapturedTopLevelVars { get; } = [];

        // Static field on $Program that holds the entry-point display class instance
        // This allows module init methods to access the same display class
        public FieldBuilder? EntryPointDisplayClassStaticField { get; set; }

        // Maps arrow functions to their $entryPointDC field (if they capture top-level vars)
        public Dictionary<Expr.ArrowFunction, FieldBuilder> ArrowEntryPointDCFields { get; } = new(ReferenceEqualityComparer.Instance);

        // ============================================
        // Function-level display classes (for captured local variables)
        // ============================================

        // Maps function (by qualified name) to its display class type
        public Dictionary<string, TypeBuilder> FunctionDisplayClasses { get; } = [];

        // Maps function to its display class constructor
        public Dictionary<string, ConstructorBuilder> FunctionDisplayClassCtors { get; } = [];

        // Maps function to its display class fields (variable name -> field)
        public Dictionary<string, Dictionary<string, FieldBuilder>> FunctionDisplayClassFields { get; } = [];

        // Variables captured by async arrows that should be excluded from function DCs
        // (they use the hoisted field mechanism and would conflict with the DC)
        public Dictionary<string, HashSet<string>> AsyncCapturedVarsExclusion { get; } = [];

        // Maps function to its AST node (needed for ClosureAnalyzer lookups)
        public Dictionary<string, object> FunctionAstNodes { get; } = [];

        // Maps arrow functions to their $functionDC field (if they capture function-level vars)
        public Dictionary<Expr.ArrowFunction, FieldBuilder> ArrowFunctionDCFields { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps an arrow to (captured source name -> renamed $functionDC storage key) for write-captured
        // block-scope shadows it lifts into the shared function DC under a disambiguated name (#838).
        // Consumed when emitting the arrow body to set CompilationContext.CurrentArrowFunctionDCFieldRenames.
        public Dictionary<Expr.ArrowFunction, Dictionary<string, string>> ArrowFunctionDCFieldRenames { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps arrow functions to the function display class they need access to
        public Dictionary<Expr.ArrowFunction, string> ArrowFunctionDCSource { get; } = new(ReferenceEqualityComparer.Instance);

        // Follow-up to #838: maps an ASYNC ARROW to the synthetic function-display-class key registered
        // for it — its own locals that a nested sync arrow captures-and-writes are lifted into a
        // reference-type DC on the async arrow's state machine, letting the async arrow play the role of a
        // "function" scope for nested sync arrows. Key shape "$asyncArrow$N" is disjoint from real
        // function keys. Registered in RegisterAsyncArrowFunctionDisplayClasses (Phase 5, pre-propagate).
        public Dictionary<Expr.ArrowFunction, string> AsyncArrowDCKeys { get; } = new(ReferenceEqualityComparer.Instance);

        // ============================================
        // Scope display classes (for callable-local vars captured by nested closures).
        // Keys are the OWNING callable's AST node: Expr.ArrowFunction, or Stmt.Function
        // for inner function declarations (#313) — both kinds of callable can own
        // mutable locals that sibling closures must share.
        // ============================================

        // Maps owning callable to its scope display class type
        public Dictionary<object, TypeBuilder> ArrowScopeDisplayClasses { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps owning callable to its scope display class constructor
        public Dictionary<object, ConstructorBuilder> ArrowScopeDisplayClassCtors { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps owning callable to its scope display class fields (variable name -> field)
        public Dictionary<object, Dictionary<string, FieldBuilder>> ArrowScopeDisplayClassFields { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps child arrows to their $arrowDC field (if they capture arrow-scope vars)
        public Dictionary<Expr.ArrowFunction, FieldBuilder> ArrowScopeDCFields { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps child arrows to the callable whose scope display class they need
        public Dictionary<Expr.ArrowFunction, object> ArrowScopeDCSource { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps arrow functions to their resolved parameter types (for typed parameter optimization)
        // When parameters have type annotations, this stores the resolved .NET types to avoid boxing
        public Dictionary<Expr.ArrowFunction, Type[]> ArrowParameterTypes { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps arrow functions to their resolved return types (for typed return optimization)
        // When arrow functions have return type annotations, this stores the resolved .NET type to avoid boxing
        public Dictionary<Expr.ArrowFunction, Type> ArrowReturnTypes { get; } = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// State for async function compilation.
    /// </summary>
    private sealed class AsyncCompilationState
    {
        public AsyncStateAnalyzer Analyzer { get; } = new();
        public Dictionary<string, AsyncStateMachineBuilder> StateMachines { get; } = [];
        public Dictionary<string, Stmt.Function> Functions { get; } = [];
        public Dictionary<string, AsyncStateAnalyzer.AsyncFunctionAnalysis> Analyses { get; } = [];
        public Dictionary<string, HashSet<Expr.Await>> DirectCoreAwaits { get; } = [];
        public Dictionary<string, Stmt.Function> SuspensionFreePrimitiveFunctions { get; } = [];
        public Dictionary<string, MethodBuilder> SuspensionFreePrimitiveCores { get; } = [];
        public Dictionary<string, MethodBuilder> StableSuspensionFreePrimitiveCores { get; } = [];
        public int StateMachineCounter { get; set; }
        public int ArrowCounter { get; set; }

        // Arrow function async support
        public Dictionary<Expr.ArrowFunction, AsyncArrowStateMachineBuilder> ArrowBuilders { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ArrowFunction, AsyncStateMachineBuilder> ArrowOuterBuilders { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ArrowFunction, AsyncArrowStateMachineBuilder> ArrowParentBuilders { get; } = new(ReferenceEqualityComparer.Instance);
        // Tracks which class (if any) each async arrow is enclosed in (for private field access)
        public Dictionary<Expr.ArrowFunction, string> ArrowEnclosingClassNames { get; } = new(ReferenceEqualityComparer.Instance);

        // Pooled HashSets for async arrow analysis
        public HashSet<string> DeclaredVars { get; } = [];
        public HashSet<string> UsedAfterAwait { get; } = [];
        public HashSet<string> DeclaredBeforeAwait { get; } = [];
    }

    /// <summary>
    /// State for generator function compilation.
    /// </summary>
    private sealed class GeneratorCompilationState
    {
        public GeneratorStateAnalyzer Analyzer { get; } = new();
        public Dictionary<string, GeneratorStateMachineBuilder> StateMachines { get; } = [];
        public Dictionary<string, Stmt.Function> Functions { get; } = [];
        public int StateMachineCounter { get; set; }
    }

    /// <summary>
    /// State for async generator function compilation.
    /// </summary>
    private sealed class AsyncGeneratorCompilationState
    {
        public AsyncGeneratorStateAnalyzer Analyzer { get; } = new();
        public Dictionary<string, AsyncGeneratorStateMachineBuilder> StateMachines { get; } = [];
        public Dictionary<string, Stmt.Function> Functions { get; } = [];
        public int StateMachineCounter { get; set; }
    }

    /// <summary>
    /// State for module compilation.
    /// </summary>
    private sealed class ModuleCompilationState
    {
        public Dictionary<string, TypeBuilder> Types { get; } = [];
        public Dictionary<string, Dictionary<string, FieldBuilder>> ExportFields { get; } = [];
        public Dictionary<string, MethodBuilder> InitMethods { get; } = [];
        public Dictionary<string, string> ClassToModule { get; } = [];
        public Dictionary<string, string> FunctionToModule { get; } = [];
        public Dictionary<string, string> EnumToModule { get; } = [];
        public Dictionary<string, string?> Namespaces { get; } = [];
        public ModuleResolver? Resolver { get; set; }
        public string? CurrentPath { get; set; }
        public string? CurrentDotNetNamespace { get; set; }
        /// <summary>
        /// Maps module path to the qualified class name when the module uses `export = ClassName`.
        /// Used to enable compile-time static member resolution for imported classes.
        /// </summary>
        public Dictionary<string, string> ExportAssignmentClasses { get; } = [];

        /// <summary>
        /// Maps module path to a dictionary of export name to qualified class name.
        /// Used for resolving named class imports to direct constructor calls.
        /// Example: ExportedClasses["./person.ts"]["Person"] = "$M_person_Person"
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> ExportedClasses { get; } = [];

        /// <summary>
        /// Maps module path to the qualified class name for default class exports.
        /// Used for resolving default class imports to direct constructor calls.
        /// Example: DefaultExportClasses["./counter.ts"] = "$M_counter_Counter"
        /// </summary>
        public Dictionary<string, string> DefaultExportClasses { get; } = [];

        /// <summary>
        /// Maps module path to a dictionary of import name to static field.
        /// Used for storing imported values so they're accessible from module functions.
        /// Example: ImportFields["./module-a.ts"]["createCounter"] = (FieldBuilder for static field)
        /// </summary>
        public Dictionary<string, Dictionary<string, FieldBuilder>> ImportFields { get; } = [];

        /// <summary>
        /// CommonJS modules: maps module path → its $exports static field. Used by both
        /// the CJS module body emitter (to read/write module.exports) and the require() lowering.
        /// </summary>
        public Dictionary<string, FieldBuilder> CommonJsExportFields { get; } = [];

        /// <summary>
        /// CommonJS modules: maps module path → its $GetExports static method. Used by
        /// require('./literal') lowering and ESM-imports-CJS lowering.
        /// </summary>
        public Dictionary<string, MethodBuilder> CommonJsGetExportsMethods { get; } = [];
    }

    /// <summary>
    /// State for enum compilation.
    /// </summary>
    private sealed class EnumCompilationState
    {
        public Dictionary<string, Dictionary<string, object>> Members { get; } = [];
        public Dictionary<string, Dictionary<double, string>> Reverse { get; } = [];
        public Dictionary<string, EnumKind> Kinds { get; } = [];
        public HashSet<string> ConstEnums { get; } = [];
    }

    /// <summary>
    /// State for class expression compilation.
    /// </summary>
    private sealed class ClassExpressionCompilationState
    {
        public Dictionary<Expr.ClassExpr, TypeBuilder> Builders { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, string> Names { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<string, Expr.ClassExpr> VarToClassExpr { get; } = [];
        public List<Expr.ClassExpr> ToDefine { get; } = [];
        public int Counter { get; set; }

        // Extended tracking (mirrors class compilation state)
        public Dictionary<Expr.ClassExpr, Dictionary<string, FieldBuilder>> BackingFields { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, PropertyBuilder>> Properties { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, Type>> PropertyTypes { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, HashSet<string>> DeclaredProperties { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, HashSet<string>> ReadonlyProperties { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, FieldBuilder>> StaticFields { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>> StaticMethods { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>> InstanceMethods { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>> Getters { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>> Setters { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, ConstructorBuilder> Constructors { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, GenericTypeParameterBuilder[]> GenericParams { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, string?> Superclass { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, string> EnclosingClass { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, FieldBuilder>> CaptureFields { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, (MethodBuilder Method, IReadOnlyList<Expr> Keys)> DeferredComputedKeys { get; } = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// State for typed interop (real .NET properties).
    /// </summary>
    private sealed class TypedInteropState
    {
        public Dictionary<string, Dictionary<string, FieldBuilder>> PropertyBackingFields { get; } = [];
        public Dictionary<string, Dictionary<string, PropertyBuilder>> ClassProperties { get; } = [];
        public Dictionary<string, HashSet<string>> DeclaredPropertyNames { get; } = [];
        public Dictionary<string, HashSet<string>> ReadonlyPropertyNames { get; } = [];
        public Dictionary<string, Dictionary<string, Type>> PropertyTypes { get; } = [];
        public Dictionary<string, FieldBuilder> ExtrasFields { get; } = [];
        public Dictionary<string, Dictionary<string, (MethodBuilder? Getter, MethodBuilder? Setter, Type PropertyType)>> ExplicitAccessors { get; } = [];
    }

    /// <summary>
    /// State for @lock decorator support.
    /// </summary>
    private sealed class LockDecoratorState
    {
        // Instance locks
        public Dictionary<string, FieldBuilder> SyncLockFields { get; } = [];
        public Dictionary<string, FieldBuilder> AsyncLockFields { get; } = [];
        public Dictionary<string, FieldBuilder> ReentrancyFields { get; } = [];

        // Static locks
        public Dictionary<string, FieldBuilder> StaticSyncLockFields { get; } = [];
        public Dictionary<string, FieldBuilder> StaticAsyncLockFields { get; } = [];
        public Dictionary<string, FieldBuilder> StaticReentrancyFields { get; } = [];
    }

    #endregion
}
