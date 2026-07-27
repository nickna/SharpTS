using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using PEPacker;
using SharpTS.Compilation.Symbols;
using SharpTS.Compilation.Emitters;
using SharpTS.Compilation.Emitters.Modules;
using SharpTS.Compilation.Registries;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Modules;
using SharpTS.Packaging;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Ahead-of-time compiler that generates .NET IL assemblies from the AST.
/// </summary>
/// <remarks>
/// One of two execution paths after type checking (the other being <see cref="Interpreter"/>).
/// Uses <c>System.Reflection.Emit</c> with <see cref="PersistedAssemblyBuilder"/> to emit
/// .NET assemblies. Compilation runs in multiple phases: emit runtime types, analyze closures,
/// define classes/functions, collect arrow functions, emit method bodies, and finalize.
/// Delegates IL emission to <see cref="ILEmitter"/>, closure detection to <see cref="ClosureAnalyzer"/>,
/// and type mapping to <see cref="TypeMapper"/>. Produces standalone executables via <c>--compile</c> flag.
/// </remarks>
/// <seealso cref="ILEmitter"/>
/// <seealso cref="ClosureAnalyzer"/>
/// <seealso cref="Interpreter"/>
public partial class ILCompiler
{
    /// <summary>
    /// The assembly builder. Concrete type is either <see cref="PersistedAssemblyBuilder"/>
    /// (default; supports <see cref="SaveToBytes"/> for shipping a standalone DLL) or the
    /// runtime <see cref="AssemblyBuilder"/> when <c>inMemoryOnly</c> is set (test-only fast
    /// path that skips the PE-format roundtrip entirely — saves ~150ms per compile).
    /// </summary>
    private readonly AssemblyBuilder _assemblyBuilder;
    /// <summary>
    /// True when the assembly builder is the runtime <see cref="AssemblyBuilder"/> (no PE
    /// emission). Used by tests to skip the ~150ms-per-test save+load roundtrip.
    /// SaveToBytes/Save are unavailable in this mode.
    /// </summary>
    private readonly bool _inMemoryOnly;
    private readonly ModuleBuilder _moduleBuilder;
    private readonly TypeMapper _typeMapper;
    private readonly TypeEmitterRegistry _typeEmitterRegistry = new();  // Type-first method dispatch registry
    private readonly BuiltInModuleEmitterRegistry _builtInModuleEmitterRegistry = new();  // Built-in module emitters
    private readonly Dictionary<string, string> _builtInModuleNamespaces = [];  // Variable name -> module name for direct dispatch
    // Per-owning-module local-name → (module, method) bindings for named imports.
    // Keyed by the importing module's path so that two stdlib modules aliasing the same
    // primitive under the same local name (e.g. both os.ts and process.ts using `__platform`)
    // don't collide in a shared flat dictionary.
    // Null key = single-module / global bindings (legacy single-module Compile path).
    private readonly Dictionary<string, Dictionary<string, (string ModuleName, string MethodName)>> _builtInModuleMethodBindingsByModule = [];
    private static readonly Dictionary<string, (string ModuleName, string MethodName)> _emptyBindings = [];
    private readonly HashSet<string> _importedNames = [];  // Every named / default / namespace import, any module source. Used to shadow global call handlers.
    private TypeBuilder _programType = null!;

    // Organized state containers (see ILCompiler.State.cs for definitions)
    private readonly ClassCompilationState _classes = new();
    private readonly FunctionCompilationState _functions = new();
    private readonly ClosureCompilationState _closures = new();
    private readonly AsyncCompilationState _async = new();
    private readonly GeneratorCompilationState _generators = new();
    private readonly AsyncGeneratorCompilationState _asyncGenerators = new();
    private readonly ModuleCompilationState _modules = new();
    private readonly EnumCompilationState _enums = new();
    private readonly ClassExpressionCompilationState _classExprs = new();
    private readonly TypedInteropState _typedInterop = new();
    private readonly LockDecoratorState _locks = new();

    // Registry services
    private ClassRegistry? _classRegistry;

    // Namespace support: namespace path -> static field
    private readonly Dictionary<string, FieldBuilder> _namespaceFields = [];

    // Emitted runtime (for standalone DLLs)
    private EmittedRuntime _runtime = null!;

    /// <summary>
    /// Reasons this compilation emitted SharpTS-runtime late binding whose normal execution
    /// needs SharpTS.dll present (e.g. "eval()", "Proxy", "Intl"). Empty ⇒ fully standalone.
    /// Valid after <see cref="Compile"/> / <see cref="CompileModules"/> have run. The CLI uses
    /// this to decide whether to co-locate SharpTS.dll with the output.
    /// </summary>
    public IReadOnlyCollection<string> RequiredSharpTSRuntimeReasons =>
        _runtime?.RequiredSharpTSRuntimeReasons ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>
    /// Non-fatal compilation warnings (e.g. an unresolvable external .NET type).
    /// Collected instead of written to Console so embedders observe them and the
    /// compiler never interleaves with the compiled program's own output; the
    /// CLI prints them to stderr after compilation.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;
    private readonly List<string> _warnings = [];

    internal void AddWarning(string message) => _warnings.Add(message);

    /// <summary>
    /// The assemblies of every external .NET type the compilation bound (@DotNetType
    /// classes and dotnet: imports). These are HARD metadata references in the emitted
    /// IL; the CLI intersects them with the sharpts.json / -r reference set to decide
    /// which third-party DLLs to co-locate with the output.
    /// </summary>
    public IReadOnlyCollection<System.Reflection.Assembly> ExternalInteropAssemblies =>
        _typeMapper.ExternalTypes.Values.Select(t => t.Assembly).Distinct().ToArray();

    // Type information from static analysis
    private TypeMap _typeMap = null!;

    // Dead code analysis results
    private DeadCodeInfo? _deadCodeInfo;

    // Strict mode setting (from "use strict" directive)
    private bool _isStrictMode;

    // Runtime feature gating result. Populated by Compile via RuntimeFeatureDetector
    // unless a caller (CompileModules, tests) has set it explicitly first.
    private RuntimeFeatureSet? _features;

    /// <summary>
    /// Pre-set the runtime feature flags before <see cref="Compile"/> runs. If unset,
    /// <see cref="Compile"/> derives them from the AST via <see cref="RuntimeFeatureDetector"/>.
    /// Tests / benchmarks use this to reuse a detected set across multiple compile invocations.
    /// </summary>
    public void SetRuntimeFeatures(RuntimeFeatureSet features) => _features = features;

    private UnionTypeGenerator? _unionGenerator;

    // Shared context for definition phase (module name resolution)
    private CompilationContext? _definitionContext;

    // Top-level variables captured by async functions (need to be static fields).
    // Union across all modules; kept for closure analysis and as a lookup source
    // when building module-scoped snapshots. Emission contexts do NOT use this
    // dict directly — see _moduleTopLevelStaticVars.
    private readonly Dictionary<string, FieldBuilder> _topLevelStaticVars = [];

    // Per-module top-level var/const fields (non-captured only). Captured vars
    // live on the shared entry-point display class. This split scopes plain
    // top-level bindings correctly — a `const foo` in main.ts no longer leaks
    // into lib.ts's resolver.
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _moduleTopLevelStaticVars = [];

    // Function name -> owning module path. Used to restore _modules.CurrentPath
    // during Phase-7 body emission so per-module lookups resolve against the
    // right module's storage.
    //
    // Keying differs by flavor, matching how each reader looks the entry up:
    //  * async / generator / async-generator functions register via
    //    RegisterStateMachineFunctionModule keyed by the module-QUALIFIED name,
    //    because the Phase-7 state-machine emission loops iterate the
    //    *.StateMachines registries (also keyed by the qualified name since #418)
    //    and restore the module by that same key.
    //  * sync functions register keyed by the SIMPLE name (Functions.cs).
    // These flavors also populate _modules.FunctionToModule (keyed by simple name)
    // so ResolveFunctionName qualifies call-site / value references. Pre-#418 the
    // state-machine flavors used simple stub names and were deliberately kept out of
    // FunctionToModule; #418 qualified their stubs, making that entry both safe and
    // required to disambiguate same-named functions across modules.
    //
    // For script files (which share global scope), the stored path IS the
    // script's own path; <see cref="NormalizeToEmissionPath"/> translates it
    // back to <c>null</c> before use so captured-var lookups land in the
    // SingleFile bucket that registration used.
    private readonly Dictionary<string, string> _functionDefinitionModule = [];

    // Tracks which module paths belong to script files. Kept as a set so
    // NormalizeToEmissionPath can fold script paths back to null when
    // restoring _modules.CurrentPath.
    private readonly HashSet<string> _scriptModulePaths = [];

    /// <summary>
    /// Folds script module paths back to <c>null</c> so emission-time lookups
    /// match the registration-time key used in script mode (captures,
    /// top-level static vars, etc. all share the SingleFile bucket for scripts).
    /// </summary>
    private string? NormalizeToEmissionPath(string? path)
    {
        if (path != null && _scriptModulePaths.Contains(path))
        {
            return null;
        }
        return path;
    }

    // Entry point
    private MethodBuilder? _entryPoint;

    // Type provider for resolving .NET types (always runtime types for compilation)
    private readonly TypeProvider _types;

    // Whether to post-process the assembly for reference assembly compatibility
    private readonly bool _useReferenceAssemblies;
    private readonly string? _sdkPath;

    // External assembly references for @DotNetType support
    private AssemblyReferenceLoader? _referenceLoader;

    // Output target type (DLL or EXE)
    private readonly OutputTarget _outputTarget;

    // Simple name of the emitted assembly, used to derive a default PDB file name.
    private readonly string _assemblyName;

    /// <summary>
    /// When set, <see cref="SaveArtifacts"/> and <see cref="Save(string)"/> also produce a portable
    /// PDB describing the emitted IL, and stamp a matching debug directory into the assembly.
    /// Off by default: non-debug builds pay none of the PDB cost.
    /// </summary>
    public bool EmitDebugSymbols { get; set; }

    // Source documents and sequence points gathered while emitting, when EmitDebugSymbols is set.
    private readonly DebugInfoCollector _debugInfo = new();

    // Symbol scopes, by the same normalized module path emission already tracks, plus the scope for
    // a single-file compile (which has no module path). Null unless EmitDebugSymbols is set, which
    // is what makes symbol emission free otherwise.
    private Dictionary<string, DebugEmitScope>? _debugScopesByModule;
    private DebugEmitScope? _entryPointDebugScope;

    /// <summary>
    /// The document whose statements are being emitted right now.
    /// </summary>
    /// <remarks>
    /// Resolved from <c>_modules.CurrentPath</c>, which every emission phase already sets and clears
    /// around each module, rather than from a parallel "current document" that each phase would have
    /// to remember to maintain. Falls back to the entry-point scope, which is what a single-file
    /// compile uses.
    /// </remarks>
    private DebugEmitScope? CurrentDebugScope
    {
        get
        {
            if (_modules.CurrentPath is { } path
                && _debugScopesByModule?.TryGetValue(path, out var scope) == true)
            {
                return scope;
            }
            return _entryPointDebugScope;
        }
    }

    /// <summary>
    /// Sink for source documents and sequence points. Emitters record into it only while
    /// <see cref="EmitDebugSymbols"/> is set; otherwise it stays empty and costs nothing.
    /// </summary>
    internal DebugInfoCollector DebugInfo => _debugInfo;

    /// <summary>
    /// Declares which source file the statements emitted from now on came from.
    /// </summary>
    /// <remarks>
    /// Compilation proceeds one module at a time, so the document is ambient rather than carried on
    /// every statement — a per-node map would cost every build to serve only debug ones. Callers
    /// that compile several modules set this before emitting each. Passing null (or building
    /// without <see cref="EmitDebugSymbols"/>) means the code that follows gets no sequence points,
    /// which is the right outcome for generated code with no source of its own.
    /// </remarks>
    public void SetSourceDocument(SourceDocument? document)
    {
        _entryPointDebugScope = CreateDebugScope(document);
    }

    /// <summary>
    /// Indexes each module's source document by the path emission uses, so every body compiled for
    /// that module resolves to the right file without each phase tracking it.
    /// </summary>
    private void RegisterModuleDocuments(List<ParsedModule> modules)
    {
        if (!EmitDebugSymbols) return;

        _debugScopesByModule = [];
        DebugEmitScope? entryScope = null;

        foreach (var module in modules)
        {
            if (CreateDebugScope(module.Document) is not { } scope) continue;

            // Phases are not consistent about whether CurrentPath is normalized, so index both
            // spellings rather than depending on which one a given phase happens to use.
            _debugScopesByModule[NormalizeToEmissionPath(module.Path)] = scope;
            _debugScopesByModule[module.Path] = scope;
            entryScope = scope;
        }

        // Modules arrive in dependency order, so the last is the entry module. Its statements are
        // what the entry point emits, and that emission runs with no module path set.
        _entryPointDebugScope = entryScope;
    }

    /// <summary>
    /// Marks the assembly as debuggable with optimizations disabled, so the JIT keeps the emitted
    /// IL's shape and a debugger sees the locals and stepping order the PDB describes.
    /// </summary>
    /// <remarks>
    /// Applied only when symbols are requested. Without it the JIT is free to reorder and elide,
    /// and breakpoints bind to instructions that no longer correspond to the recorded offsets.
    /// </remarks>
    private void ApplyDebuggableAttribute()
    {
        var constructor = typeof(System.Diagnostics.DebuggableAttribute)
            .GetConstructor([typeof(System.Diagnostics.DebuggableAttribute.DebuggingModes)])!;

        _assemblyBuilder.SetCustomAttribute(new CustomAttributeBuilder(
            constructor,
            [System.Diagnostics.DebuggableAttribute.DebuggingModes.Default
                | System.Diagnostics.DebuggableAttribute.DebuggingModes.DisableOptimizations]));
    }

    private DebugEmitScope? CreateDebugScope(SourceDocument? document)
    {
        if (!EmitDebugSymbols || document is null) return null;

        return new DebugEmitScope(
            _debugInfo,
            // Embedded stdlib and other virtual documents have no file a debugger could open, so
            // their text travels inside the PDB instead of being referenced by path.
            _debugInfo.AddDocument(document.Path, document.Text, embedSource: document.IsVirtual),
            document.Spans,
            document.Lines);
    }

    /// <summary>
    /// Creates a new IL compiler with default settings (runtime assembly mode).
    /// </summary>
    public ILCompiler(string assemblyName, bool preserveConstEnums = false)
        : this(assemblyName, preserveConstEnums, useReferenceAssemblies: false, sdkPath: null, metadata: null, references: null)
    {
    }

    /// <summary>
    /// Creates a new IL compiler with optional reference assembly support.
    /// </summary>
    /// <param name="assemblyName">Name for the output assembly.</param>
    /// <param name="preserveConstEnums">Whether to preserve const enums in output.</param>
    /// <param name="useReferenceAssemblies">If true, post-processes output for compile-time referenceability.</param>
    /// <param name="sdkPath">Optional explicit path to SDK reference assemblies.</param>
    public ILCompiler(string assemblyName, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath)
        : this(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata: null, references: null)
    {
    }

    /// <summary>
    /// Creates a new IL compiler with optional reference assembly support and assembly metadata.
    /// </summary>
    /// <param name="assemblyName">Name for the output assembly.</param>
    /// <param name="preserveConstEnums">Whether to preserve const enums in output.</param>
    /// <param name="useReferenceAssemblies">If true, post-processes output for compile-time referenceability.</param>
    /// <param name="sdkPath">Optional explicit path to SDK reference assemblies.</param>
    /// <param name="metadata">Optional assembly metadata for version and attributes.</param>
    public ILCompiler(string assemblyName, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, AssemblyMetadata? metadata)
        : this(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata, references: null)
    {
    }

    /// <summary>
    /// Creates a new IL compiler with optional reference assembly support, assembly metadata, and external references.
    /// </summary>
    /// <param name="assemblyName">Name for the output assembly.</param>
    /// <param name="preserveConstEnums">Whether to preserve const enums in output.</param>
    /// <param name="useReferenceAssemblies">If true, post-processes output for compile-time referenceability.</param>
    /// <param name="sdkPath">Optional explicit path to SDK reference assemblies.</param>
    /// <param name="metadata">Optional assembly metadata for version and attributes.</param>
    /// <param name="references">Optional list of external assembly paths for @DotNetType support.</param>
    public ILCompiler(string assemblyName, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, AssemblyMetadata? metadata, IReadOnlyList<string>? references)
        : this(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata, references, OutputTarget.Dll)
    {
    }

    /// <summary>
    /// Creates a new IL compiler with all options including output target type.
    /// </summary>
    /// <param name="assemblyName">Name for the output assembly.</param>
    /// <param name="preserveConstEnums">Whether to preserve const enums in output.</param>
    /// <param name="useReferenceAssemblies">If true, post-processes output for compile-time referenceability.</param>
    /// <param name="sdkPath">Optional explicit path to SDK reference assemblies.</param>
    /// <param name="metadata">Optional assembly metadata for version and attributes.</param>
    /// <param name="references">Optional list of external assembly paths for @DotNetType support.</param>
    /// <param name="target">Output target type: DLL (class library) or EXE (executable).</param>
    public ILCompiler(string assemblyName, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, AssemblyMetadata? metadata, IReadOnlyList<string>? references, OutputTarget target)
        : this(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata, references, target, inMemoryOnly: false)
    {
    }

    /// <summary>
    /// Creates an in-memory compiler that skips PE-format save/load entirely.
    /// Test-only fast path — the resulting assembly is invokable directly via
    /// <see cref="GetEmittedAssembly"/> but cannot be saved to disk.
    /// Saves ~150ms per compile (the cost of <see cref="PersistedAssemblyBuilder.GenerateMetadata"/>
    /// + <see cref="Assembly.Load(byte[])"/> + PE parsing).
    /// </summary>
    public static ILCompiler CreateInMemory(string assemblyName) =>
        new(assemblyName, preserveConstEnums: false, useReferenceAssemblies: false,
            sdkPath: null, metadata: null, references: null, OutputTarget.Dll, inMemoryOnly: true);

    /// <summary>
    /// Full constructor with the in-memory-only flag exposed.
    /// </summary>
    public ILCompiler(string assemblyName, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, AssemblyMetadata? metadata, IReadOnlyList<string>? references, OutputTarget target, bool inMemoryOnly)
    {
        _useReferenceAssemblies = useReferenceAssemblies;
        _sdkPath = sdkPath;
        _outputTarget = target;
        _inMemoryOnly = inMemoryOnly;
        _assemblyName = assemblyName;

        // Initialize reference loader if external assemblies are provided
        if (references != null && references.Count > 0)
        {
            _referenceLoader = new AssemblyReferenceLoader(references, sdkPath);
        }

        // Always use runtime types for compilation.
        // When needed, Save() post-processes metadata to rewrite assembly references
        // for ref-asm output and to strip leaked SharpTS assembly references.
        // This is necessary because MetadataLoadContext types cannot be used with
        // TypeBuilder.DefineType() for interface implementation (async/generator types).
        _types = TypeProvider.Runtime;

        // Create AssemblyName with version if metadata is provided
        var asmName = new AssemblyName(assemblyName);
        if (metadata?.Version != null)
        {
            asmName.Version = metadata.Version;
        }

        // In-memory mode skips the PE-format roundtrip used for shipping a standalone DLL.
        // PersistedAssemblyBuilder is needed when SaveToBytes is called (it produces the
        // PE bytes); the legacy in-memory AssemblyBuilder produces a directly-loadable
        // dynamic assembly with no save support, ~150x faster end-to-end on small
        // tests. See the perf-probe in .perf-probe/Program.cs.
        _assemblyBuilder = _inMemoryOnly
            ? AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run)
            : new PersistedAssemblyBuilder(asmName, _types.CoreAssembly);

        // Apply assembly-level attributes if metadata is provided
        if (metadata != null)
        {
            foreach (var attr in AssemblyAttributeBuilder.BuildAll(metadata))
            {
                _assemblyBuilder.SetCustomAttribute(attr);
            }
        }

        _moduleBuilder = _assemblyBuilder.DefineDynamicModule(assemblyName);
        _typeMapper = new TypeMapper(_moduleBuilder, _types);
    }

    /// <summary>
    /// Gets a shared CompilationContext for the definition phase (module name resolution).
    /// Lazily creates the context and updates CurrentModulePath on each call.
    /// </summary>
    private CompilationContext GetDefinitionContext()
    {
        _definitionContext ??= CreateDefinitionPhaseContext();
        _definitionContext.CurrentModulePath = _modules.CurrentPath;
        // Keep the definition context's namespace in sync so GetQualifiedFunctionName produces
        // namespace-qualified keys while a namespace's members are being defined/collected (#657).
        _definitionContext.CurrentNamespacePath = _currentNamespacePath;
        _definitionContext.DotNetNamespace = _modules.CurrentDotNetNamespace;
        _definitionContext.IsStrictMode = _isStrictMode;
        return _definitionContext;
    }

    /// <summary>
    /// Gets the shared ClassRegistry instance, creating it if necessary.
    /// The registry wraps all class-related state containers for centralized lookups.
    /// </summary>
    private ClassRegistry GetClassRegistry()
    {
        return _classRegistry ??= new ClassRegistry(
            builders: _classes.Builders,
            superclass: _classes.Superclass,
            constructors: _classes.Constructors,
            instanceMethods: _classes.InstanceMethods,
            instanceGetters: _classes.InstanceGetters,
            instanceSetters: _classes.InstanceSetters,
            staticFields: _classes.StaticFields,
            staticMethods: _classes.StaticMethods,
            staticGetters: _classes.StaticGetters,
            staticSetters: _classes.StaticSetters,
            genericParams: _classes.GenericParams,
            privateFieldStorage: _classes.PrivateFieldStorage,
            privateFieldNames: _classes.PrivateFieldNames,
            staticPrivateFields: _classes.StaticPrivateFields,
            privateMethods: _classes.PrivateMethods,
            staticPrivateMethods: _classes.StaticPrivateMethods,
            classToModule: _modules.ClassToModule,
            getCurrentModulePath: () => _modules.CurrentPath,
            getDotNetNamespace: () => _modules.CurrentDotNetNamespace
        );
    }

    /// <summary>
    /// Extracts the .NET namespace from @Namespace file directive if present.
    /// </summary>
    private static string? ExtractNamespaceFromStatements(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.FileDirective directive)
            {
                foreach (var decorator in directive.Decorators)
                {
                    if (decorator.Expression is Expr.Call call &&
                        call.Callee is Expr.Variable v &&
                        v.Name.Lexeme == "Namespace" &&
                        call.Arguments.Count == 1 &&
                        call.Arguments[0] is Expr.Literal { Value: string ns })
                    {
                        return ns;
                    }
                }
            }
        }
        return null;
    }

    public void Compile(List<Stmt> statements, TypeMap typeMap, DeadCodeInfo? deadCodeInfo = null)
    {
        _typeMap = typeMap;
        _deadCodeInfo = deadCodeInfo;

        // Check for "use strict" directive at file level
        _isStrictMode = Parsing.DirectivePrologue.HasUseStrict(statements);

        // Relocate non-capturing nested generator/async/state-machine-nested function declarations
        // to the module top level so the mature top-level state-machine pipeline can lower them
        // (#470, #501). Compile-path only — the interpreter handles nested declarations natively.
        statements = NestedFunctionLifter.Lift(statements, _entryPointDebugScope?.Spans);

        // Walk the AST to determine which runtime feature categories the program
        // actually needs, so Phase 1 can skip emitting unused helper types.
        // The override `_runtimeFeatures` (set by callers that have already detected,
        // e.g. CompileModules over multiple files) wins.
        _features ??= new RuntimeFeatureDetector().Detect(statements);

        Phase0_ExtractNamespace(statements);
        Phase1_EmitRuntimeTypes();
        Phase2_AnalyzeClosures(statements);
        ArrayLocalPromotionAnalyzer.Analyze(statements, _typeMap, _closures.Analyzer);
        StringAccumulatorPromotionAnalyzer.Analyze(statements, _typeMap, _closures.Analyzer);
        NonEscapingArrowLocalAnalyzer.Analyze(statements, _closures.DirectCallArrowBindings, _closures.Analyzer);
        ObjectLocalPromotionAnalyzer.Analyze(statements, _typeMap, _closures.Analyzer);
        Phase3_CreateProgramType();
        DefineObjectShapeTypes();
        DefineHoistedRegexFields(statements);
        PreScanBuiltInModuleImports(statements);
        Phase4_DefineDeclarations(statements);
        Phase5_CollectArrowFunctions(statements);
        Phase6_EmitArrowAndStateMachineBodies(statements);
        Phase7_EmitMethodBodies(statements);
        Phase8_EmitEntryPoint(statements);
        Phase9_FinalizeTypes();
    }

    #region Compile Phases

    /// <summary>
    /// Phase 0: Extract .NET namespace from @Namespace file directive.
    /// </summary>
    private void Phase0_ExtractNamespace(List<Stmt> statements)
    {
        _modules.CurrentDotNetNamespace = ExtractNamespaceFromStatements(statements);
    }

    /// <summary>
    /// Phase 1: Emit runtime support types into the generated assembly.
    /// This makes compiled DLLs standalone without requiring SharpTS.dll.
    /// </summary>
    private void Phase1_EmitRuntimeTypes()
    {
        _runtime = new RuntimeEmitter(_types) { EntryModulePath = _entryModulePath }
            .EmitAll(_moduleBuilder, _features ?? RuntimeFeatureSet.EmitEverything());
        _typeMapper.SetRuntime(_runtime);
    }

    // Absolute path of the entry module (last in dependency order), baked into the
    // emitted ClusterFork helper so compiled cluster.fork() knows which script its
    // interpreted workers re-execute. Null for single-script Compile().
    private string? _entryModulePath;

    /// <summary>
    /// Phase 2: Analyze closures to detect captured variables.
    /// </summary>
    private void Phase2_AnalyzeClosures(List<Stmt> statements)
    {
        _closures.Analyzer = new ClosureAnalyzer();
        _closures.Analyzer.Analyze(statements);
    }

    /// <summary>
    /// Phase 3: Create the main program type for top-level code.
    /// </summary>
    private void Phase3_CreateProgramType()
    {
        _programType = _moduleBuilder.DefineType(
            "$Program",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed
        );
    }

    /// <summary>
    /// Pre-scans statements for built-in module imports and registers them
    /// in _builtInModuleNamespaces. This must happen before function bodies
    /// are emitted so that calls like os.platform() can be properly dispatched.
    ///
    /// <paramref name="owningModulePath"/> is the path of the module that owns
    /// these imports — named-import bindings are stored per-owning-module so
    /// that two stdlib modules aliasing the same primitive to the same local
    /// name don't clobber each other (e.g. os.ts and process.ts both using
    /// __platform for their respective primitive:os and primitive:process
    /// imports). Pass null in the single-module Compile path.
    /// </summary>
    private void PreScanBuiltInModuleImports(List<Stmt> statements, string? owningModulePath = null)
    {
        var bindings = GetOrCreateBuiltInMethodBindings(owningModulePath);

        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Import import && !import.IsTypeOnly)
            {
                // Record every imported local name — call handlers for globally-
                // intercepted names (TimerHandler, FetchHandler) need to know
                // when a name is imported to avoid shadowing stdlib TS re-exports.
                if (import.DefaultImport != null) _importedNames.Add(import.DefaultImport.Lexeme);
                if (import.NamespaceImport != null) _importedNames.Add(import.NamespaceImport.Lexeme);
                if (import.NamedImports != null)
                {
                    foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
                    {
                        _importedNames.Add(spec.LocalName?.Lexeme ?? spec.Imported.Lexeme);
                    }
                }

                // Check if the import path is a built-in module or an stdlib-internal
                // primitive. Both dispatch through BuiltInModuleEmitterRegistry — the
                // primitive case's key is the full specifier (e.g. "primitive:os").
                string? builtInModuleName = Runtime.BuiltIns.Modules.BuiltInModuleRegistry.IsBuiltIn(import.ModulePath)
                    ? import.ModulePath  // Use the module path directly as the module name
                    : Modules.Stdlib.PrimitiveRegistry.IsPrimitive(import.ModulePath)
                        ? import.ModulePath  // "primitive:os" is its own emitter key
                        : Runtime.BuiltIns.Modules.BuiltInModuleRegistry.GetModuleName(import.ModulePath);  // Try sentinel path
                if (builtInModuleName != null)
                {
                    // Default import: import os from 'os' -> os maps to "os"
                    if (import.DefaultImport != null)
                    {
                        _builtInModuleNamespaces[import.DefaultImport.Lexeme] = builtInModuleName;
                    }

                    // Namespace import: import * as os from 'os' -> os maps to "os"
                    if (import.NamespaceImport != null)
                    {
                        _builtInModuleNamespaces[import.NamespaceImport.Lexeme] = builtInModuleName;
                    }

                    // Named imports: create static fields so they're accessible from functions
                    // Also track bindings for direct dispatch (avoids TSFunction reflection issues)
                    if (import.NamedImports != null)
                    {
                        foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
                        {
                            string importedName = spec.Imported.Lexeme;
                            string localName = spec.LocalName?.Lexeme ?? importedName;

                            // Track method binding for direct dispatch (scoped to this module)
                            bindings[localName] = (builtInModuleName, importedName);

                            if (!_topLevelStaticVars.ContainsKey(localName))
                            {
                                var field = _programType.DefineField(
                                    $"$builtInImport_{localName}",
                                    _types.Object,
                                    FieldAttributes.Public | FieldAttributes.Static);
                                _topLevelStaticVars[localName] = field;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns the flat local-name → (module, method) binding map for the given
    /// owning module path, creating it on demand.
    /// </summary>
    private Dictionary<string, (string ModuleName, string MethodName)> GetOrCreateBuiltInMethodBindings(string? owningModulePath)
    {
        string key = owningModulePath ?? "";
        if (!_builtInModuleMethodBindingsByModule.TryGetValue(key, out var bindings))
        {
            bindings = [];
            _builtInModuleMethodBindingsByModule[key] = bindings;
        }
        return bindings;
    }

    /// <summary>
    /// Returns the binding map that call-site emission should consult, scoped
    /// to whichever module is currently being emitted. Falls back to the
    /// single-module (null-key) bindings when no module is active.
    /// </summary>
    private Dictionary<string, (string ModuleName, string MethodName)> GetCurrentBuiltInMethodBindings()
    {
        string? currentPath = _modules.CurrentPath;
        if (currentPath != null && _builtInModuleMethodBindingsByModule.TryGetValue(currentPath, out var scoped))
            return scoped;
        if (_builtInModuleMethodBindingsByModule.TryGetValue("", out var global))
            return global;
        return _emptyBindings;
    }

    /// <summary>
    /// Phase 4: Define all class, function, enum, and namespace declarations.
    /// Also initializes typed interop and type emitter registries.
    /// </summary>
    private void Phase4_DefineDeclarations(List<Stmt> statements)
    {
        // Define all declarations. Delegates to the shared per-statement helper so
        // single-file (script) mode unwraps `export`-wrapped declarations exactly like
        // the module path does — without it, `export function f() {}` never defines its
        // MethodBuilder and call sites can't resolve `f` (issue #417).
        foreach (var stmt in statements)
        {
            DefineDeclarationFromStatement(stmt);
        }

        // Alias namespace function import aliases to their targets now that every namespace
        // member function is in the registry (#657).
        ResolveNamespaceImportAliasFunctions();

        // Define static fields for top-level variables captured by async functions
        DefineTopLevelCapturedVariables(statements);

        // Initialize typed interop support
        InitializeTypedInterop();

        // Initialize type emitter registries
        InitializeTypeEmitterRegistries();
    }

    /// <summary>
    /// Phase 5: Collect all arrow functions and define class expressions.
    /// </summary>
    private void Phase5_CollectArrowFunctions(List<Stmt> statements)
    {
        CollectAndDefineArrowFunctions(statements);
        DefineInnerFunctions();
        DefineTopLevelAsyncArrows(); // Define state machines for top-level async arrows
        DefineClassExpressionTypes();
        DefineClassExpressionMethods();
    }

    /// <summary>
    /// Phase 6: Emit arrow function bodies and async/generator state machine bodies.
    /// </summary>
    private void Phase6_EmitArrowAndStateMachineBodies(List<Stmt> statements)
    {
        // Pre-define class methods (including constructors) BEFORE arrow bodies so that
        // `new Foo(...)` inside an arrow can resolve to the typed ctor path. Otherwise
        // the arrow emitter falls through to the fallback and silently emits ldnull
        // because ClassRegistry.GetConstructorByQualifiedName hasn't been populated yet.
        DefineAllClassMethods(statements);

        EmitArrowFunctionBodies();
        EmitInnerFunctionBodies();

        // Emit $IHasFields interface method bodies now that method definitions are available
        // This uses compile-time dispatch (no runtime reflection)
        EmitAllHasFieldsInterfaceMethodBodies(statements);

        EmitAsyncStateMachineBodies();
        EmitTopLevelAsyncArrowBodies(); // Emit MoveNext for top-level async arrows
        EmitGeneratorStateMachineBodies();
        EmitAsyncGeneratorStateMachineBodies();
    }

    /// <summary>
    /// Phase 7: Emit method bodies for all classes, functions, and namespaces.
    /// </summary>
    private void Phase7_EmitMethodBodies(List<Stmt> statements)
    {
        // Delegates to the shared per-statement helper so single-file (script) mode
        // unwraps `export`-wrapped declarations like the module path does — otherwise an
        // exported function's body is never emitted and its call site can't resolve the
        // name (issue #417).
        foreach (var stmt in statements)
        {
            EmitMethodBodyFromStatement(stmt);
        }

        EmitClassExpressionBodies();
    }

    /// <summary>
    /// Phase 8: Emit entry point (top-level statements).
    /// </summary>
    private void Phase8_EmitEntryPoint(List<Stmt> statements)
    {
        EmitEntryPoint(statements);
    }

    /// <summary>
    /// Defines one generated value-type <c>$Shape_N</c> struct per distinct promotable object-literal
    /// shape (#862), de-duplicated by canonical key. Each field becomes a typed public field
    /// (<c>number</c>→<c>double</c>, <c>boolean</c>→<c>bool</c>, <c>string</c>→<c>string</c>). The structs
    /// reference only BCL types, so compiled output stays standalone (no SharpTS.dll dependency). They are
    /// finalized (<c>CreateType</c>) at the top of the finalize phase, before any type that uses them.
    /// </summary>
    private void DefineObjectShapeTypes()
    {
        if (_typeMap == null) return;
        foreach (var shape in _typeMap.PromotableObjectLocalShapes)
        {
            if (_closures.ObjectShapes.ByKey.ContainsKey(shape.CanonicalKey)) continue;

            var structType = _moduleBuilder.DefineType(
                $"$Shape_{_closures.ObjectShapes.Counter++}",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
                _types.ValueType);

            var fieldBuilders = new Dictionary<string, FieldBuilder>(StringComparer.Ordinal);
            foreach (var (name, kind) in shape.Fields)
            {
                var clr = kind switch
                {
                    TokenType.TYPE_NUMBER => _types.Double,
                    TokenType.TYPE_BOOLEAN => _types.Boolean,
                    _ => _types.String,
                };
                fieldBuilders[name] = structType.DefineField(name, clr, FieldAttributes.Public);
            }

            var info = new ObjectShapeTypeInfo
            {
                ClrType = structType,
                Fields = shape.Fields.Select(f => (f.Name, f.Kind)).ToList(),
                FieldBuilders = fieldBuilders,
            };
            _closures.ObjectShapes.ByKey[shape.CanonicalKey] = info;
            _closures.ObjectShapes.ByClrType[structType] = info;
        }
    }

    /// <summary>
    /// Defines one <c>public static $RegExp</c> field on <c>$Program</c> per
    /// hoistable regex literal (<see cref="RegexLiteralHoistAnalyzer"/>), and
    /// records them on <see cref="EmittedRuntime.RegexHoistFields"/> for
    /// <c>EmitRegexLiteral</c>. Runs after <c>$Program</c> is created and before
    /// any body is emitted, so the fields exist when a literal is first emitted.
    /// Fields are <c>object</c>-typed (matching <c>CreateRegExpWithFlags</c>'s
    /// return) and not <c>initonly</c> — the lazy-init path writes them via
    /// <c>stsfld</c> on first evaluation. BCL/emitted-runtime only, so output
    /// stays standalone.
    /// </summary>
    private void DefineHoistedRegexFields(List<Stmt> statements)
    {
        var hoistable = RegexLiteralHoistAnalyzer.Analyze(statements);
        if (hoistable.Count == 0) return;

        var fields = new Dictionary<Parsing.Expr.RegexLiteral, FieldBuilder>(ReferenceEqualityComparer.Instance);
        int index = 0;
        foreach (var literal in hoistable)
        {
            fields[literal] = _programType.DefineField(
                $"$rx_{index++}",
                _types.Object,
                FieldAttributes.Public | FieldAttributes.Static);
        }
        _runtime.RegexHoistFields = fields;
    }

    /// <summary>
    /// Phase 9: Finalize all types by calling CreateType().
    /// </summary>
    private void Phase9_FinalizeTypes()
    {
        _unionGenerator?.FinalizeAllUnionTypes();

        // Finalize generated object-literal shape structs (#862) before any type that uses them.
        foreach (var info in _closures.ObjectShapes.ByKey.Values)
            ((TypeBuilder)info.ClrType).CreateType();

        // Finalize entry-point display class first (needed by closures)
        _closures.EntryPointDisplayClass?.CreateType();

        // Finalize function-level display classes
        foreach (var tb in _closures.FunctionDisplayClasses.Values)
        {
            tb.CreateType();
        }

        // Finalize arrow scope display classes
        foreach (var tb in _closures.ArrowScopeDisplayClasses.Values)
        {
            tb.CreateType();
        }

        // Finalize inner function display classes
        FinalizeInnerFunctionDisplayClasses();

        foreach (var tb in _closures.DisplayClasses.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _classes.Builders.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _classExprs.Builders.Values)
        {
            tb.CreateType();
        }
        _programType.CreateType();
    }

    /// <summary>
    /// Initializes typed interop support (union generator, type mapper).
    /// </summary>
    private void InitializeTypedInterop()
    {
        _unionGenerator = new UnionTypeGenerator(_typeMapper);
        _unionGenerator.UnionTypeInterface = _runtime.IUnionTypeInterface;
        _typeMapper.SetClassBuilders(_classes.Builders);
        _typeMapper.SetUnionGenerator(_unionGenerator);
    }

    /// <summary>
    /// Initializes type emitter registries for type-first method dispatch.
    /// </summary>
    private void InitializeTypeEmitterRegistries()
    {
        _typeEmitterRegistry.SetExternalTypes(_classes.ExternalTypes);

        // Instance type emitters
        var stringEmitter = new StringEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.String>(stringEmitter);
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.StringLiteral>(stringEmitter);
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Array>(new ArrayEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Tuple>(new ArrayEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Buffer>(new BufferEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.EventEmitter>(new EventEmitterEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Date>(new DateEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Map>(new MapEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Set>(new SetEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.WeakMap>(new WeakMapEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.WeakSet>(new WeakSetEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.WeakRef>(new WeakRefEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.FinalizationRegistry>(new FinalizationRegistryEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.RegExp>(new RegExpEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.AsyncGenerator>(new AsyncGeneratorEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Error>(new ErrorEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.SharedArrayBuffer>(new SharedArrayBufferEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.ArrayBuffer>(new ArrayBufferEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.DataView>(new DataViewEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.AbortController>(new AbortControllerEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.AbortSignal>(new AbortSignalEmitter());
        var iteratorEmitter = new IteratorEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Iterator>(iteratorEmitter);
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Generator>(iteratorEmitter);

        // Static type emitters
        _typeEmitterRegistry.RegisterStatic("Math", new MathStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("JSON", new JSONStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Object", new ObjectStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Array", new ArrayStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Buffer", new BufferStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Number", new NumberStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Promise", new PromiseStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Symbol", new SymbolStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Map", new MapStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("String", new StringStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Boolean", new BooleanStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("process", new ProcessStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("globalThis", new GlobalThisStaticEmitter(_typeEmitterRegistry));
        _typeEmitterRegistry.RegisterStatic("Atomics", new AtomicsStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("ArrayBuffer", new ArrayBufferStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Reflect", new ReflectStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Proxy", new ProxyStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("AbortSignal", new AbortSignalStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Response", new ResponseStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Iterator", new IteratorStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("RegExp", new RegExpStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("ReadableStream", new ReadableStreamStaticEmitter());

        // Built-in module emitters
        _builtInModuleEmitterRegistry.Register(new OsModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new FsModuleEmitter());
        // "path"         — migrated to stdlib/node/path.ts (pure-TS, uses primitive:process for cwd).
        // "querystring"  — migrated to stdlib/node/querystring.ts.
        // "assert"       — migrated to stdlib/node/assert.ts (pure-logic leaf).
        // "url"          — migrated to stdlib/node/url.ts (full WHATWG state machine).
        // "process"      — migrated to stdlib/node/process.ts which imports from primitive:process.
        //   ProcessModuleEmitter remains, registered only under the primitive specifier.
        var processEmitter = new ProcessModuleEmitter();
        _builtInModuleEmitterRegistry.RegisterAlias("primitive:process", processEmitter);
        _builtInModuleEmitterRegistry.Register(new CryptoModuleEmitter());
        // "util" — migrated to stdlib/node/util.ts (pure-TS port).
        // "readline" — migrated to stdlib/node/readline.ts; emitter registered under primitive:readline only.
        _builtInModuleEmitterRegistry.Register(new ReadlinePrimitiveEmitter());
        _builtInModuleEmitterRegistry.Register(new ChildProcessModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new BufferModuleEmitter());
        // "zlib" — migrated to stdlib/node/zlib.ts; emitter registered under primitive:zlib only.
        _builtInModuleEmitterRegistry.Register(new ZlibModuleEmitter());
        // "events" — migrated to stdlib/node/events.ts (pure-TS EventEmitter).
        // "timers" and "timers/promises" migrated to stdlib/node/timers{,/promises}.ts
        //   (TS facades over primitive:timers and primitive:timers/promises respectively).
        _builtInModuleEmitterRegistry.Register(new TimersPrimitiveEmitter());
        _builtInModuleEmitterRegistry.Register(new TimersPromisesPrimitiveEmitter());
        // "string_decoder" — migrated to stdlib/node/string_decoder.ts.
        // "perf_hooks" — migrated to stdlib/node/perf_hooks.ts (pure-TS over primitive:perf).
        //   Only the narrow now() method needs host access; mark/measure/observer are TS.
        _builtInModuleEmitterRegistry.Register(new PerfPrimitiveEmitter());
        _builtInModuleEmitterRegistry.Register(new StreamModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new StreamPromisesModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new StreamWebModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new HttpModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new WorkerThreadsModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new DnsModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new DnsPromisesModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new FsPromisesModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new NetModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new TlsModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new DgramModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new ClusterModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new VmModuleEmitter());
        // "async_hooks" migrated to stdlib/node/async_hooks.ts (TS class over primitive:async_hooks).
        _builtInModuleEmitterRegistry.Register(new AsyncHooksPrimitiveEmitter());
        // "tty" migrated to stdlib/node/tty.ts (pure-TS over primitive:tty).
        _builtInModuleEmitterRegistry.Register(new TtyPrimitiveEmitter());

        // https delegates to http emitter
        var httpsEmitter = new HttpModuleEmitter();
        _builtInModuleEmitterRegistry.Register(new HttpsModuleEmitterProxy());
    }

    #endregion

    /// <summary>
    /// Compiles multiple modules into a single merged DLL.
    /// </summary>
    /// <param name="modules">Modules in dependency order (dependencies first)</param>
    /// <param name="resolver">Module resolver for path resolution</param>
    /// <param name="typeMap">Type map from static analysis</param>
    /// <param name="deadCodeInfo">Optional dead code analysis results</param>
    public void CompileModules(List<ParsedModule> modules, ModuleResolver resolver, TypeMap typeMap, DeadCodeInfo? deadCodeInfo = null)
    {
        _typeMap = typeMap;
        _deadCodeInfo = deadCodeInfo;
        _modules.Resolver = resolver;
        // Entry module is last in dependency order (mirrors Interpreter.EntryModulePath).
        _entryModulePath = modules.Count > 0 ? Path.GetFullPath(modules[^1].Path) : null;

        RegisterModuleDocuments(modules);

        // Relocate non-capturing nested generator/async/state-machine-nested function declarations
        // to each module's top level (#470, #501). Compile-path only. The Statements property is
        // read-only but the underlying list is shared by reference across all phases, so mutate it
        // in place.
        foreach (var m in modules)
        {
            var lifted = NestedFunctionLifter.Lift(m.Statements, m.Document?.Spans);
            if (!ReferenceEquals(lifted, m.Statements))
            {
                m.Statements.Clear();
                m.Statements.AddRange(lifted);
            }
        }

        var allStatements = modules.SelectMany(m => m.Statements).ToList();

        // Detect features across the union of all modules' statements (any module
        // pulling in `crypto` makes the runtime emit crypto types).
        if (_features is null)
        {
            var detector = new RuntimeFeatureDetector();
            _features = detector.Detect(allStatements);
            if (modules.Any(module => module.IsCommonJs))
                _features.UsesCjsRequire = true;
        }

        ModulePhase0_ExtractNamespaces(modules);
        Phase1_EmitRuntimeTypes();
        Phase2_AnalyzeClosures(allStatements);
        ArrayLocalPromotionAnalyzer.Analyze(allStatements, _typeMap, _closures.Analyzer);
        StringAccumulatorPromotionAnalyzer.Analyze(allStatements, _typeMap, _closures.Analyzer);
        NonEscapingArrowLocalAnalyzer.Analyze(allStatements, _closures.DirectCallArrowBindings, _closures.Analyzer);
        ObjectLocalPromotionAnalyzer.Analyze(allStatements, _typeMap, _closures.Analyzer);
        Phase3_CreateProgramType();
        DefineObjectShapeTypes();
        DefineHoistedRegexFields(allStatements);
        // Scope each module's named-import bindings to that module so local
        // aliases like __platform don't collide between stdlib modules.
        foreach (var m in modules)
        {
            PreScanBuiltInModuleImports(m.Statements, m.Path);
        }
        ModulePhase4_DefineModuleTypes(modules);
        // Captured top-level vars continue to share the entry-point display class
        // (script-compatible closure semantics), but each module gets its own
        // FIELD on that class. Two modules that both declare `const x` no longer
        // clobber each other.
        AnalyzeCapturedTopLevelVarsAcrossModules(modules);
        foreach (var m in modules)
        {
            DefineModuleScopedTopLevelStaticFields(m.Statements, m.Path);
        }
        ModulePhase5_DefineDeclarations(modules);
        InitializeTypedInterop();
        InitializeTypeEmitterRegistries();
        ModulePhase6_CollectArrowFunctions(modules);
        ModulePhase7_EmitArrowBodies(modules);
        ModulePhase8_EmitMethodBodies(modules);
        ModulePhase9_EmitModuleInits(modules);
        ModulePhase10_EmitEntryPoint(modules);
        ModulePhase11_FinalizeTypes();
    }

    #region CompileModules Phases

    /// <summary>
    /// Module Phase 0: Extract .NET namespaces from @Namespace directives in each module.
    /// </summary>
    private void ModulePhase0_ExtractNamespaces(List<ParsedModule> modules)
    {
        foreach (var module in modules)
        {
            _modules.Namespaces[module.Path] = ExtractNamespaceFromStatements(module.Statements);
        }
    }

    /// <summary>
    /// Module Phase 4: Define module types with export fields.
    /// </summary>
    private void ModulePhase4_DefineModuleTypes(List<ParsedModule> modules)
    {
        foreach (var module in modules)
        {
            if (module.IsCommonJs)
            {
                DefineCommonJsModuleType(module);
            }
            else
            {
                DefineModuleType(module);
            }
        }
    }

    /// <summary>
    /// Module Phase 5: Define all class, function, enum, and namespace declarations across modules.
    /// </summary>
    private void ModulePhase5_DefineDeclarations(List<ParsedModule> modules)
    {
        foreach (var module in modules)
        {
            // NormalizeToEmissionPath, not module.Path: a script-like module's top-level
            // vars are registered under null (the shared global bucket) by
            // AnalyzeCapturedTopLevelVarsAcrossModules, so anything that resolves their
            // storage has to look there too. Declaring a function under module.Path
            // instead binds it to a per-module bucket its captures don't live in. (#1282)
            _modules.CurrentPath = NormalizeToEmissionPath(module.Path);
            _modules.CurrentDotNetNamespace = _modules.Namespaces.GetValueOrDefault(module.Path);

            foreach (var stmt in module.Statements)
            {
                DefineDeclarationFromStatement(stmt);
            }
        }
        _modules.CurrentPath = null;
        _modules.CurrentDotNetNamespace = null;

        // dotnet: imports register external types AFTER all user declarations so that
        // name-collision detection sees every class in the program (ClassToModule is
        // complete only once the loop above finishes).
        foreach (var module in modules)
        {
            RegisterDotNetImports(module);
        }

        // Alias namespace function import aliases to their targets now that every namespace
        // member function is in the registry (#657).
        ResolveNamespaceImportAliasFunctions();
    }

    /// <summary>
    /// Defines a declaration from a statement (class, function, enum, namespace, or export).
    /// </summary>
    private void DefineDeclarationFromStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Class classStmt:
                DefineClass(classStmt);
                break;
            case Stmt.Function funcStmt when funcStmt.Body != null:
                DefineFunction(funcStmt);
                break;
            case Stmt.Enum enumStmt:
                DefineEnum(enumStmt);
                break;
            case Stmt.Namespace nsStmt:
                DefineNamespaceFields(nsStmt);
                break;
            case Stmt.Export { Declaration: not null } export:
                DefineDeclarationFromStatement(export.Declaration);
                break;
        }
    }

    /// <summary>
    /// Module Phase 6: Collect all arrow functions and define class expressions.
    /// </summary>
    private void ModulePhase6_CollectArrowFunctions(List<ParsedModule> modules)
    {
        // Walk per module with _modules.CurrentPath set, so arrow collection
        // records each arrow's owning module for later body emission to restore
        // the right per-module state.
        foreach (var module in modules)
        {
            _modules.CurrentPath = NormalizeToEmissionPath(module.Path);
            CollectArrowsFromStatementsInCurrentModule(module.Statements);
        }
        _modules.CurrentPath = null;

        // These steps don't depend on per-module scope yet — they see all
        // collected arrows and operate cross-module.
        FinalizeArrowFunctionCollection();

        DefineInnerFunctions();
        DefineTopLevelAsyncArrows(); // Define state machines for top-level async arrows
        DefineClassExpressionTypes();
        DefineClassExpressionMethods();
    }

    /// <summary>
    /// Module Phase 7: Emit arrow function bodies and async/generator state machine bodies.
    /// Pre-defines class methods (incl. constructors) across all modules first so arrow
    /// bodies that contain <c>new Foo(...)</c> can resolve the typed ctor — otherwise
    /// ClassRegistry.GetConstructorByQualifiedName is empty and EmitNew falls through to
    /// the null fallback (silent miscompile).
    /// </summary>
    private void ModulePhase7_EmitArrowBodies(List<ParsedModule> modules)
    {
        foreach (var module in modules)
        {
            _modules.CurrentPath = NormalizeToEmissionPath(module.Path);
            DefineAllClassMethods(module.Statements);
        }
        _modules.CurrentPath = null;

        EmitArrowFunctionBodies();
        EmitInnerFunctionBodies();
        EmitAsyncStateMachineBodies();
        EmitTopLevelAsyncArrowBodies(); // Emit MoveNext for top-level async arrows
        EmitGeneratorStateMachineBodies();
        EmitAsyncGeneratorStateMachineBodies();
    }

    /// <summary>
    /// Module Phase 8: Emit method bodies for all modules.
    /// </summary>
    private void ModulePhase8_EmitMethodBodies(List<ParsedModule> modules)
    {
        // Class methods (signatures) were pre-defined in Phase7 so arrow bodies could see
        // them; DefineAllClassMethods is idempotent and safely re-enters here as a no-op
        // for classes whose methods were already registered.
        foreach (var module in modules)
        {
            _modules.CurrentPath = NormalizeToEmissionPath(module.Path);
            DefineAllClassMethods(module.Statements);
        }
        _modules.CurrentPath = null;

        // Now emit $IHasFields interface method bodies for all module classes
        // This must be done before EmitMethodBodyFromStatement since methods need to be defined
        foreach (var module in modules)
        {
            _modules.CurrentPath = NormalizeToEmissionPath(module.Path);
            EmitAllHasFieldsInterfaceMethodBodies(module.Statements);
        }
        _modules.CurrentPath = null;

        // Now emit the actual class/function method bodies
        foreach (var module in modules)
        {
            _modules.CurrentPath = NormalizeToEmissionPath(module.Path);
            foreach (var stmt in module.Statements)
            {
                EmitMethodBodyFromStatement(stmt);
            }
        }
        _modules.CurrentPath = null;

        EmitClassExpressionBodies();
    }

    /// <summary>
    /// Emits method body from a statement (class, function, or export).
    /// </summary>
    private void EmitMethodBodyFromStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Class classStmt:
                EmitClassMethods(classStmt);
                break;
            case Stmt.Function funcStmt when funcStmt.Body != null:
                EmitFunctionBody(funcStmt);
                EmitFunctionOverloads(funcStmt);
                break;
            case Stmt.Namespace nsStmt:
                // Mirrors the Stmt.Namespace arm in DefineDeclarationFromStatement so the
                // define/emit helpers stay symmetric. EmitNamespaceMemberBodies internally
                // unwraps exported members, so an `export namespace` reaches its members.
                EmitNamespaceMemberBodies(nsStmt);
                break;
            case Stmt.Export { Declaration: not null } export:
                EmitMethodBodyFromStatement(export.Declaration);
                break;
        }
    }

    /// <summary>
    /// Module Phase 9: Emit module initialization methods.
    /// </summary>
    private void ModulePhase9_EmitModuleInits(List<ParsedModule> modules)
    {
        foreach (var module in modules)
        {
            if (module.IsCommonJs)
            {
                EmitCommonJsModuleInit(module);
            }
            else
            {
                EmitModuleInit(module);
            }
        }
    }

    /// <summary>
    /// Module Phase 10: Emit entry point that initializes modules in order.
    /// </summary>
    private void ModulePhase10_EmitEntryPoint(List<ParsedModule> modules)
    {
        EmitModulesEntryPoint(modules);
    }

    /// <summary>
    /// Module Phase 11: Finalize all types including module types.
    /// </summary>
    private void ModulePhase11_FinalizeTypes()
    {
        // Run label validation BEFORE finalizing types — ILGenerator branch/label state is cleared
        // once CreateType() is called, so we need to catch unmarked-and-branched labels here.
        var allTypes = ILLabelValidator.AllTypesFromModule(_moduleBuilder).ToList();
        ILLabelValidator.SweepAllTypes(allTypes);
        ILLabelValidator.SweepConstructors(allTypes);

        _unionGenerator?.FinalizeAllUnionTypes();

        // Finalize generated object-literal shape structs (#862) before any type that uses them.
        foreach (var info in _closures.ObjectShapes.ByKey.Values)
            ((TypeBuilder)info.ClrType).CreateType();

        // Finalize entry-point display class first (needed by closures)
        _closures.EntryPointDisplayClass?.CreateType();

        // Finalize function-level display classes
        foreach (var tb in _closures.FunctionDisplayClasses.Values)
        {
            tb.CreateType();
        }

        // Finalize arrow scope display classes
        foreach (var tb in _closures.ArrowScopeDisplayClasses.Values)
        {
            tb.CreateType();
        }

        // Finalize inner function display classes
        FinalizeInnerFunctionDisplayClasses();

        foreach (var tb in _closures.DisplayClasses.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _classes.Builders.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _classExprs.Builders.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _modules.Types.Values)
        {
            tb.CreateType();
        }
        _programType.CreateType();
    }

    #endregion

    /// <summary>
    /// Materializes the compiled assembly as PE bytes — the form ALC's
    /// LoadFromStream consumes directly without any disk roundtrip. Used by
    /// the Test262 runner which loads thousands of compiled assemblies per
    /// minute; eliminating the temp-file write halves end-to-end per-test
    /// latency on contended file systems (Windows + Defender scanning every
    /// freshly written DLL is the killer case).
    /// </summary>
    /// <summary>
    /// Returns the live in-memory assembly. Only valid when constructed via
    /// <see cref="CreateInMemory"/> — the test-mode fast path that skips PE save/load.
    /// </summary>
    public Assembly GetEmittedAssembly()
    {
        if (!_inMemoryOnly)
            throw new InvalidOperationException(
                "GetEmittedAssembly() only works when ILCompiler was created with inMemoryOnly=true. " +
                "For shipping DLLs, use SaveToBytes() / Save(path) and Assembly.Load(bytes) instead.");
        return _assemblyBuilder;
    }

    /// <summary>
    /// Serializes the assembly, returning it together with debug symbols when
    /// <see cref="EmitDebugSymbols"/> is set.
    /// </summary>
    /// <param name="pdbPath">
    /// Path to record in the assembly's CodeView entry so debuggers can locate the PDB. Defaults to
    /// <c>&lt;assembly name&gt;.pdb</c>, which resolves next to the assembly.
    /// </param>
    /// <remarks>
    /// Symbols are attached after assembly-reference rewriting, not before: the rewriter rebuilds
    /// the PE without a debug directory. The PDB is therefore serialized against the row counts of
    /// the finished image, and the rewriter's row-for-row preservation of <c>MethodDef</c> is
    /// verified rather than assumed. See <see cref="Symbols.DebugDirectoryInjector"/>.
    /// </remarks>
    public CompilationArtifacts SaveArtifacts(string? pdbPath = null)
    {
        if (_inMemoryOnly)
            throw new InvalidOperationException(
                "SaveToBytes()/SaveArtifacts() are unavailable in inMemoryOnly mode. Use GetEmittedAssembly() to access " +
                "the live dynamic assembly directly, or construct ILCompiler without inMemoryOnly=true.");

        if (EmitDebugSymbols)
            ApplyDebuggableAttribute();

        // The PDB tables are built from _debugInfo rather than from GenerateMetadata's own pdb
        // builder — see DebugInfoCollector for why that overload cannot be used here.
        var persisted = (PersistedAssemblyBuilder)_assemblyBuilder;
        MetadataBuilder metadataBuilder = persisted.GenerateMetadata(
            out BlobBuilder ilStream,
            out BlobBuilder fieldData);

        PEHeaderBuilder peHeader = _outputTarget == OutputTarget.Exe
            ? PEHeaderBuilder.CreateExecutableHeader()
            : PEHeaderBuilder.CreateLibraryHeader();

        var entryPointHandle = _entryPoint != null
            ? MetadataTokens.MethodDefinitionHandle(_entryPoint.MetadataToken)
            : default;

        ManagedPEBuilder peBuilder = new(
            header: peHeader,
            metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
            ilStream: ilStream,
            mappedFieldData: fieldData,
            entryPoint: entryPointHandle);

        BlobBuilder peBlob = new();
        peBuilder.Serialize(peBlob);

        using var tempStream = new MemoryStream();
        peBlob.WriteContentTo(tempStream);
        tempStream.Position = 0;

        var hasSharpTsReference = HasAssemblyReference(tempStream, "SharpTS");
        tempStream.Position = 0;

        bool rewritingReferences = _useReferenceAssemblies || hasSharpTsReference;

        // Kept only to verify the rewriter preserved MethodDef identity, so it is not materialized
        // for builds that will not emit symbols.
        byte[]? beforeRewrite = EmitDebugSymbols && rewritingReferences ? tempStream.ToArray() : null;

        byte[] image;
        if (rewritingReferences)
        {
            var refAssemblyPath = _sdkPath ?? SdkResolver.FindReferenceAssembliesPath()
                ?? throw new CompileException(
                    "Could not find SDK reference assemblies for post-processing. " +
                    "Ensure the .NET SDK is installed.");

            tempStream.Position = 0;
            using var rewriter = new AssemblyReferenceRewriter(tempStream, refAssemblyPath);
            rewriter.Rewrite();

            using var outMem = new MemoryStream();
            rewriter.Save(outMem);
            image = outMem.ToArray();
        }
        else
        {
            image = tempStream.ToArray();
        }

        if (!EmitDebugSymbols)
            return new CompilationArtifacts(image, null, null);

        // The rewriter rebuilds the PE from scratch; prove it kept MethodDef identities before
        // describing the result with symbols derived from the pre-rewrite emit.
        if (beforeRewrite is not null)
            PdbEmitter.VerifyMethodMappingPreserved(beforeRewrite, image);

        pdbPath ??= _assemblyName + ".pdb";
        var pdbMetadata = _debugInfo.BuildPdbMetadata(
            metadataBuilder.GetRowCounts()[(int)TableIndex.MethodDef],
            PdbEmitter.ReadLocalSignatureRids(image));
        var pdb = PdbEmitter.Serialize(
            pdbMetadata,
            PdbEmitter.ReadTypeSystemRowCounts(image),
            entryPointHandle);

        image = DebugDirectoryInjector.Inject(
            image, pdb.ContentId, pdb.FormatVersion, pdbPath, pdb.Checksum, PdbEmitter.ChecksumAlgorithmName);

        return new CompilationArtifacts(image, pdb.Bytes, Path.GetFileName(pdbPath));
    }

    public byte[] SaveToBytes() => SaveArtifacts().Assembly;

    public void Save(string outputPath) => Save(outputPath, pdbPath: null);

    /// <summary>
    /// Writes the assembly to <paramref name="outputPath"/>, plus a portable PDB when
    /// <see cref="EmitDebugSymbols"/> is set.
    /// </summary>
    /// <param name="pdbPath">
    /// Where to write the PDB, and the path recorded in the assembly's CodeView entry. Defaults to
    /// <paramref name="outputPath"/> with a <c>.pdb</c> extension. EXE builds pass the final
    /// executable's location explicitly, because the assembly itself is emitted to a temporary
    /// file before being bundled.
    /// </param>
    public void Save(string outputPath, string? pdbPath)
    {
        if (EmitDebugSymbols)
            pdbPath ??= Path.ChangeExtension(outputPath, ".pdb");

        var artifacts = SaveArtifacts(pdbPath);

        File.WriteAllBytes(outputPath, artifacts.Assembly);
        if (artifacts.Pdb is not null)
            File.WriteAllBytes(pdbPath!, artifacts.Pdb);
    }

    private static bool HasAssemblyReference(Stream assemblyStream, string assemblyName)
    {
        using var peReader = new PEReader(assemblyStream, PEStreamOptions.LeaveOpen);
        var reader = peReader.GetMetadataReader();

        foreach (var asmRefHandle in reader.AssemblyReferences)
        {
            var asmRef = reader.GetAssemblyReference(asmRefHandle);
            if (string.Equals(reader.GetString(asmRef.Name), assemblyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Defines storage for all top-level (module-level) variables.
    /// Variables captured by closures use an entry-point display class for proper
    /// by-reference semantics. Non-captured variables use static fields.
    /// </summary>
    /// <remarks>
    /// When a top-level variable is captured by a closure, both the outer code and
    /// the closure must reference the same storage location. We achieve this by storing
    /// captured variables in a display class instance that's shared between the
    /// entry point and all closures that capture those variables.
    /// </remarks>
    private void DefineTopLevelCapturedVariables(List<Stmt> statements)
    {
        // Single-file compile path — treat the statements as one "module" with no path.
        // The display class is created lazily by RegisterCapturedTopLevelVarsForModule
        // only when there's at least one captured top-level var to register on it;
        // creating it unconditionally would emit an unused `<>c__EntryPointDisplayClass`
        // type into every compiled DLL.
        RegisterCapturedTopLevelVarsForModule(statements, modulePath: null);
        DefineModuleScopedTopLevelStaticFields(statements, modulePath: null);
    }

    /// <summary>
    /// Cross-module pass: populates per-module captured-top-level-var maps and
    /// defines one field per (module, varName) pair on the shared
    /// <c>&lt;&gt;c__EntryPointDisplayClass</c>. This prevents two modules that both
    /// declare <c>const x</c> from sharing storage — each module's <c>x</c> lives
    /// in its own qualified field (e.g. <c>a_ts__x</c>, <c>b_ts__x</c>).
    /// </summary>
    /// <remarks>
    /// Captures are still analyzed against the flat union AST (closures may refer
    /// across script-merged scopes), so <see cref="ClosureAnalyzer.IsVariableCaptured"/>
    /// sees names globally. But field allocation and context views are
    /// module-scoped so emissions route through the right storage.
    /// </remarks>
    private void AnalyzeCapturedTopLevelVarsAcrossModules(List<ParsedModule> modules)
    {
        foreach (var module in modules)
        {
            if (module.IsScript)
            {
                _scriptModulePaths.Add(module.Path);
            }
            // Scripts share global scope — register under null so they all land in
            // the SingleFile bucket, matching what EmitScriptInit looks up at
            // emission time (it sets _modules.CurrentPath = null before emission).
            string? registrationPath = module.IsScript ? null : module.Path;
            RegisterCapturedTopLevelVarsForModule(module.Statements, registrationPath);
        }
        // The display class is created lazily inside RegisterCapturedTopLevelVarsForModule
        // when there are captured vars to register. Don't force-create it here, or we
        // emit an unused `<>c__EntryPointDisplayClass` type into every compiled output.
    }

    /// <summary>
    /// For a single module (or the whole program in single-file mode), records which
    /// of its top-level var/const declarations are captured by any closure, and
    /// defines a uniquely-named field on the shared entry-point display class for
    /// each one. Field names are qualified by module path to keep same-named
    /// bindings in different modules independent.
    /// </summary>
    private void RegisterCapturedTopLevelVarsForModule(List<Stmt> statements, string? modulePath)
    {
        string key = modulePath ?? ClosureCompilationState.SingleFileKey;
        HashSet<string>? moduleVars = null;
        Dictionary<string, FieldBuilder>? moduleFields = null;

        // #1201 pre-pass: every name declared anywhere in this statement tree (params, locals,
        // loop vars, functions, classes, imports, …). Used to (a) revoke block-scoped lifts made
        // by an earlier same-key registration (script-merged scopes) that this tree re-declares,
        // and (b) enforce the declared-exactly-once safety rule for this tree's own candidates.
        var declaredNameCounts = CountDeclaredNames(statements);
        RevokeCollidingBlockScopedLifts(key, declaredNameCounts.Keys);

        foreach (var stmt in statements)
        {
            RegisterCapturedStmt(stmt, key, modulePath, ref moduleVars, ref moduleFields);
        }

        RegisterLiftableBlockScopedVars(statements, key, modulePath, declaredNameCounts, ref moduleVars, ref moduleFields);

        static TValue GetOrCreate<TValue>(Dictionary<string, TValue> dict, string k)
            where TValue : new()
        {
            if (!dict.TryGetValue(k, out var v))
            {
                v = new TValue();
                dict[k] = v;
            }
            return v;
        }

        // Local function, recurses into Stmt.Sequence so destructuring-produced var decls
        // (desugared into a sequence of Stmt.Vars) participate in capture registration.
        // Without the recursion, `const { x } = require(...)` at module scope would leave
        // `x` neither in TopLevelStaticVars nor in the captured-DC fields, and any class
        // method referencing `x` (common in semver, where re/t/etc. are destructured after
        // class declarations) would throw ReferenceError at runtime.
        void RegisterCapturedStmt(
            Stmt stmt,
            string captureKey,
            string? path,
            ref HashSet<string>? mv,
            ref Dictionary<string, FieldBuilder>? mf)
        {
            if (stmt is Stmt.Sequence seq)
            {
                foreach (var inner in seq.Statements)
                {
                    RegisterCapturedStmt(inner, captureKey, path, ref mv, ref mf);
                }
                return;
            }

            // Single-file (script) mode: `export const/let/var x = …` still declares
            // the top-level binding `x`, so a closure capturing it needs a field on
            // the entry-point display class exactly as a bare declaration would.
            // Real modules (non-null path) bind exported vars through export fields,
            // so leave those untouched.
            if (path == null && stmt is Stmt.Export { Declaration: { } exportedDecl })
            {
                RegisterCapturedStmt(exportedDecl, captureKey, path, ref mv, ref mf);
                return;
            }

            string? varName = stmt switch
            {
                Stmt.Var v => v.Name.Lexeme,
                Stmt.Const c => c.Name.Lexeme,
                _ => null
            };
            if (varName == null || !_closures.Analyzer.IsVariableCaptured(varName)) return;

            var displayClass = EnsureEntryPointDisplayClass();

            mv ??= GetOrCreate(_closures.ModuleCapturedTopLevelVars, captureKey);
            mf ??= GetOrCreate(_closures.ModuleEntryPointDisplayClassFields, captureKey);

            if (mv.Add(varName) && !mf.ContainsKey(varName))
            {
                // A revoked block-scope lift (#1201) may have left this name's field behind;
                // reuse it rather than defining a same-named duplicate on the display class.
                string fieldName = path == null
                    ? varName
                    : $"{SanitizeModuleForField(path)}__{varName}";
                var field = displayClass.DefineField(fieldName, _types.Object, FieldAttributes.Public);
                mf[varName] = field;
            }

            _closures.CapturedTopLevelVars.Add(varName);
        }
    }

    /// <summary>
    /// #1201: gives captured top-level BLOCK-scoped <c>let</c>/<c>const</c> bindings the same
    /// entry-point-display-class home that direct top-level declarations get. Without one, a
    /// closure capturing such a binding snapshots the entry method's local by value — and a
    /// closure created inside ANOTHER closure's body (whose creating context can't see that
    /// local at all) captures <c>null</c> via the populate fallback, so mutations vanish and
    /// object references are lost. Mirrors what function display classes already do for
    /// block-scoped locals inside functions.
    ///
    /// <para><b>Safety rule:</b> a binding is lifted only when its name is declared exactly once
    /// in the whole module tree (and never by any other registration sharing this key — the
    /// script-merged global scope). Names failing the rule keep today's snapshot path, so any
    /// shadowing/aliasing pattern that works now is untouched. Declarations inside LOOP bodies
    /// are never lifted: those bindings are per-iteration, and a single shared field would fuse
    /// iterations that the current snapshot semantics keeps distinct.</para>
    /// </summary>
    private void RegisterLiftableBlockScopedVars(
        List<Stmt> statements,
        string key,
        string? modulePath,
        Dictionary<string, int> declaredNameCounts,
        ref HashSet<string>? mv,
        ref Dictionary<string, FieldBuilder>? mf)
    {
        var candidates = new List<Stmt>();
        foreach (var stmt in statements)
            CollectLiftableBlockScopedDecls(stmt, nested: false, candidates);

        if (!_closures.ModuleRegisteredDeclaredNames.TryGetValue(key, out var namesFromEarlierCalls))
            _closures.ModuleRegisteredDeclaredNames[key] = namesFromEarlierCalls = [];

        foreach (var decl in candidates)
        {
            string name = decl switch
            {
                Stmt.Var v => v.Name.Lexeme,
                Stmt.Const c => c.Name.Lexeme,
                _ => throw new InvalidOperationException("unreachable: candidates are Var/Const only")
            };
            if (!_closures.Analyzer.IsVariableCaptured(name)) continue;
            if (declaredNameCounts[name] != 1) continue;          // declared-exactly-once rule
            if (namesFromEarlierCalls.Contains(name)) continue;    // collides across script-merged scopes

            var displayClass = EnsureEntryPointDisplayClass();
            mv ??= GetOrCreateEntry(_closures.ModuleCapturedTopLevelVars, key);
            mf ??= GetOrCreateEntry(_closures.ModuleEntryPointDisplayClassFields, key);

            if (!mv.Add(name)) continue; // already stored on the DC by an earlier same-key registration

            if (!mf.ContainsKey(name))
            {
                string fieldName = modulePath == null
                    ? name
                    : $"{SanitizeModuleForField(modulePath)}__{name}";
                mf[name] = displayClass.DefineField(fieldName, _types.Object, FieldAttributes.Public);
            }

            if (!_closures.ModuleLiftedBlockScopedVars.TryGetValue(key, out var lifted))
                _closures.ModuleLiftedBlockScopedVars[key] = lifted = [];
            lifted.Add(name);
            _closures.CapturedTopLevelVars.Add(name);
        }

        // Record this tree's declared names so later registrations sharing the key (scripts merged
        // into the single-file scope) can detect collisions with the lifts made here.
        namesFromEarlierCalls.UnionWith(declaredNameCounts.Keys);
    }

    /// <summary>
    /// Revokes block-scope lifts (#1201) made by earlier registrations under the same key when a
    /// later script re-declares the name: the lifted binding reverts to the plain-local snapshot
    /// path, restoring pre-lift behavior for both. The orphaned display-class field is harmless
    /// (direct registration reuses it if the colliding declaration is a captured top-level var).
    /// </summary>
    private void RevokeCollidingBlockScopedLifts(string key, IEnumerable<string> declaredNames)
    {
        if (!_closures.ModuleLiftedBlockScopedVars.TryGetValue(key, out var lifted) || lifted.Count == 0)
            return;
        _closures.ModuleCapturedTopLevelVars.TryGetValue(key, out var mv);

        foreach (var name in declaredNames)
        {
            if (!lifted.Remove(name)) continue;
            mv?.Remove(name);
            // Drop from the flat union only if no module still stores this name on the DC.
            if (!_closures.ModuleCapturedTopLevelVars.Values.Any(set => set.Contains(name)))
                _closures.CapturedTopLevelVars.Remove(name);
        }
    }

    private static TValue GetOrCreateEntry<TValue>(Dictionary<string, TValue> dict, string k)
        where TValue : new()
    {
        if (!dict.TryGetValue(k, out var v))
        {
            v = new TValue();
            dict[k] = v;
        }
        return v;
    }

    /// <summary>
    /// Collects captured-liftable block-scoped declarations (#1201): <c>let</c>/<c>const</c>
    /// statements nested in non-loop statement containers at module top level. Loops are a hard
    /// stop (per-iteration bindings); function/class/namespace bodies are separate scopes with
    /// their own display-class machinery. <c>var</c> declarations are excluded — they are
    /// function-scoped (hoisted), a different storage story.
    /// </summary>
    private static void CollectLiftableBlockScopedDecls(Stmt stmt, bool nested, List<Stmt> result)
    {
        switch (stmt)
        {
            case Stmt.Var v when nested && !v.IsVar:
                result.Add(v);
                break;
            case Stmt.Const when nested:
                result.Add(stmt);
                break;
            case Stmt.Sequence seq: // no scope of its own — keep the current nesting level
                foreach (var s in seq.Statements) CollectLiftableBlockScopedDecls(s, nested, result);
                break;
            case Stmt.Block block:
                foreach (var s in block.Statements) CollectLiftableBlockScopedDecls(s, nested: true, result);
                break;
            case Stmt.If ifStmt:
                CollectLiftableBlockScopedDecls(ifStmt.ThenBranch, nested: true, result);
                if (ifStmt.ElseBranch != null) CollectLiftableBlockScopedDecls(ifStmt.ElseBranch, nested: true, result);
                break;
            case Stmt.TryCatch tc:
                foreach (var s in tc.TryBlock) CollectLiftableBlockScopedDecls(s, nested: true, result);
                if (tc.CatchBlock != null)
                    foreach (var s in tc.CatchBlock) CollectLiftableBlockScopedDecls(s, nested: true, result);
                if (tc.FinallyBlock != null)
                    foreach (var s in tc.FinallyBlock) CollectLiftableBlockScopedDecls(s, nested: true, result);
                break;
            case Stmt.Switch sw:
                foreach (var c in sw.Cases)
                    foreach (var s in c.Body) CollectLiftableBlockScopedDecls(s, nested: true, result);
                if (sw.DefaultBody != null)
                    foreach (var s in sw.DefaultBody) CollectLiftableBlockScopedDecls(s, nested: true, result);
                break;
            case Stmt.LabeledStatement labeled:
                CollectLiftableBlockScopedDecls(labeled.Statement, nested, result);
                break;
        }
    }

    /// <summary>
    /// Counts every declared identifier in the statement tree, descending into nested closures,
    /// class bodies, and loop bodies. Deliberately over-broad (method names, params, catch params,
    /// enum/namespace/import names all count): the #1201 lift only fires for names this reports as
    /// declared exactly once, so any potential shadow — however exotic — blocks the lift.
    /// </summary>
    private static Dictionary<string, int> CountDeclaredNames(List<Stmt> statements)
    {
        var counter = new DeclaredNameCounter();
        foreach (var stmt in statements)
            counter.Visit(stmt);
        return counter.Counts;
    }

    private sealed class DeclaredNameCounter : Parsing.Visitors.AstVisitorBase
    {
        public Dictionary<string, int> Counts { get; } = [];

        private void Add(string name) =>
            Counts[name] = Counts.TryGetValue(name, out var c) ? c + 1 : 1;

        private void AddParams(List<Stmt.Parameter> parameters)
        {
            foreach (var p in parameters) Add(p.Name.Lexeme);
        }

        protected override void VisitVar(Stmt.Var stmt) { Add(stmt.Name.Lexeme); base.VisitVar(stmt); }
        protected override void VisitConst(Stmt.Const stmt) { Add(stmt.Name.Lexeme); base.VisitConst(stmt); }
        protected override void VisitFunction(Stmt.Function stmt)
        {
            Add(stmt.Name.Lexeme);
            AddParams(stmt.Parameters);
            base.VisitFunction(stmt);
        }
        protected override void VisitArrowFunction(Expr.ArrowFunction expr)
        {
            if (expr.Name != null) Add(expr.Name.Lexeme);
            AddParams(expr.Parameters);
            base.VisitArrowFunction(expr);
        }
        protected override void VisitClass(Stmt.Class stmt) { Add(stmt.Name.Lexeme); base.VisitClass(stmt); }
        protected override void VisitEnum(Stmt.Enum stmt) { Add(stmt.Name.Lexeme); base.VisitEnum(stmt); }
        protected override void VisitNamespace(Stmt.Namespace stmt) { Add(stmt.Name.Lexeme); base.VisitNamespace(stmt); }
        protected override void VisitForOf(Stmt.ForOf stmt) { Add(stmt.Variable.Lexeme); base.VisitForOf(stmt); }
        protected override void VisitForIn(Stmt.ForIn stmt) { Add(stmt.Variable.Lexeme); base.VisitForIn(stmt); }
        protected override void VisitTryCatch(Stmt.TryCatch stmt)
        {
            if (stmt.CatchParam != null) Add(stmt.CatchParam.Lexeme);
            base.VisitTryCatch(stmt);
        }
        protected override void VisitImport(Stmt.Import stmt)
        {
            if (stmt.DefaultImport != null) Add(stmt.DefaultImport.Lexeme);
            if (stmt.NamespaceImport != null) Add(stmt.NamespaceImport.Lexeme);
            if (stmt.NamedImports != null)
                foreach (var spec in stmt.NamedImports)
                    Add((spec.LocalName ?? spec.Imported).Lexeme);
            base.VisitImport(stmt);
        }
        protected override void VisitImportAlias(Stmt.ImportAlias stmt) { Add(stmt.AliasName.Lexeme); base.VisitImportAlias(stmt); }
        protected override void VisitImportRequire(Stmt.ImportRequire stmt) { Add(stmt.AliasName.Lexeme); base.VisitImportRequire(stmt); }
    }

    /// <summary>
    /// Lazily defines the shared <c>&lt;&gt;c__EntryPointDisplayClass</c> type, its default
    /// constructor, and the static <c>$entryPointDC</c> field that holds the instance.
    /// Returns the display class builder so callers can add fields to it.
    /// </summary>
    private TypeBuilder EnsureEntryPointDisplayClass()
    {
        if (_closures.EntryPointDisplayClass != null)
        {
            return _closures.EntryPointDisplayClass;
        }

        var displayClass = _moduleBuilder.DefineType(
            "<>c__EntryPointDisplayClass",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);

        var ctor = displayClass.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIl.Emit(OpCodes.Ret);

        _closures.EntryPointDisplayClass = displayClass;
        _closures.EntryPointDisplayClassCtor = ctor;
        _closures.EntryPointDisplayClassStaticField = _programType.DefineField(
            "$entryPointDC",
            displayClass,
            FieldAttributes.Public | FieldAttributes.Static);

        return displayClass;
    }

    /// <summary>
    /// Per-module pass: defines static fields for non-captured top-level
    /// var/const declarations and records them in
    /// <see cref="_moduleTopLevelStaticVars"/>. Captured vars are handled by
    /// <see cref="AnalyzeCapturedTopLevelVarsAcrossModules"/> and live on
    /// per-module fields of the shared entry-point display class.
    /// </summary>
    /// <param name="modulePath">The module this statement set belongs to, or
    /// <c>null</c> for single-file compilation (which shares a global scope).</param>
    private void DefineModuleScopedTopLevelStaticFields(List<Stmt> statements, string? modulePath)
    {
        Dictionary<string, FieldBuilder>? moduleDict = null;
        if (modulePath != null)
        {
            if (!_moduleTopLevelStaticVars.TryGetValue(modulePath, out moduleDict))
            {
                moduleDict = new Dictionary<string, FieldBuilder>();
                _moduleTopLevelStaticVars[modulePath] = moduleDict;
            }
        }

        foreach (var stmt in statements)
        {
            DefineModuleScopedTopLevelStaticField(stmt, modulePath, moduleDict);
        }
    }

    /// <summary>
    /// Handles a single top-level statement, recursing into <see cref="Stmt.Sequence"/>
    /// so destructuring declarations (which the parser desugars into a sequence of
    /// <c>Stmt.Var</c>s) register their named bindings as top-level statics rather than
    /// falling back to per-init-method locals.
    /// </summary>
    /// <remarks>
    /// Without this, <c>const { x, y } = require('...')</c> at module scope would register
    /// neither <c>x</c> nor <c>y</c> as static fields, so class methods in the same module
    /// couldn't access them (the reference would throw "Undefined variable 'x'" at runtime).
    /// Semver's classes/comparator.js destructures re and t this way and fails without it.
    /// </remarks>
    private void DefineModuleScopedTopLevelStaticField(Stmt stmt, string? modulePath, Dictionary<string, FieldBuilder>? moduleDict)
    {
        if (stmt is Stmt.Sequence seq)
        {
            foreach (var inner in seq.Statements)
            {
                DefineModuleScopedTopLevelStaticField(inner, modulePath, moduleDict);
            }
            return;
        }

        // Single-file (script) mode: `export const/let/var x = …` still declares the
        // top-level binding `x`. Unwrap to the inner declaration so it gets a static
        // field exactly like a bare declaration would; without this its initializer
        // would have nowhere to store and references to `x` would throw at runtime.
        // Real modules (non-null path) bind exported vars through export fields +
        // per-init-method locals, so leave those untouched.
        if (modulePath == null && stmt is Stmt.Export { Declaration: { } exportedDecl })
        {
            DefineModuleScopedTopLevelStaticField(exportedDecl, modulePath, moduleDict);
            return;
        }

        string? varName = stmt switch
        {
            Stmt.Var v => v.Name.Lexeme,
            Stmt.Const c => c.Name.Lexeme,
            _ => null
        };
        if (varName == null || _closures.CapturedTopLevelVars.Contains(varName)) return;

        // Each module gets its own field, so two modules that both declare
        // `const foo` have independent storage. Field names are qualified
        // by module when a path is present; script mode keeps unqualified
        // names since scripts intentionally share global scope.
        string fieldName = modulePath == null
            ? $"$topLevel_{varName}"
            : $"$topLevel_{SanitizeModuleForField(modulePath)}_{varName}";

        var field = _programType.DefineField(
            fieldName,
            _types.Object,
            FieldAttributes.Public | FieldAttributes.Static);

        // The global dict (_topLevelStaticVars) is last-write-wins by name. That's
        // only consulted for captured-var dispatch via name, which this path skips
        // (we `continue` above when the var is captured). For non-captured vars,
        // resolution goes through ctx.TopLevelStaticVars which is module-scoped.
        _topLevelStaticVars[varName] = field;
        if (moduleDict != null)
        {
            moduleDict[varName] = field;
        }
    }

    /// <summary>
    /// Produces a field-name-safe fragment from a module path. Used to
    /// disambiguate per-module static fields (e.g. two modules both declaring
    /// <c>const foo</c>).
    /// </summary>
    private static string SanitizeModuleForField(string modulePath)
    {
        var chars = new char[modulePath.Length];
        for (int i = 0; i < modulePath.Length; i++)
        {
            char c = modulePath[i];
            chars[i] = char.IsLetterOrDigit(c) ? c : '_';
        }
        return new string(chars);
    }

    /// <summary>
    /// Builds a fresh dict of top-level static vars visible to code emitted
    /// for <paramref name="modulePath"/>. Merges this module's var/const fields
    /// with its import fields. Returns <c>null</c> when there's nothing to expose.
    /// </summary>
    /// <remarks>
    /// Always return a new dict — never share a reference across emission
    /// contexts. Mutation by emitters (e.g. <see cref="EmitModuleInit"/> adding
    /// import fields after the fact) must not leak into other modules.
    /// </remarks>
    private Dictionary<string, FieldBuilder>? BuildTopLevelStaticVarsForModule(string? modulePath)
    {
        Dictionary<string, FieldBuilder>? result = null;

        if (modulePath != null)
        {
            if (_moduleTopLevelStaticVars.TryGetValue(modulePath, out var moduleVars))
            {
                foreach (var (name, field) in moduleVars)
                {
                    result ??= new Dictionary<string, FieldBuilder>();
                    result[name] = field;
                }
            }
            if (_modules.ImportFields.TryGetValue(modulePath, out var imports))
            {
                foreach (var (name, field) in imports)
                {
                    result ??= new Dictionary<string, FieldBuilder>();
                    result[name] = field;
                }
            }
        }
        else if (_topLevelStaticVars.Count > 0)
        {
            // Single-file / script-style: no per-module scoping — expose the global union.
            result = new Dictionary<string, FieldBuilder>(_topLevelStaticVars);
        }

        // A body emitted for a namespace member (function, generator, async, async-generator,
        // class method/accessor/static) must also resolve its enclosing namespace's
        // var/let/const members by bare name (#567). _currentNamespacePath is non-null only
        // while EmitNamespaceMemberBodies is emitting such a body, so this augments exactly
        // those emissions and nothing else. Centralizing here covers every emission site that
        // routes through this helper (state machines, class methods) rather than just the plain
        // function path.
        if (_currentNamespacePath != null)
            result = BuildNamespaceScopedStaticVars(result, _currentNamespacePath);

        return result;
    }

    /// <summary>
    /// Builds a class-method-scoped top-level static var map that augments the base
    /// <see cref="BuildTopLevelStaticVarsForModule"/> with this module's own ESM export
    /// fields. A method body like
    ///   <c>braceExpand() { return braceExpand(this.x, this.y); }</c>
    /// references the same-module export by bare identifier — that reference has to
    /// resolve to the export's static field or the runtime throws a <c>ReferenceError</c>.
    /// Deliberately scoped to class methods because other emission sites (module init,
    /// imported-function calls, __dirname resolution) already rely on their existing
    /// view and adding ESM exports there broke 280+ tests when tried.
    /// </summary>
    private Dictionary<string, FieldBuilder>? BuildClassMethodTopLevelStaticVarsForModule(string? modulePath)
    {
        var result = BuildTopLevelStaticVarsForModule(modulePath);
        if (modulePath == null || !_modules.ExportFields.TryGetValue(modulePath, out var exports))
            return result;

        foreach (var (name, field) in exports)
        {
            if (name == "$default" || name == "$exportAssignment") continue;
            result ??= new Dictionary<string, FieldBuilder>();
            // Don't overwrite a same-named module-scoped static or import — those
            // take precedence. ESM `export const X` without an accompanying
            // `_moduleTopLevelStaticVars` entry reaches this fallback and binds X
            // to the export field.
            if (!result.ContainsKey(name))
                result[name] = field;
        }

        return result;
    }

    /// <summary>
    /// Returns the captured-top-level-var names visible to code emitted for the
    /// given module, or <c>null</c> if none. Mirrors <see cref="BuildTopLevelStaticVarsForModule"/>.
    /// </summary>
    /// <remarks>
    /// Two modules that both declare <c>const x</c> (with at least one module's
    /// closures capturing <c>x</c>) see only their own <c>x</c> here — the set is
    /// filtered by module so each module's emissions resolve to that module's
    /// field on the shared display class, not the other module's.
    /// </remarks>
    private HashSet<string>? BuildCapturedTopLevelVarsForModule(string? modulePath)
    {
        string key = modulePath ?? ClosureCompilationState.SingleFileKey;
        if (_closures.ModuleCapturedTopLevelVars.TryGetValue(key, out var vars) && vars.Count > 0)
        {
            return vars;
        }
        return null;
    }

    /// <summary>
    /// Returns the lifted block-scoped top-level var names (#1201) visible to code emitted for
    /// the given module, or <c>null</c> if none. Companion of
    /// <see cref="BuildCapturedTopLevelVarsForModule"/>; consumed only by the module-top-level
    /// declaration emitter.
    /// </summary>
    private HashSet<string>? BuildLiftedBlockScopedTopLevelVarsForModule(string? modulePath)
    {
        string key = modulePath ?? ClosureCompilationState.SingleFileKey;
        if (_closures.ModuleLiftedBlockScopedVars.TryGetValue(key, out var vars) && vars.Count > 0)
        {
            return vars;
        }
        return null;
    }

    /// <summary>
    /// Returns the entry-point display class field map visible to code emitted
    /// for the given module — a fresh dict so downstream mutation can't leak into
    /// other modules' views. Keys are bare var names (e.g. <c>"x"</c>); values
    /// resolve to this module's qualified field (e.g. <c>a_ts__x</c>) on the
    /// shared display class.
    /// </summary>
    private Dictionary<string, FieldBuilder>? BuildEntryPointDisplayClassFieldsForModule(string? modulePath)
    {
        string key = modulePath ?? ClosureCompilationState.SingleFileKey;
        if (_closures.ModuleEntryPointDisplayClassFields.TryGetValue(key, out var fields) && fields.Count > 0)
        {
            return new Dictionary<string, FieldBuilder>(fields);
        }
        return null;
    }
}
