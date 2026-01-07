using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Modules;
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

    // Enum support
    private readonly Dictionary<string, Dictionary<string, object>> _enumMembers = [];
    private readonly Dictionary<string, Dictionary<double, string>> _enumReverse = [];
    private readonly Dictionary<string, EnumKind> _enumKinds = [];
    private readonly HashSet<string> _constEnums = [];
    private readonly bool _preserveConstEnums;

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

    // Async arrow function state machines (one per async arrow in async methods)
    private readonly Dictionary<Expr.ArrowFunction, AsyncArrowStateMachineBuilder> _asyncArrowBuilders = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, AsyncStateMachineBuilder> _asyncArrowOuterBuilders = new(ReferenceEqualityComparer.Instance);
    // For nested async arrows, maps to the parent arrow's builder (not the function's builder)
    private readonly Dictionary<Expr.ArrowFunction, AsyncArrowStateMachineBuilder> _asyncArrowParentBuilders = new(ReferenceEqualityComparer.Instance);

    // Module support
    private readonly Dictionary<string, TypeBuilder> _moduleTypes = [];
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _moduleExportFields = [];
    private readonly Dictionary<string, MethodBuilder> _moduleInitMethods = [];
    private ModuleResolver? _moduleResolver;
    private string? _currentModulePath; // Current module being compiled (for qualified type names)
    private readonly Dictionary<string, string> _classToModule = []; // Maps class name to module path for lookups
    private readonly Dictionary<string, string> _functionToModule = []; // Maps function name to module path for lookups
    private readonly Dictionary<string, string> _enumToModule = []; // Maps enum name to module path for lookups

    // Typed Interop: Real .NET property support
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _propertyBackingFields = [];
    private readonly Dictionary<string, Dictionary<string, PropertyBuilder>> _classProperties = [];
    private readonly Dictionary<string, HashSet<string>> _declaredPropertyNames = [];
    private readonly Dictionary<string, HashSet<string>> _readonlyPropertyNames = [];
    private readonly Dictionary<string, Dictionary<string, Type>> _propertyTypes = [];
    private readonly Dictionary<string, FieldBuilder> _extrasFields = []; // _extras dict field for dynamic properties
    private UnionTypeGenerator? _unionGenerator;

    // Shared context for definition phase (module name resolution)
    private CompilationContext? _definitionContext;

    // Entry point
    private MethodBuilder? _entryPoint;

    // Type provider for resolving .NET types (always runtime types for compilation)
    private readonly TypeProvider _types;

    // Whether to post-process the assembly for reference assembly compatibility
    private readonly bool _useReferenceAssemblies;
    private readonly string? _sdkPath;

    /// <summary>
    /// Creates a new IL compiler with default settings (runtime assembly mode).
    /// </summary>
    public ILCompiler(string assemblyName, bool preserveConstEnums = false)
        : this(assemblyName, preserveConstEnums, useReferenceAssemblies: false, sdkPath: null)
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
    {
        _assemblyName = assemblyName;
        _preserveConstEnums = preserveConstEnums;
        _useReferenceAssemblies = useReferenceAssemblies;
        _sdkPath = sdkPath;

        // Always use runtime types for compilation.
        // When --ref-asm is enabled, the assembly will be post-processed in Save()
        // to rewrite System.Private.CoreLib references to SDK assemblies.
        // This is necessary because MetadataLoadContext types cannot be used with
        // TypeBuilder.DefineType() for interface implementation (async/generator types).
        _types = TypeProvider.Runtime;

        _assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            _types.CoreAssembly
        );
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
        return _definitionContext;
    }

    public void Compile(List<Stmt> statements, TypeMap typeMap, DeadCodeInfo? deadCodeInfo = null)
    {
        // Store the type map and dead code info for use by ILEmitter
        _typeMap = typeMap;
        _deadCodeInfo = deadCodeInfo;

        // Phase 1: Emit runtime support types into the generated assembly
        // This makes compiled DLLs standalone without requiring SharpTS.dll
        _runtime = RuntimeEmitter.EmitAll(_moduleBuilder, _types);

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
        }

        // Phase 4.5: Initialize typed interop support now that all classes are defined
        _unionGenerator = new UnionTypeGenerator(_typeMapper);
        _typeMapper.SetClassBuilders(_classBuilders);
        _typeMapper.SetUnionGenerator(_unionGenerator);

        // Phase 5: Collect all arrow functions and generate methods/display classes
        CollectAndDefineArrowFunctions(statements);

        // Phase 6: Emit arrow function method bodies
        EmitArrowFunctionBodies();

        // Phase 6.3: Define all class methods (without bodies) so they're available for async
        // This populates _instanceMethods, _instanceGetters, _instanceSetters for direct dispatch
        DefineAllClassMethods(statements);

        // Phase 6.5: Emit async state machine bodies
        EmitAsyncStateMachineBodies();

        // Phase 6.6: Emit generator state machine bodies
        EmitGeneratorStateMachineBodies();

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

        // Phase 1: Emit runtime support types
        _runtime = RuntimeEmitter.EmitAll(_moduleBuilder, _types);

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
                }
            }
        }
        _currentModulePath = null;

        // Phase 5.5: Initialize typed interop support now that all classes are defined
        _unionGenerator = new UnionTypeGenerator(_typeMapper);
        _typeMapper.SetClassBuilders(_classBuilders);
        _typeMapper.SetUnionGenerator(_unionGenerator);

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
}
