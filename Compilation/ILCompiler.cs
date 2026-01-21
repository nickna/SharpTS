using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation.Emitters;
using SharpTS.Compilation.Emitters.Modules;
using SharpTS.Compilation.Registries;
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
    private readonly string _assemblyName;
    private readonly PersistedAssemblyBuilder _assemblyBuilder;
    private readonly ModuleBuilder _moduleBuilder;
    private readonly TypeMapper _typeMapper;
    private readonly TypeEmitterRegistry _typeEmitterRegistry = new();  // Type-first method dispatch registry
    private readonly BuiltInModuleEmitterRegistry _builtInModuleEmitterRegistry = new();  // Built-in module emitters
    private readonly Dictionary<string, string> _builtInModuleNamespaces = [];  // Variable name -> module name for direct dispatch
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

    // Configuration options
    private readonly bool _preserveConstEnums;

    // Namespace support: namespace path -> static field
    private readonly Dictionary<string, FieldBuilder> _namespaceFields = [];

    // Emitted runtime (for standalone DLLs)
    private EmittedRuntime _runtime = null!;

    // Type information from static analysis
    private TypeMap _typeMap = null!;

    // Dead code analysis results
    private DeadCodeInfo? _deadCodeInfo;

    // Strict mode setting (from "use strict" directive)
    private bool _isStrictMode;

    private UnionTypeGenerator? _unionGenerator;

    // Shared context for definition phase (module name resolution)
    private CompilationContext? _definitionContext;

    // Top-level variables captured by async functions (need to be static fields)
    private readonly Dictionary<string, FieldBuilder> _topLevelStaticVars = [];

    // Entry point
    private MethodBuilder? _entryPoint;

    // Type provider for resolving .NET types (always runtime types for compilation)
    private readonly TypeProvider _types;

    // Whether to post-process the assembly for reference assembly compatibility
    private readonly bool _useReferenceAssemblies;
    private readonly string? _sdkPath;

    // Assembly metadata for version and attributes
    private readonly AssemblyMetadata? _metadata;

    // External assembly references for @DotNetType support
    private readonly IReadOnlyList<string>? _referenceAssemblies;
    private AssemblyReferenceLoader? _referenceLoader;

    // Output target type (DLL or EXE)
    private readonly OutputTarget _outputTarget;

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
    {
        _assemblyName = assemblyName;
        _preserveConstEnums = preserveConstEnums;
        _useReferenceAssemblies = useReferenceAssemblies;
        _sdkPath = sdkPath;
        _metadata = metadata;
        _referenceAssemblies = references;
        _outputTarget = target;

        // Initialize reference loader if external assemblies are provided
        if (references != null && references.Count > 0)
        {
            _referenceLoader = new AssemblyReferenceLoader(references, sdkPath);
        }

        // Always use runtime types for compilation.
        // When --ref-asm is enabled, the assembly will be post-processed in Save()
        // to rewrite System.Private.CoreLib references to SDK assemblies.
        // This is necessary because MetadataLoadContext types cannot be used with
        // TypeBuilder.DefineType() for interface implementation (async/generator types).
        _types = TypeProvider.Runtime;

        // Create AssemblyName with version if metadata is provided
        var asmName = new AssemblyName(assemblyName);
        if (metadata?.Version != null)
        {
            asmName.Version = metadata.Version;
        }

        _assemblyBuilder = new PersistedAssemblyBuilder(
            asmName,
            _types.CoreAssembly
        );

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
        _definitionContext ??= new CompilationContext(null!, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            IsStrictMode = _isStrictMode
            // Note: ClassRegistry intentionally not set here - definition context uses raw dictionaries
        };
        _definitionContext.CurrentModulePath = _modules.CurrentPath;
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
            externalTypes: _classes.ExternalTypes,
            superclass: _classes.Superclass,
            constructors: _classes.Constructors,
            constructorOverloads: _classes.ConstructorOverloads,
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
        _isStrictMode = CheckForUseStrict(statements);

        Phase0_ExtractNamespace(statements);
        Phase1_EmitRuntimeTypes();
        Phase2_AnalyzeClosures(statements);
        Phase3_CreateProgramType();
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
    /// Checks if the statements begin with a "use strict" directive.
    /// </summary>
    /// <param name="statements">The list of statements to check.</param>
    /// <returns>True if "use strict" directive is found at the beginning.</returns>
    private static bool CheckForUseStrict(List<Stmt>? statements)
    {
        if (statements == null) return false;
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Directive directive)
            {
                if (directive.Value == "use strict")
                {
                    return true;
                }
                // Continue checking other directives at the start
            }
            else
            {
                // Non-directive statement encountered, stop checking
                break;
            }
        }
        return false;
    }

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
        _runtime = new RuntimeEmitter(_types).EmitAll(_moduleBuilder);
    }

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
    /// </summary>
    private void PreScanBuiltInModuleImports(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Import import && !import.IsTypeOnly)
            {
                // Check if the import path is a built-in module
                string? builtInModuleName = Runtime.BuiltIns.Modules.BuiltInModuleRegistry.IsBuiltIn(import.ModulePath)
                    ? import.ModulePath  // Use the module path directly as the module name
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
                    if (import.NamedImports != null)
                    {
                        foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
                        {
                            string localName = spec.LocalName?.Lexeme ?? spec.Imported.Lexeme;
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
    /// Phase 4: Define all class, function, enum, and namespace declarations.
    /// Also initializes typed interop and type emitter registries.
    /// </summary>
    private void Phase4_DefineDeclarations(List<Stmt> statements)
    {
        // Define all declarations
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt)
            {
                DefineClass(classStmt);
            }
            else if (stmt is Stmt.Function funcStmt)
            {
                if (funcStmt.Body == null) continue; // Skip overload signatures
                DefineFunction(funcStmt);
            }
            else if (stmt is Stmt.Enum enumStmt)
            {
                DefineEnum(enumStmt);
            }
            else if (stmt is Stmt.Namespace nsStmt)
            {
                DefineNamespaceFields(nsStmt);
            }
        }

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
        DefineClassExpressionTypes();
        DefineClassExpressionMethods();
    }

    /// <summary>
    /// Phase 6: Emit arrow function bodies and async/generator state machine bodies.
    /// </summary>
    private void Phase6_EmitArrowAndStateMachineBodies(List<Stmt> statements)
    {
        EmitArrowFunctionBodies();
        DefineAllClassMethods(statements);
        EmitAsyncStateMachineBodies();
        EmitGeneratorStateMachineBodies();
        EmitAsyncGeneratorStateMachineBodies();
    }

    /// <summary>
    /// Phase 7: Emit method bodies for all classes, functions, and namespaces.
    /// </summary>
    private void Phase7_EmitMethodBodies(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt)
            {
                EmitClassMethods(classStmt);
            }
            else if (stmt is Stmt.Function funcStmt)
            {
                if (funcStmt.Body == null) continue; // Skip overload signatures
                EmitFunctionBody(funcStmt);
                EmitFunctionOverloads(funcStmt);
            }
            else if (stmt is Stmt.Namespace nsStmt)
            {
                EmitNamespaceMemberBodies(nsStmt);
            }
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
    /// Phase 9: Finalize all types by calling CreateType().
    /// </summary>
    private void Phase9_FinalizeTypes()
    {
        _unionGenerator?.FinalizeAllUnionTypes();

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
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Date>(new DateEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Map>(new MapEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Set>(new SetEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.WeakMap>(new WeakMapEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.WeakSet>(new WeakSetEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.RegExp>(new RegExpEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.AsyncGenerator>(new AsyncGeneratorEmitter());
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Error>(new ErrorEmitter());

        // Static type emitters
        _typeEmitterRegistry.RegisterStatic("Math", new MathStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("JSON", new JSONStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Object", new ObjectStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Array", new ArrayStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Number", new NumberStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Promise", new PromiseStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Symbol", new SymbolStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("process", new ProcessStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("globalThis", new GlobalThisStaticEmitter(_typeEmitterRegistry));

        // Built-in module emitters
        _builtInModuleEmitterRegistry.Register(new PathModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new OsModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new FsModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new QuerystringModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new AssertModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new UrlModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new ProcessModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new CryptoModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new UtilModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new ReadlineModuleEmitter());
        _builtInModuleEmitterRegistry.Register(new ChildProcessModuleEmitter());
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

        var allStatements = modules.SelectMany(m => m.Statements).ToList();

        ModulePhase0_ExtractNamespaces(modules);
        Phase1_EmitRuntimeTypes();
        Phase2_AnalyzeClosures(allStatements);
        Phase3_CreateProgramType();
        PreScanBuiltInModuleImports(allStatements);
        ModulePhase4_DefineModuleTypes(modules);
        DefineTopLevelCapturedVariables(allStatements);
        ModulePhase5_DefineDeclarations(modules);
        InitializeTypedInterop();
        InitializeTypeEmitterRegistries();
        ModulePhase6_CollectArrowFunctions(allStatements);
        ModulePhase7_EmitArrowBodies();
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
            DefineModuleType(module);
        }
    }

    /// <summary>
    /// Module Phase 5: Define all class, function, enum, and namespace declarations across modules.
    /// </summary>
    private void ModulePhase5_DefineDeclarations(List<ParsedModule> modules)
    {
        foreach (var module in modules)
        {
            _modules.CurrentPath = module.Path;
            _modules.CurrentDotNetNamespace = _modules.Namespaces.GetValueOrDefault(module.Path);

            foreach (var stmt in module.Statements)
            {
                DefineDeclarationFromStatement(stmt);
            }
        }
        _modules.CurrentPath = null;
        _modules.CurrentDotNetNamespace = null;
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
    private void ModulePhase6_CollectArrowFunctions(List<Stmt> allStatements)
    {
        CollectAndDefineArrowFunctions(allStatements);
        DefineClassExpressionTypes();
        DefineClassExpressionMethods();
    }

    /// <summary>
    /// Module Phase 7: Emit arrow function bodies.
    /// </summary>
    private void ModulePhase7_EmitArrowBodies()
    {
        EmitArrowFunctionBodies();
    }

    /// <summary>
    /// Module Phase 8: Emit method bodies for all modules.
    /// </summary>
    private void ModulePhase8_EmitMethodBodies(List<ParsedModule> modules)
    {
        foreach (var module in modules)
        {
            _modules.CurrentPath = module.Path;
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
            EmitModuleInit(module);
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
        _unionGenerator?.FinalizeAllUnionTypes();

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

    public void Save(string outputPath)
    {
        // Generate metadata for the assembly
        MetadataBuilder metadataBuilder = _assemblyBuilder.GenerateMetadata(
            out BlobBuilder ilStream,
            out BlobBuilder fieldData);

        // Choose PE header based on output target
        // DLLs can still have entry points (runnable with `dotnet <dll>`)
        PEHeaderBuilder peHeader = _outputTarget == OutputTarget.Exe
            ? PEHeaderBuilder.CreateExecutableHeader()
            : PEHeaderBuilder.CreateLibraryHeader();

        // Set entry point if available (both DLL and EXE can have entry points)
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

        // If using reference assemblies, post-process to rewrite System.Private.CoreLib
        // references to SDK reference assemblies (System.Runtime, etc.)
        if (_useReferenceAssemblies)
        {
            // Write to a temp memory stream first
            using var tempStream = new MemoryStream();
            peBlob.WriteContentTo(tempStream);
            tempStream.Position = 0;

            // Get the SDK reference assembly path (use explicit path if provided)
            var refAssemblyPath = _sdkPath ?? SdkResolver.FindReferenceAssembliesPath()
                ?? throw new InvalidOperationException(
                    "Could not find SDK reference assemblies for post-processing. " +
                    "Ensure the .NET SDK is installed.");

            // Rewrite assembly references
            using var rewriter = new AssemblyReferenceRewriter(tempStream, refAssemblyPath);
            rewriter.Rewrite();

            // Write the rewritten assembly to the output file
            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            rewriter.Save(outputStream);
        }
        else
        {
            // Write directly to file
            using FileStream fileStream = new(outputPath, FileMode.Create, FileAccess.Write);
            peBlob.WriteContentTo(fileStream);
        }
    }

    /// <summary>
    /// Defines static fields for all top-level (module-level) variables.
    /// These use static fields so all functions can access them regardless of
    /// execution context (closures, async, nested calls, arrow functions, etc).
    /// </summary>
    /// <remarks>
    /// In JavaScript/TypeScript, module-level variables have module scope - they persist
    /// for the application lifetime and are accessible from any function. This maps
    /// directly to static fields in .NET, which is the correct semantic representation.
    /// </remarks>
    private void DefineTopLevelCapturedVariables(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            string? varName = stmt switch
            {
                Stmt.Var v => v.Name.Lexeme,
                Stmt.Const c => c.Name.Lexeme,
                _ => null
            };

            if (varName != null)
            {
                // Skip if already defined (e.g., from built-in module imports)
                if (_topLevelStaticVars.ContainsKey(varName))
                    continue;

                var field = _programType.DefineField(
                    $"$topLevel_{varName}",
                    _types.Object,
                    FieldAttributes.Public | FieldAttributes.Static);
                _topLevelStaticVars[varName] = field;
            }
        }
    }
}
