using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation.Emitters;
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
    private readonly Dictionary<string, TypeBuilder> _classBuilders = [];
    private readonly Dictionary<string, Type> _externalTypes = [];  // @DotNetType classes mapped to .NET types
    private readonly TypeEmitterRegistry _typeEmitterRegistry = new();  // Type-first method dispatch registry
    private readonly Dictionary<string, MethodBuilder> _functionBuilders = [];
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _staticFields = [];
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _staticMethods = [];
    private readonly Dictionary<string, FieldBuilder> _instanceFieldsField = []; // _fields dict field per class
    private readonly Dictionary<string, ConstructorBuilder> _classConstructors = [];
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _instanceMethods = [];
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _instanceGetters = [];
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _instanceSetters = [];
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _preDefinedMethods = []; // Methods pre-defined before body emission
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _preDefinedAccessors = []; // Accessors pre-defined before body emission
    private readonly Dictionary<string, string?> _classSuperclass = [];
    private readonly Dictionary<string, (int RestParamIndex, int RegularParamCount)> _functionRestParams = [];
    private TypeBuilder _programType = null!;

    // Closure support
    private ClosureAnalyzer _closureAnalyzer = null!;
    private readonly Dictionary<Expr.ArrowFunction, MethodBuilder> _arrowMethods = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, TypeBuilder> _displayClasses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, Dictionary<string, FieldBuilder>> _displayClassFields = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, ConstructorBuilder> _displayClassConstructors = new(ReferenceEqualityComparer.Instance);
    private int _arrowMethodCounter = 0;
    private int _displayClassCounter = 0;

    // Class expression support
    private readonly Dictionary<Expr.ClassExpr, TypeBuilder> _classExprBuilders = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ClassExpr, string> _classExprNames = new(ReferenceEqualityComparer.Instance);
    private readonly List<Expr.ClassExpr> _classExprsToDefine = [];
    private int _classExprCounter = 0;

    // Enum support
    private readonly Dictionary<string, Dictionary<string, object>> _enumMembers = [];
    private readonly Dictionary<string, Dictionary<double, string>> _enumReverse = [];
    private readonly Dictionary<string, EnumKind> _enumKinds = [];
    private readonly HashSet<string> _constEnums = [];
    private readonly bool _preserveConstEnums;

    // Namespace support: namespace path -> static field
    private readonly Dictionary<string, FieldBuilder> _namespaceFields = [];

    // Generic type parameter support
    private readonly Dictionary<string, GenericTypeParameterBuilder[]> _classGenericParams = [];
    private readonly Dictionary<string, GenericTypeParameterBuilder[]> _functionGenericParams = [];
    private readonly Dictionary<string, bool> _isGenericFunction = [];

    // Emitted runtime (for standalone DLLs)
    private EmittedRuntime _runtime = null!;

    // Type information from static analysis
    private TypeMap _typeMap = null!;

    // Dead code analysis results
    private DeadCodeInfo? _deadCodeInfo;

    // Async method compilation (native IL state machine generation)
    private readonly AsyncStateAnalyzer _asyncAnalyzer = new();
    private readonly Dictionary<string, AsyncStateMachineBuilder> _asyncStateMachines = [];
    private readonly Dictionary<string, Stmt.Function> _asyncFunctions = [];
    private int _asyncStateMachineCounter = 0;
    private int _asyncArrowCounter = 0;

    // Generator method compilation (generator state machine generation)
    private readonly GeneratorStateAnalyzer _generatorAnalyzer = new();
    private readonly Dictionary<string, GeneratorStateMachineBuilder> _generatorStateMachines = [];
    private readonly Dictionary<string, Stmt.Function> _generatorFunctions = [];
    private int _generatorStateMachineCounter = 0;

    // Async generator method compilation (combined async + generator state machine)
    private readonly AsyncGeneratorStateAnalyzer _asyncGeneratorAnalyzer = new();
    private readonly Dictionary<string, AsyncGeneratorStateMachineBuilder> _asyncGeneratorStateMachines = [];
    private readonly Dictionary<string, Stmt.Function> _asyncGeneratorFunctions = [];
    private int _asyncGeneratorStateMachineCounter = 0;

    // Async arrow function state machines (one per async arrow in async methods)
    private readonly Dictionary<Expr.ArrowFunction, AsyncArrowStateMachineBuilder> _asyncArrowBuilders = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, AsyncStateMachineBuilder> _asyncArrowOuterBuilders = new(ReferenceEqualityComparer.Instance);
    // For nested async arrows, maps to the parent arrow's builder (not the function's builder)
    private readonly Dictionary<Expr.ArrowFunction, AsyncArrowStateMachineBuilder> _asyncArrowParentBuilders = new(ReferenceEqualityComparer.Instance);

    // Pooled HashSets for async arrow analysis (reduces allocation churn)
    private readonly HashSet<string> _asyncArrowDeclaredVars = [];
    private readonly HashSet<string> _asyncArrowUsedAfterAwait = [];
    private readonly HashSet<string> _asyncArrowDeclaredBeforeAwait = [];

    // Module support
    private readonly Dictionary<string, TypeBuilder> _moduleTypes = [];
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _moduleExportFields = [];
    private readonly Dictionary<string, MethodBuilder> _moduleInitMethods = [];
    private ModuleResolver? _moduleResolver;
    private string? _currentModulePath; // Current module being compiled (for qualified type names)
    private readonly Dictionary<string, string> _classToModule = []; // Maps class name to module path for lookups
    private readonly Dictionary<string, string> _functionToModule = []; // Maps function name to module path for lookups
    private readonly Dictionary<string, string> _enumToModule = []; // Maps enum name to module path for lookups

    // .NET namespace support via @Namespace decorator
    private string? _currentDotNetNamespace; // Current .NET namespace for compiled types
    private readonly Dictionary<string, string?> _moduleNamespaces = []; // Per-module .NET namespaces

    // Typed Interop: Real .NET property support
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _propertyBackingFields = [];
    private readonly Dictionary<string, Dictionary<string, PropertyBuilder>> _classProperties = [];
    private readonly Dictionary<string, HashSet<string>> _declaredPropertyNames = [];
    private readonly Dictionary<string, HashSet<string>> _readonlyPropertyNames = [];
    private readonly Dictionary<string, Dictionary<string, Type>> _propertyTypes = [];
    private readonly Dictionary<string, FieldBuilder> _extrasFields = []; // _extras dict field for dynamic properties

    // @lock decorator support: Thread-safe method execution
    private readonly Dictionary<string, FieldBuilder> _syncLockFields = [];           // class -> _syncLock field (object for Monitor)
    private readonly Dictionary<string, FieldBuilder> _asyncLockFields = [];          // class -> _asyncLock field (SemaphoreSlim)
    private readonly Dictionary<string, FieldBuilder> _lockReentrancyFields = [];     // class -> _lockReentrancy field (AsyncLocal<int>)
    private readonly Dictionary<string, FieldBuilder> _staticSyncLockFields = [];     // class -> static _syncLock field
    private readonly Dictionary<string, FieldBuilder> _staticAsyncLockFields = [];    // class -> static _asyncLock field
    private readonly Dictionary<string, FieldBuilder> _staticLockReentrancyFields = []; // class -> static _lockReentrancy field
    private UnionTypeGenerator? _unionGenerator;
    // Explicit accessor tracking for PropertyBuilder creation (TypeScript get/set syntax)
    private readonly Dictionary<string, Dictionary<string, (MethodBuilder? Getter, MethodBuilder? Setter, Type PropertyType)>> _explicitAccessors = [];

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
    {
        _assemblyName = assemblyName;
        _preserveConstEnums = preserveConstEnums;
        _useReferenceAssemblies = useReferenceAssemblies;
        _sdkPath = sdkPath;
        _metadata = metadata;
        _referenceAssemblies = references;

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
        _definitionContext ??= new CompilationContext(null!, _typeMapper, _functionBuilders, _classBuilders, _types)
        {
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule
        };
        _definitionContext.CurrentModulePath = _currentModulePath;
        _definitionContext.DotNetNamespace = _currentDotNetNamespace;
        return _definitionContext;
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
        // Store the type map and dead code info for use by ILEmitter
        _typeMap = typeMap;
        _deadCodeInfo = deadCodeInfo;

        // Phase 0: Extract .NET namespace from @Namespace file directive
        _currentDotNetNamespace = ExtractNamespaceFromStatements(statements);

        // Phase 1: Emit runtime support types into the generated assembly
        // This makes compiled DLLs standalone without requiring SharpTS.dll
        // Phase 1: Emit runtime support types into the generated assembly
        // This makes compiled DLLs standalone without requiring SharpTS.dll
        _runtime = new RuntimeEmitter(_types).EmitAll(_moduleBuilder);

        // Phase 2: Analyze closures
        _closureAnalyzer = new ClosureAnalyzer();
        _closureAnalyzer.Analyze(statements);

        // Phase 3: Create the main program type for top-level code
        _programType = _moduleBuilder.DefineType(
            "$Program",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed
        );

        // Phase 4: Collect and define all class and function declarations
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt)
            {
                DefineClass(classStmt);
            }
            else if (stmt is Stmt.Function funcStmt)
            {
                // Skip overload signatures (no body)
                if (funcStmt.Body == null) continue;
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

        // Phase 4.4: Define static fields for top-level variables captured by async functions
        DefineTopLevelCapturedVariables(statements);

        // Phase 4.5: Initialize typed interop support now that all classes are defined
        _unionGenerator = new UnionTypeGenerator(_typeMapper);
        _typeMapper.SetClassBuilders(_classBuilders);
        _typeMapper.SetUnionGenerator(_unionGenerator);

        // Phase 4.6: Initialize type emitter registry with external types for type-first dispatch
        _typeEmitterRegistry.SetExternalTypes(_externalTypes);

        // Register type-specific emitters
        var stringEmitter = new StringEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.String>(stringEmitter);
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.StringLiteral>(stringEmitter);

        var arrayEmitter = new ArrayEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Array>(arrayEmitter);

        var dateEmitter = new DateEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Date>(dateEmitter);

        var mapEmitter = new MapEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Map>(mapEmitter);

        var setEmitter = new SetEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Set>(setEmitter);

        var weakMapEmitter = new WeakMapEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.WeakMap>(weakMapEmitter);

        var weakSetEmitter = new WeakSetEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.WeakSet>(weakSetEmitter);

        var regExpEmitter = new RegExpEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.RegExp>(regExpEmitter);

        var asyncGeneratorEmitter = new AsyncGeneratorEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.AsyncGenerator>(asyncGeneratorEmitter);

        // Register static type emitters
        _typeEmitterRegistry.RegisterStatic("Math", new MathStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("JSON", new JSONStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Object", new ObjectStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Array", new ArrayStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Number", new NumberStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Promise", new PromiseStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Symbol", new SymbolStaticEmitter());

        // Phase 5: Collect all arrow functions and generate methods/display classes
        CollectAndDefineArrowFunctions(statements);

        // Phase 5.5: Define class expression types (collected during arrow collection)
        DefineClassExpressionTypes();

        // Phase 6: Emit arrow function method bodies
        EmitArrowFunctionBodies();

        // Phase 6.3: Define all class methods (without bodies) so they're available for async
        // This populates _instanceMethods, _instanceGetters, _instanceSetters for direct dispatch
        DefineAllClassMethods(statements);

        // Phase 6.5: Emit async state machine bodies
        EmitAsyncStateMachineBodies();

        // Phase 6.6: Emit generator state machine bodies
        EmitGeneratorStateMachineBodies();

        // Phase 6.7: Emit async generator state machine bodies
        EmitAsyncGeneratorStateMachineBodies();

        // Phase 7: Emit method bodies for all classes and functions
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt)
            {
                EmitClassMethods(classStmt);
            }
            else if (stmt is Stmt.Function funcStmt)
            {
                // Skip overload signatures (no body)
                if (funcStmt.Body == null) continue;
                EmitFunctionBody(funcStmt);
            }
            else if (stmt is Stmt.Namespace nsStmt)
            {
                EmitNamespaceMemberBodies(nsStmt);
            }
        }

        // Phase 8: Emit entry point (top-level statements)
        EmitEntryPoint(statements);

        // Phase 9: Finalize all types
        foreach (var tb in _displayClasses.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _classBuilders.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _classExprBuilders.Values)
        {
            tb.CreateType();
        }
        _programType.CreateType();
    }

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
        _moduleResolver = resolver;

        // Phase 0: Extract .NET namespaces from @Namespace directives in each module
        foreach (var module in modules)
        {
            _moduleNamespaces[module.Path] = ExtractNamespaceFromStatements(module.Statements);
        }

        // Phase 1: Emit runtime support types
        // Phase 1: Emit runtime support types into the generated assembly
        // This makes compiled DLLs standalone without requiring SharpTS.dll
        _runtime = new RuntimeEmitter(_types).EmitAll(_moduleBuilder);

        // Phase 2: Collect all statements for closure analysis
        var allStatements = modules.SelectMany(m => m.Statements).ToList();
        _closureAnalyzer = new ClosureAnalyzer();
        _closureAnalyzer.Analyze(allStatements);

        // Phase 3: Create the main program type
        _programType = _moduleBuilder.DefineType(
            "$Program",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed
        );

        // Phase 4: Define module types with export fields
        foreach (var module in modules)
        {
            DefineModuleType(module);
        }

        // Phase 5: Collect and define all class and function declarations
        foreach (var module in modules)
        {
            _currentModulePath = module.Path; // Track which module we're processing
            _currentDotNetNamespace = _moduleNamespaces.GetValueOrDefault(module.Path); // Set .NET namespace for this module
            foreach (var stmt in module.Statements)
            {
                if (stmt is Stmt.Class classStmt)
                {
                    DefineClass(classStmt);
                }
                else if (stmt is Stmt.Function funcStmt && funcStmt.Body != null)
                {
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
                else if (stmt is Stmt.Export export && export.Declaration != null)
                {
                    // Handle exported declarations
                    if (export.Declaration is Stmt.Class classDecl)
                    {
                        DefineClass(classDecl);
                    }
                    else if (export.Declaration is Stmt.Function funcDecl && funcDecl.Body != null)
                    {
                        DefineFunction(funcDecl);
                    }
                    else if (export.Declaration is Stmt.Enum enumDecl)
                    {
                        DefineEnum(enumDecl);
                    }
                    else if (export.Declaration is Stmt.Namespace nsDecl)
                    {
                        DefineNamespaceFields(nsDecl);
                    }
                }
            }
        }
        _currentModulePath = null;
        _currentDotNetNamespace = null;

        // Phase 5.5: Initialize typed interop support now that all classes are defined
        _unionGenerator = new UnionTypeGenerator(_typeMapper);
        _typeMapper.SetClassBuilders(_classBuilders);
        _typeMapper.SetUnionGenerator(_unionGenerator);

        // Phase 5.6: Initialize type emitter registry with external types for type-first dispatch
        _typeEmitterRegistry.SetExternalTypes(_externalTypes);

        // Register type-specific emitters
        var stringEmitter = new StringEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.String>(stringEmitter);
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.StringLiteral>(stringEmitter);

        var arrayEmitter = new ArrayEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Array>(arrayEmitter);

        var dateEmitter = new DateEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Date>(dateEmitter);

        var mapEmitter = new MapEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Map>(mapEmitter);

        var setEmitter = new SetEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.Set>(setEmitter);

        var weakMapEmitter = new WeakMapEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.WeakMap>(weakMapEmitter);

        var weakSetEmitter = new WeakSetEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.WeakSet>(weakSetEmitter);

        var regExpEmitter = new RegExpEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.RegExp>(regExpEmitter);

        var asyncGeneratorEmitter = new AsyncGeneratorEmitter();
        _typeEmitterRegistry.Register<TypeSystem.TypeInfo.AsyncGenerator>(asyncGeneratorEmitter);

        // Register static type emitters
        _typeEmitterRegistry.RegisterStatic("Math", new MathStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("JSON", new JSONStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Object", new ObjectStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Array", new ArrayStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Number", new NumberStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Promise", new PromiseStaticEmitter());
        _typeEmitterRegistry.RegisterStatic("Symbol", new SymbolStaticEmitter());

        // Phase 6: Collect all arrow functions
        CollectAndDefineArrowFunctions(allStatements);

        // Phase 7: Emit arrow function bodies
        EmitArrowFunctionBodies();

        // Phase 8: Emit method bodies
        foreach (var module in modules)
        {
            _currentModulePath = module.Path; // Track which module we're processing
            foreach (var stmt in module.Statements)
            {
                if (stmt is Stmt.Class classStmt)
                {
                    EmitClassMethods(classStmt);
                }
                else if (stmt is Stmt.Function funcStmt && funcStmt.Body != null)
                {
                    EmitFunctionBody(funcStmt);
                }
                else if (stmt is Stmt.Export export && export.Declaration != null)
                {
                    if (export.Declaration is Stmt.Class classDecl)
                    {
                        EmitClassMethods(classDecl);
                    }
                    else if (export.Declaration is Stmt.Function funcDecl && funcDecl.Body != null)
                    {
                        EmitFunctionBody(funcDecl);
                    }
                }
            }
        }
        _currentModulePath = null;

        // Phase 9: Emit module initialization methods
        foreach (var module in modules)
        {
            EmitModuleInit(module);
        }

        // Phase 10: Emit entry point that initializes modules in order
        EmitModulesEntryPoint(modules);

        // Phase 11: Finalize all types
        foreach (var tb in _displayClasses.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _classBuilders.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _moduleTypes.Values)
        {
            tb.CreateType();
        }
        _programType.CreateType();
    }

    public void Save(string outputPath)
    {
        // Generate metadata for the assembly
        MetadataBuilder metadataBuilder = _assemblyBuilder.GenerateMetadata(
            out BlobBuilder ilStream,
            out BlobBuilder fieldData);

        // Create an executable with entry point
        PEHeaderBuilder peHeader = PEHeaderBuilder.CreateExecutableHeader();

        ManagedPEBuilder peBuilder = new(
            header: peHeader,
            metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
            ilStream: ilStream,
            mappedFieldData: fieldData,
            entryPoint: _entryPoint != null
                ? MetadataTokens.MethodDefinitionHandle(_entryPoint.MetadataToken)
                : default);

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
    /// Defines static fields for top-level variables that are captured by functions.
    /// These variables cannot be accessed as locals from the entry point because functions
    /// may run in separate contexts (async state machines, nested calls, etc).
    /// </summary>
    private void DefineTopLevelCapturedVariables(List<Stmt> statements)
    {
        // Collect all top-level variable names
        var topLevelVars = new HashSet<string>();
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Var varStmt)
            {
                topLevelVars.Add(varStmt.Name.Lexeme);
            }
        }

        // Check which top-level variables are captured by regular functions
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Function funcStmt && !funcStmt.IsAsync && !funcStmt.IsGenerator)
            {
                var captures = _closureAnalyzer.GetCaptures(funcStmt);
                foreach (var capturedVar in captures)
                {
                    if (topLevelVars.Contains(capturedVar) && !_topLevelStaticVars.ContainsKey(capturedVar))
                    {
                        var field = _programType.DefineField(
                            $"$topLevel_{capturedVar}",
                            _types.Object,
                            FieldAttributes.Public | FieldAttributes.Static);
                        _topLevelStaticVars[capturedVar] = field;
                    }
                }
            }
        }

        // Check which top-level variables are captured by async functions
        foreach (var funcStmt in _asyncFunctions.Values)
        {
            var captures = _closureAnalyzer.GetCaptures(funcStmt);
            foreach (var capturedVar in captures)
            {
                if (topLevelVars.Contains(capturedVar) && !_topLevelStaticVars.ContainsKey(capturedVar))
                {
                    // Define a static field for this captured variable
                    // Use Public so async state machines (nested types) can access them
                    var field = _programType.DefineField(
                        $"$topLevel_{capturedVar}",
                        _types.Object,
                        FieldAttributes.Public | FieldAttributes.Static);
                    _topLevelStaticVars[capturedVar] = field;
                }
            }
        }

        // Also check async generator functions
        foreach (var funcStmt in _asyncGeneratorFunctions.Values)
        {
            var captures = _closureAnalyzer.GetCaptures(funcStmt);
            foreach (var capturedVar in captures)
            {
                if (topLevelVars.Contains(capturedVar) && !_topLevelStaticVars.ContainsKey(capturedVar))
                {
                    var field = _programType.DefineField(
                        $"$topLevel_{capturedVar}",
                        _types.Object,
                        FieldAttributes.Public | FieldAttributes.Static);
                    _topLevelStaticVars[capturedVar] = field;
                }
            }
        }
    }
}
