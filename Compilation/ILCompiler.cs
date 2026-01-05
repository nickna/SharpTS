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
public class ILCompiler
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

    public ILCompiler(string assemblyName, bool preserveConstEnums = false)
    {
        _assemblyName = assemblyName;
        _preserveConstEnums = preserveConstEnums;
        _assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly
        );
        _moduleBuilder = _assemblyBuilder.DefineDynamicModule(assemblyName);
        _typeMapper = new TypeMapper(_moduleBuilder);
    }

    public void Compile(List<Stmt> statements, TypeMap typeMap, DeadCodeInfo? deadCodeInfo = null)
    {
        // Store the type map and dead code info for use by ILEmitter
        _typeMap = typeMap;
        _deadCodeInfo = deadCodeInfo;

        // Phase 1: Emit runtime support types into the generated assembly
        // This makes compiled DLLs standalone without requiring SharpTS.dll
        _runtime = RuntimeEmitter.EmitAll(_moduleBuilder);

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

        // Phase 5: Collect all arrow functions and generate methods/display classes
        CollectAndDefineArrowFunctions(statements);

        // Phase 6: Emit arrow function method bodies
        EmitArrowFunctionBodies();

        // Phase 6.3: Define all class methods (without bodies) so they're available for async
        // This populates _instanceMethods, _instanceGetters, _instanceSetters for direct dispatch
        DefineAllClassMethods(statements);

        // Phase 6.5: Emit async state machine bodies
        EmitAsyncStateMachineBodies();

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
        _runtime = RuntimeEmitter.EmitAll(_moduleBuilder);

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

        // Phase 6: Collect all arrow functions
        CollectAndDefineArrowFunctions(allStatements);

        // Phase 7: Emit arrow function bodies
        EmitArrowFunctionBodies();

        // Phase 8: Emit method bodies
        foreach (var module in modules)
        {
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

    /// <summary>
    /// Defines a module type with export fields.
    /// </summary>
    private void DefineModuleType(ParsedModule module)
    {
        // Create module class: $Module_<name>
        string moduleTypeName = $"$Module_{SanitizeModuleName(module.ModuleName)}";
        var moduleType = _moduleBuilder.DefineType(
            moduleTypeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract
        );

        _moduleTypes[module.Path] = moduleType;
        var exportFields = new Dictionary<string, FieldBuilder>();

        // Create export fields
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Export export)
            {
                if (export.IsDefaultExport)
                {
                    // Default export field
                    var field = moduleType.DefineField(
                        "$default",
                        typeof(object),
                        FieldAttributes.Public | FieldAttributes.Static
                    );
                    exportFields["$default"] = field;
                }
                else if (export.Declaration != null)
                {
                    // Named export from declaration
                    string? exportName = GetExportDeclarationName(export.Declaration);
                    if (exportName != null)
                    {
                        var field = moduleType.DefineField(
                            exportName,
                            typeof(object),
                            FieldAttributes.Public | FieldAttributes.Static
                        );
                        exportFields[exportName] = field;
                    }
                }
                else if (export.NamedExports != null && export.FromModulePath == null)
                {
                    // Named exports like export { x, y as z }
                    foreach (var spec in export.NamedExports)
                    {
                        string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                        if (!exportFields.ContainsKey(exportedName))
                        {
                            var field = moduleType.DefineField(
                                exportedName,
                                typeof(object),
                                FieldAttributes.Public | FieldAttributes.Static
                            );
                            exportFields[exportedName] = field;
                        }
                    }
                }
                else if (export.FromModulePath != null && _moduleResolver != null)
                {
                    // Re-export: export { x } from './module' or export * from './module'
                    string sourcePath = _moduleResolver.ResolveModulePath(export.FromModulePath, module.Path);

                    if (export.NamedExports != null)
                    {
                        // export { x, y as z } from './module'
                        foreach (var spec in export.NamedExports)
                        {
                            string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                            if (!exportFields.ContainsKey(exportedName))
                            {
                                var field = moduleType.DefineField(
                                    exportedName,
                                    typeof(object),
                                    FieldAttributes.Public | FieldAttributes.Static
                                );
                                exportFields[exportedName] = field;
                            }
                        }
                    }
                    else
                    {
                        // export * from './module' - need source module's exports
                        // Source module is processed first (topological order)
                        if (_moduleExportFields.TryGetValue(sourcePath, out var sourceFields))
                        {
                            foreach (var (name, _) in sourceFields)
                            {
                                if (name == "$default") continue;  // * doesn't include default
                                if (!exportFields.ContainsKey(name))
                                {
                                    var field = moduleType.DefineField(
                                        name,
                                        typeof(object),
                                        FieldAttributes.Public | FieldAttributes.Static
                                    );
                                    exportFields[name] = field;
                                }
                            }
                        }
                    }
                }
            }
        }

        _moduleExportFields[module.Path] = exportFields;
    }

    /// <summary>
    /// Gets the name of an exported declaration.
    /// </summary>
    private string? GetExportDeclarationName(Stmt decl) => decl switch
    {
        Stmt.Function f => f.Name.Lexeme,
        Stmt.Class c => c.Name.Lexeme,
        Stmt.Var v => v.Name.Lexeme,
        Stmt.Enum e => e.Name.Lexeme,
        Stmt.Interface or Stmt.TypeAlias => null, // Type-only, no runtime export
        _ => null
    };

    /// <summary>
    /// Sanitizes a module name for use as a type name.
    /// </summary>
    private string SanitizeModuleName(string name)
    {
        // Replace invalid characters
        return name.Replace("/", "_").Replace("\\", "_").Replace(".", "_").Replace("-", "_");
    }

    /// <summary>
    /// Emits the initialization method for a module.
    /// </summary>
    private void EmitModuleInit(ParsedModule module)
    {
        var moduleType = _moduleTypes[module.Path];
        var exportFields = _moduleExportFields[module.Path];

        // Create $Initialize method
        var initMethod = moduleType.DefineMethod(
            "$Initialize",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        _moduleInitMethods[module.Path] = initMethod;

        var il = initMethod.GetILGenerator();
        var ctx = CreateCompilationContext(il);
        ctx.CurrentModulePath = module.Path;
        ctx.ModuleExportFields = _moduleExportFields;
        ctx.ModuleTypes = _moduleTypes;
        ctx.ModuleResolver = _moduleResolver;

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in module.Statements)
        {
            // Skip class, function, interface, type alias, and enum declarations
            // (they are compiled separately in earlier phases)
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.TypeAlias or Stmt.Enum)
            {
                continue;
            }

            emitter.EmitStatement(stmt);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the entry point that initializes all modules in dependency order.
    /// </summary>
    private void EmitModulesEntryPoint(List<ParsedModule> modules)
    {
        var mainMethod = _programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        _entryPoint = mainMethod;

        var il = mainMethod.GetILGenerator();

        // Call each module's $Initialize method in dependency order
        foreach (var module in modules)
        {
            var initMethod = _moduleInitMethods[module.Path];
            il.Emit(OpCodes.Call, initMethod);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Creates a CompilationContext with common settings.
    /// </summary>
    private CompilationContext CreateCompilationContext(ILGenerator il)
    {
        return new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };
    }

    #region Arrow Function Collection

    private readonly List<(Expr.ArrowFunction Arrow, HashSet<string> Captures)> _collectedArrows = [];

    private void CollectAndDefineArrowFunctions(List<Stmt> statements)
    {
        // Walk the AST and collect all arrow functions
        foreach (var stmt in statements)
        {
            CollectArrowsFromStmt(stmt);
        }

        // Define methods and display classes
        foreach (var (arrow, captures) in _collectedArrows)
        {
            var paramTypes = arrow.Parameters.Select(_ => typeof(object)).ToArray();

            if (captures.Count == 0)
            {
                // Non-capturing: static method on $Program
                var methodBuilder = _programType.DefineMethod(
                    $"<>Arrow_{_arrowMethodCounter++}",
                    MethodAttributes.Private | MethodAttributes.Static,
                    typeof(object),
                    paramTypes
                );
                _arrowMethods[arrow] = methodBuilder;
            }
            else
            {
                // Capturing: create display class
                var displayClass = _moduleBuilder.DefineType(
                    $"<>c__DisplayClass{_displayClassCounter++}",
                    TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    typeof(object)
                );

                // Add fields for captured variables
                var fieldMap = new Dictionary<string, FieldBuilder>();
                foreach (var capturedVar in captures)
                {
                    var field = displayClass.DefineField(capturedVar, typeof(object), FieldAttributes.Public);
                    fieldMap[capturedVar] = field;
                }
                _displayClassFields[arrow] = fieldMap;

                // Add default constructor
                var ctorBuilder = displayClass.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    Type.EmptyTypes
                );
                var ctorIL = ctorBuilder.GetILGenerator();
                ctorIL.Emit(OpCodes.Ldarg_0);
                ctorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
                ctorIL.Emit(OpCodes.Ret);
                _displayClassConstructors[arrow] = ctorBuilder;

                // Add Invoke method
                var invokeMethod = displayClass.DefineMethod(
                    "Invoke",
                    MethodAttributes.Public,
                    typeof(object),
                    paramTypes
                );

                _displayClasses[arrow] = displayClass;
                _arrowMethods[arrow] = invokeMethod;
            }
        }
    }

    private void CollectArrowsFromStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                CollectArrowsFromExpr(e.Expr);
                break;
            case Stmt.Var v:
                if (v.Initializer != null)
                    CollectArrowsFromExpr(v.Initializer);
                break;
            case Stmt.Function f:
                // Skip overload signatures (no body)
                if (f.Body != null)
                {
                    foreach (var s in f.Body)
                        CollectArrowsFromStmt(s);
                }
                foreach (var p in f.Parameters)
                    if (p.DefaultValue != null)
                        CollectArrowsFromExpr(p.DefaultValue);
                break;
            case Stmt.Class c:
                foreach (var method in c.Methods)
                {
                    // Skip overload signatures (no body)
                    if (method.Body != null)
                        CollectArrowsFromStmt(method);
                }
                break;
            case Stmt.If i:
                CollectArrowsFromExpr(i.Condition);
                CollectArrowsFromStmt(i.ThenBranch);
                if (i.ElseBranch != null)
                    CollectArrowsFromStmt(i.ElseBranch);
                break;
            case Stmt.While w:
                CollectArrowsFromExpr(w.Condition);
                CollectArrowsFromStmt(w.Body);
                break;
            case Stmt.ForOf f:
                CollectArrowsFromExpr(f.Iterable);
                CollectArrowsFromStmt(f.Body);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    CollectArrowsFromStmt(s);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    CollectArrowsFromStmt(s);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    CollectArrowsFromExpr(r.Value);
                break;
            case Stmt.Switch s:
                CollectArrowsFromExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    CollectArrowsFromExpr(c.Value);
                    foreach (var cs in c.Body)
                        CollectArrowsFromStmt(cs);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        CollectArrowsFromStmt(ds);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    CollectArrowsFromStmt(ts);
                if (t.CatchBlock != null)
                    foreach (var cs in t.CatchBlock)
                        CollectArrowsFromStmt(cs);
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        CollectArrowsFromStmt(fs);
                break;
            case Stmt.Throw th:
                CollectArrowsFromExpr(th.Value);
                break;
            case Stmt.Print p:
                CollectArrowsFromExpr(p.Expr);
                break;
        }
    }

    private void CollectArrowsFromExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.ArrowFunction af:
                var captures = _closureAnalyzer.GetCaptures(af);
                _collectedArrows.Add((af, captures));
                // Also collect arrows inside this arrow's body
                if (af.ExpressionBody != null)
                    CollectArrowsFromExpr(af.ExpressionBody);
                if (af.BlockBody != null)
                    foreach (var s in af.BlockBody)
                        CollectArrowsFromStmt(s);
                break;
            case Expr.Binary b:
                CollectArrowsFromExpr(b.Left);
                CollectArrowsFromExpr(b.Right);
                break;
            case Expr.Logical l:
                CollectArrowsFromExpr(l.Left);
                CollectArrowsFromExpr(l.Right);
                break;
            case Expr.Unary u:
                CollectArrowsFromExpr(u.Right);
                break;
            case Expr.Grouping g:
                CollectArrowsFromExpr(g.Expression);
                break;
            case Expr.Call c:
                CollectArrowsFromExpr(c.Callee);
                foreach (var arg in c.Arguments)
                    CollectArrowsFromExpr(arg);
                break;
            case Expr.Get g:
                CollectArrowsFromExpr(g.Object);
                break;
            case Expr.Set s:
                CollectArrowsFromExpr(s.Object);
                CollectArrowsFromExpr(s.Value);
                break;
            case Expr.GetIndex gi:
                CollectArrowsFromExpr(gi.Object);
                CollectArrowsFromExpr(gi.Index);
                break;
            case Expr.SetIndex si:
                CollectArrowsFromExpr(si.Object);
                CollectArrowsFromExpr(si.Index);
                CollectArrowsFromExpr(si.Value);
                break;
            case Expr.Assign a:
                CollectArrowsFromExpr(a.Value);
                break;
            case Expr.New n:
                foreach (var arg in n.Arguments)
                    CollectArrowsFromExpr(arg);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    CollectArrowsFromExpr(elem);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    CollectArrowsFromExpr(prop.Value);
                break;
            case Expr.Ternary t:
                CollectArrowsFromExpr(t.Condition);
                CollectArrowsFromExpr(t.ThenBranch);
                CollectArrowsFromExpr(t.ElseBranch);
                break;
            case Expr.NullishCoalescing nc:
                CollectArrowsFromExpr(nc.Left);
                CollectArrowsFromExpr(nc.Right);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    CollectArrowsFromExpr(e);
                break;
            case Expr.CompoundAssign ca:
                CollectArrowsFromExpr(ca.Value);
                break;
            case Expr.CompoundSet cs:
                CollectArrowsFromExpr(cs.Object);
                CollectArrowsFromExpr(cs.Value);
                break;
            case Expr.CompoundSetIndex csi:
                CollectArrowsFromExpr(csi.Object);
                CollectArrowsFromExpr(csi.Index);
                CollectArrowsFromExpr(csi.Value);
                break;
            case Expr.PrefixIncrement pi:
                CollectArrowsFromExpr(pi.Operand);
                break;
            case Expr.PostfixIncrement poi:
                CollectArrowsFromExpr(poi.Operand);
                break;
        }
    }

    private void EmitArrowFunctionBodies()
    {
        foreach (var (arrow, captures) in _collectedArrows)
        {
            var methodBuilder = _arrowMethods[arrow];

            if (captures.Count == 0)
            {
                // Non-capturing: emit body into static method
                EmitArrowBody(arrow, methodBuilder, null);
            }
            else
            {
                // Capturing: emit body into display class method
                var displayClass = _displayClasses[arrow];
                EmitArrowBody(arrow, methodBuilder, displayClass);
            }
        }
    }

    private void EmitArrowBody(Expr.ArrowFunction arrow, MethodBuilder method, TypeBuilder? displayClass)
    {
        var il = method.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };

        if (displayClass != null)
        {
            // Instance method on display class - this is arg 0
            ctx.IsInstanceMethod = true;

            // Use the pre-stored field mapping
            if (_displayClassFields.TryGetValue(arrow, out var fieldMap))
            {
                ctx.CapturedFields = fieldMap;
            }
            else
            {
                ctx.CapturedFields = [];
            }

            // Parameters start at index 1
            for (int i = 0; i < arrow.Parameters.Count; i++)
            {
                ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i + 1);
            }
        }
        else
        {
            // Static method - parameters start at index 0
            for (int i = 0; i < arrow.Parameters.Count; i++)
            {
                ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i);
            }
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks
        emitter.EmitDefaultParameters(arrow.Parameters, displayClass != null);

        if (arrow.ExpressionBody != null)
        {
            // Expression body: emit expression and return
            emitter.EmitExpression(arrow.ExpressionBody);
            emitter.EmitBoxIfNeeded(arrow.ExpressionBody);
            il.Emit(OpCodes.Ret);
        }
        else if (arrow.BlockBody != null)
        {
            // Block body: emit statements
            foreach (var stmt in arrow.BlockBody)
            {
                emitter.EmitStatement(stmt);
            }
            // Finalize any deferred returns from exception blocks
            if (emitter.HasDeferredReturns)
            {
                emitter.FinalizeReturns();
            }
            else
            {
                // Default return null
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    #endregion

    private void DefineClass(Stmt.Class classStmt)
    {
        Type? baseType = null;
        if (classStmt.Superclass != null && _classBuilders.TryGetValue(classStmt.Superclass.Lexeme, out var superBuilder))
        {
            baseType = superBuilder;
        }

        // Set TypeAttributes.Abstract if the class is abstract
        TypeAttributes typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (classStmt.IsAbstract)
        {
            typeAttrs |= TypeAttributes.Abstract;
        }

        var typeBuilder = _moduleBuilder.DefineType(
            classStmt.Name.Lexeme,
            typeAttrs,
            baseType
        );

        // Track superclass for inheritance-aware method resolution
        _classSuperclass[classStmt.Name.Lexeme] = classStmt.Superclass?.Lexeme;

        // Handle generic type parameters
        if (classStmt.TypeParams != null && classStmt.TypeParams.Count > 0)
        {
            string[] typeParamNames = classStmt.TypeParams.Select(tp => tp.Name.Lexeme).ToArray();
            var genericParams = typeBuilder.DefineGenericParameters(typeParamNames);

            // Apply constraints
            for (int i = 0; i < classStmt.TypeParams.Count; i++)
            {
                var constraint = classStmt.TypeParams[i].Constraint;
                if (constraint != null)
                {
                    Type constraintType = ResolveConstraintType(constraint);
                    if (constraintType.IsInterface)
                        genericParams[i].SetInterfaceConstraints(constraintType);
                    else
                        genericParams[i].SetBaseTypeConstraint(constraintType);
                }
            }

            _classGenericParams[classStmt.Name.Lexeme] = genericParams;
        }

        // Add _fields dictionary for dynamic property storage
        var fieldsField = typeBuilder.DefineField(
            "_fields",
            typeof(Dictionary<string, object>),
            FieldAttributes.Private
        );
        _instanceFieldsField[classStmt.Name.Lexeme] = fieldsField;

        // Add static fields for static properties
        var staticFieldBuilders = new Dictionary<string, FieldBuilder>();
        foreach (var field in classStmt.Fields)
        {
            if (field.IsStatic)
            {
                var fieldBuilder = typeBuilder.DefineField(
                    field.Name.Lexeme,
                    typeof(object),
                    FieldAttributes.Public | FieldAttributes.Static
                );
                staticFieldBuilders[field.Name.Lexeme] = fieldBuilder;
            }
        }

        _classBuilders[classStmt.Name.Lexeme] = typeBuilder;
        _staticFields[classStmt.Name.Lexeme] = staticFieldBuilders;
    }

    private void DefineFunction(Stmt.Function funcStmt)
    {
        // Check if this is an async function - use native IL state machine
        if (funcStmt.IsAsync)
        {
            DefineAsyncFunction(funcStmt);
            return;
        }

        var paramTypes = funcStmt.Parameters.Select(_ => typeof(object)).ToArray();
        var methodBuilder = _programType.DefineMethod(
            funcStmt.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            paramTypes
        );

        // Handle generic type parameters
        bool isGeneric = funcStmt.TypeParams != null && funcStmt.TypeParams.Count > 0;
        _isGenericFunction[funcStmt.Name.Lexeme] = isGeneric;

        if (isGeneric)
        {
            string[] typeParamNames = funcStmt.TypeParams!.Select(tp => tp.Name.Lexeme).ToArray();
            var genericParams = methodBuilder.DefineGenericParameters(typeParamNames);

            // Apply constraints
            for (int i = 0; i < funcStmt.TypeParams!.Count; i++)
            {
                var constraint = funcStmt.TypeParams[i].Constraint;
                if (constraint != null)
                {
                    Type constraintType = ResolveConstraintType(constraint);
                    if (constraintType.IsInterface)
                        genericParams[i].SetInterfaceConstraints(constraintType);
                    else
                        genericParams[i].SetBaseTypeConstraint(constraintType);
                }
            }

            _functionGenericParams[funcStmt.Name.Lexeme] = genericParams;
        }

        _functionBuilders[funcStmt.Name.Lexeme] = methodBuilder;

        // Track rest parameter info
        var restParam = funcStmt.Parameters.FirstOrDefault(p => p.IsRest);
        if (restParam != null)
        {
            int restIndex = funcStmt.Parameters.IndexOf(restParam);
            int regularCount = funcStmt.Parameters.Count(p => !p.IsRest);
            _functionRestParams[funcStmt.Name.Lexeme] = (restIndex, regularCount);
        }
    }

    private void DefineAsyncFunction(Stmt.Function funcStmt)
    {
        // Analyze the async function for await points and hoisted variables
        var analysis = _asyncAnalyzer.Analyze(funcStmt);

        // Create state machine builder
        var smBuilder = new AsyncStateMachineBuilder(_moduleBuilder, _asyncStateMachineCounter++);
        var hasAsyncArrows = analysis.AsyncArrows.Count > 0;
        smBuilder.DefineStateMachine(funcStmt.Name.Lexeme, analysis, typeof(object), false, hasAsyncArrows);

        // Define stub method (returns Task<object>)
        var paramTypes = funcStmt.Parameters.Select(_ => typeof(object)).ToArray();
        var stubMethod = _programType.DefineMethod(
            funcStmt.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<object>),
            paramTypes
        );

        // Store for later body emission
        _functionBuilders[funcStmt.Name.Lexeme] = stubMethod;
        _asyncStateMachines[funcStmt.Name.Lexeme] = smBuilder;
        _asyncFunctions[funcStmt.Name.Lexeme] = funcStmt;

        // Build state machines for any async arrows found in this function
        DefineAsyncArrowStateMachines(analysis.AsyncArrows, smBuilder);
    }

    private void DefineAsyncArrowStateMachines(
        List<AsyncStateAnalyzer.AsyncArrowInfo> asyncArrows,
        AsyncStateMachineBuilder outerBuilder)
    {
        // Get all hoisted fields from the function's state machine
        var functionHoistedFields = new Dictionary<string, FieldBuilder>();
        foreach (var (name, field) in outerBuilder.HoistedParameters)
            functionHoistedFields[name] = field;
        foreach (var (name, field) in outerBuilder.HoistedLocals)
            functionHoistedFields[name] = field;

        // Sort arrows by nesting level to ensure parents are defined before children
        var sortedArrows = asyncArrows.OrderBy(a => a.NestingLevel).ToList();

        // Build a set of arrows that have nested async children
        var arrowsWithNestedChildren = new HashSet<Expr.ArrowFunction>(ReferenceEqualityComparer.Instance);
        foreach (var arrowInfo in sortedArrows)
        {
            if (arrowInfo.ParentArrow != null)
            {
                arrowsWithNestedChildren.Add(arrowInfo.ParentArrow);
            }
        }

        foreach (var arrowInfo in sortedArrows)
        {
            // Create a dedicated analyzer for this arrow's await points
            var arrowAnalysis = AnalyzeAsyncArrow(arrowInfo.Arrow);

            // Create state machine builder for the async arrow
            var arrowBuilder = new AsyncArrowStateMachineBuilder(
                _moduleBuilder,
                arrowInfo.Arrow,
                arrowInfo.Captures,
                _asyncArrowCounter++);

            // Determine the outer state machine type and hoisted fields
            Type outerStateMachineType;
            Dictionary<string, FieldBuilder> outerHoistedFields;

            // Check if this arrow has nested async children
            bool hasNestedChildren = arrowsWithNestedChildren.Contains(arrowInfo.Arrow);

            if (arrowInfo.ParentArrow == null)
            {
                // Direct child of the function - use function's state machine
                outerStateMachineType = outerBuilder.StateMachineType;
                outerHoistedFields = functionHoistedFields;
                _asyncArrowOuterBuilders[arrowInfo.Arrow] = outerBuilder;
            }
            else
            {
                // Nested arrow - use parent arrow's state machine
                if (!_asyncArrowBuilders.TryGetValue(arrowInfo.ParentArrow, out var parentBuilder))
                {
                    throw new InvalidOperationException(
                        $"Parent async arrow not found. Nesting level: {arrowInfo.NestingLevel}");
                }

                outerStateMachineType = parentBuilder.StateMachineType;

                // Get hoisted fields from parent arrow - includes its parameters, locals, and captured fields
                outerHoistedFields = [];
                foreach (var (name, field) in parentBuilder.ParameterFields)
                    outerHoistedFields[name] = field;
                foreach (var (name, field) in parentBuilder.LocalFields)
                    outerHoistedFields[name] = field;
                // Also include captured fields - they're accessible through parent's outer reference
                // These are "transitive" captures - we need to go through parent's <>__outer to access them
                HashSet<string> transitiveCaptures = [];
                foreach (var (name, field) in parentBuilder.CapturedFieldMap)
                {
                    outerHoistedFields[name] = field;
                    transitiveCaptures.Add(name);
                }
                // Also include parent's transitive captures (for deeper nesting)
                foreach (var name in parentBuilder.TransitiveCaptures)
                {
                    transitiveCaptures.Add(name);
                }

                _asyncArrowParentBuilders[arrowInfo.Arrow] = parentBuilder;

                // Pass transitive info for nested arrows
                arrowBuilder.DefineStateMachine(
                    outerStateMachineType,
                    outerHoistedFields,
                    arrowAnalysis.AwaitCount,
                    arrowInfo.Arrow.Parameters,
                    arrowAnalysis.HoistedLocals,
                    transitiveCaptures,
                    parentBuilder.OuterStateMachineField,
                    parentBuilder.OuterStateMachineType,
                    hasNestedChildren);

                // Define the stub method that will be called to invoke the async arrow
                arrowBuilder.DefineStubMethod(_programType);

                _asyncArrowBuilders[arrowInfo.Arrow] = arrowBuilder;
                continue; // Already handled the full setup
            }

            arrowBuilder.DefineStateMachine(
                outerStateMachineType,
                outerHoistedFields,
                arrowAnalysis.AwaitCount,
                arrowInfo.Arrow.Parameters,
                arrowAnalysis.HoistedLocals,
                hasNestedAsyncArrows: hasNestedChildren);

            // Define the stub method that will be called to invoke the async arrow
            arrowBuilder.DefineStubMethod(_programType);

            _asyncArrowBuilders[arrowInfo.Arrow] = arrowBuilder;
        }
    }

    /// <summary>
    /// Analyzes an async arrow function to determine its await points and hoisted variables.
    /// </summary>
    private (int AwaitCount, HashSet<string> HoistedLocals) AnalyzeAsyncArrow(Expr.ArrowFunction arrow)
    {
        var awaitCount = 0;
        var declaredVariables = new HashSet<string>();
        var variablesUsedAfterAwait = new HashSet<string>();
        var variablesDeclaredBeforeAwait = new HashSet<string>();
        var seenAwait = false;

        // Add parameters as declared variables
        foreach (var param in arrow.Parameters)
        {
            declaredVariables.Add(param.Name.Lexeme);
            variablesDeclaredBeforeAwait.Add(param.Name.Lexeme);
        }

        // Analyze expression body or block body
        if (arrow.ExpressionBody != null)
        {
            AnalyzeArrowExprForAwaits(arrow.ExpressionBody, ref awaitCount, ref seenAwait,
                declaredVariables, variablesUsedAfterAwait, variablesDeclaredBeforeAwait);
        }
        else if (arrow.BlockBody != null)
        {
            foreach (var stmt in arrow.BlockBody)
            {
                AnalyzeArrowStmtForAwaits(stmt, ref awaitCount, ref seenAwait,
                    declaredVariables, variablesUsedAfterAwait, variablesDeclaredBeforeAwait);
            }
        }

        // Variables that need hoisting: declared before await AND used after await
        var hoistedLocals = new HashSet<string>(variablesDeclaredBeforeAwait);
        hoistedLocals.IntersectWith(variablesUsedAfterAwait);

        // Remove parameters from hoisted locals (they're stored separately)
        foreach (var param in arrow.Parameters)
            hoistedLocals.Remove(param.Name.Lexeme);

        return (awaitCount, hoistedLocals);
    }

    private void AnalyzeArrowStmtForAwaits(Stmt stmt, ref int awaitCount, ref bool seenAwait,
        HashSet<string> declaredVariables, HashSet<string> usedAfterAwait, HashSet<string> declaredBeforeAwait)
    {
        switch (stmt)
        {
            case Stmt.Var v:
                declaredVariables.Add(v.Name.Lexeme);
                if (!seenAwait)
                    declaredBeforeAwait.Add(v.Name.Lexeme);
                if (v.Initializer != null)
                    AnalyzeArrowExprForAwaits(v.Initializer, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Expression e:
                AnalyzeArrowExprForAwaits(e.Expr, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    AnalyzeArrowExprForAwaits(r.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.If i:
                AnalyzeArrowExprForAwaits(i.Condition, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowStmtForAwaits(i.ThenBranch, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                if (i.ElseBranch != null)
                    AnalyzeArrowStmtForAwaits(i.ElseBranch, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.While w:
                AnalyzeArrowExprForAwaits(w.Condition, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowStmtForAwaits(w.Body, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.ForOf f:
                declaredVariables.Add(f.Variable.Lexeme);
                if (!seenAwait)
                    declaredBeforeAwait.Add(f.Variable.Lexeme);
                AnalyzeArrowExprForAwaits(f.Iterable, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowStmtForAwaits(f.Body, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    AnalyzeArrowStmtForAwaits(s, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    AnalyzeArrowStmtForAwaits(s, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    AnalyzeArrowStmtForAwaits(ts, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                if (t.CatchBlock != null)
                {
                    if (t.CatchParam != null)
                    {
                        declaredVariables.Add(t.CatchParam.Lexeme);
                        if (!seenAwait)
                            declaredBeforeAwait.Add(t.CatchParam.Lexeme);
                    }
                    foreach (var cs in t.CatchBlock)
                        AnalyzeArrowStmtForAwaits(cs, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                }
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        AnalyzeArrowStmtForAwaits(fs, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Switch s:
                AnalyzeArrowExprForAwaits(s.Subject, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                foreach (var c in s.Cases)
                {
                    AnalyzeArrowExprForAwaits(c.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                    foreach (var cs in c.Body)
                        AnalyzeArrowStmtForAwaits(cs, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        AnalyzeArrowStmtForAwaits(ds, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Throw th:
                AnalyzeArrowExprForAwaits(th.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Print p:
                AnalyzeArrowExprForAwaits(p.Expr, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
        }
    }

    private void AnalyzeArrowExprForAwaits(Expr expr, ref int awaitCount, ref bool seenAwait,
        HashSet<string> declaredVariables, HashSet<string> usedAfterAwait, HashSet<string> declaredBeforeAwait)
    {
        switch (expr)
        {
            case Expr.Await a:
                awaitCount++;
                seenAwait = true;
                AnalyzeArrowExprForAwaits(a.Expression, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Variable v:
                if (seenAwait && declaredVariables.Contains(v.Name.Lexeme))
                    usedAfterAwait.Add(v.Name.Lexeme);
                break;
            case Expr.Assign a:
                if (seenAwait && declaredVariables.Contains(a.Name.Lexeme))
                    usedAfterAwait.Add(a.Name.Lexeme);
                AnalyzeArrowExprForAwaits(a.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Binary b:
                AnalyzeArrowExprForAwaits(b.Left, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(b.Right, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Logical l:
                AnalyzeArrowExprForAwaits(l.Left, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(l.Right, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Unary u:
                AnalyzeArrowExprForAwaits(u.Right, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Grouping g:
                AnalyzeArrowExprForAwaits(g.Expression, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Call c:
                AnalyzeArrowExprForAwaits(c.Callee, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                foreach (var arg in c.Arguments)
                    AnalyzeArrowExprForAwaits(arg, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Get g:
                AnalyzeArrowExprForAwaits(g.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Set s:
                AnalyzeArrowExprForAwaits(s.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(s.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.GetIndex gi:
                AnalyzeArrowExprForAwaits(gi.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(gi.Index, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.SetIndex si:
                AnalyzeArrowExprForAwaits(si.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(si.Index, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(si.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.New n:
                foreach (var arg in n.Arguments)
                    AnalyzeArrowExprForAwaits(arg, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    AnalyzeArrowExprForAwaits(elem, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    AnalyzeArrowExprForAwaits(prop.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Ternary t:
                AnalyzeArrowExprForAwaits(t.Condition, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(t.ThenBranch, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(t.ElseBranch, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.NullishCoalescing nc:
                AnalyzeArrowExprForAwaits(nc.Left, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(nc.Right, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    AnalyzeArrowExprForAwaits(e, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.CompoundAssign ca:
                if (seenAwait && declaredVariables.Contains(ca.Name.Lexeme))
                    usedAfterAwait.Add(ca.Name.Lexeme);
                AnalyzeArrowExprForAwaits(ca.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.CompoundSet cs:
                AnalyzeArrowExprForAwaits(cs.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(cs.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.CompoundSetIndex csi:
                AnalyzeArrowExprForAwaits(csi.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(csi.Index, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(csi.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.PrefixIncrement pi:
                AnalyzeArrowExprForAwaits(pi.Operand, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.PostfixIncrement poi:
                AnalyzeArrowExprForAwaits(poi.Operand, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.ArrowFunction:
                // Nested arrows don't contribute to this arrow's await analysis
                break;
        }
    }

    private void EmitAsyncStateMachineBodies()
    {
        foreach (var (funcName, smBuilder) in _asyncStateMachines)
        {
            var func = _asyncFunctions[funcName];
            var stubMethod = _functionBuilders[funcName];
            var analysis = _asyncAnalyzer.Analyze(func);

            // Emit stub method body
            EmitAsyncStubMethod(stubMethod, smBuilder, func.Parameters);

            // Create context for MoveNext emission
            var il = smBuilder.MoveNextMethod.GetILGenerator();
            var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
            {
                Runtime = _runtime,
                ClassConstructors = _classConstructors,
                ClosureAnalyzer = _closureAnalyzer,
                ArrowMethods = _arrowMethods,
                DisplayClasses = _displayClasses,
                DisplayClassFields = _displayClassFields,
                DisplayClassConstructors = _displayClassConstructors,
                StaticFields = _staticFields,
                StaticMethods = _staticMethods,
                EnumMembers = _enumMembers,
                EnumReverse = _enumReverse,
                EnumKinds = _enumKinds,
                FunctionRestParams = _functionRestParams,
                ClassGenericParams = _classGenericParams,
                FunctionGenericParams = _functionGenericParams,
                IsGenericFunction = _isGenericFunction,
                TypeMap = _typeMap,
                DeadCode = _deadCodeInfo,
                InstanceMethods = _instanceMethods,
                InstanceGetters = _instanceGetters,
                InstanceSetters = _instanceSetters,
                ClassSuperclass = _classSuperclass,
                AsyncMethods = null,
                AsyncArrowBuilders = _asyncArrowBuilders,
                AsyncArrowOuterBuilders = _asyncArrowOuterBuilders,
                AsyncArrowParentBuilders = _asyncArrowParentBuilders
            };

            // Emit MoveNext body
            var moveNextEmitter = new AsyncMoveNextEmitter(smBuilder, analysis);
            moveNextEmitter.EmitMoveNext(func.Body, ctx, typeof(object));

            // Emit async arrow MoveNext bodies
            foreach (var arrowInfo in analysis.AsyncArrows)
            {
                if (_asyncArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
                {
                    EmitAsyncArrowMoveNext(arrowBuilder, arrowInfo.Arrow, ctx);
                }
            }

            // Finalize state machine type
            smBuilder.CreateType();
        }

        // Finalize all async arrow state machine types
        foreach (var (_, arrowBuilder) in _asyncArrowBuilders)
        {
            arrowBuilder.CreateType();
        }
    }

    private void EmitAsyncArrowMoveNext(AsyncArrowStateMachineBuilder arrowBuilder, Expr.ArrowFunction arrow, CompilationContext parentCtx)
    {
        // Create IL generator for the arrow's MoveNext
        var il = arrowBuilder.MoveNextMethod.GetILGenerator();

        // Create analysis for this arrow
        var arrowAnalysis = AnalyzeAsyncArrow(arrow);
        var analysis = new AsyncStateAnalyzer.AsyncFunctionAnalysis(
            arrowAnalysis.AwaitCount,
            [], // We'll regenerate await points during emission
            arrowAnalysis.HoistedLocals,
            new HashSet<string>(arrow.Parameters.Select(p => p.Name.Lexeme)),
            false, // HasTryCatch - will be detected during emission
            arrowBuilder.Captures.Contains("this"),
            [] // No nested async arrows handled yet
        );

        // Create a new context for arrow MoveNext emission
        var ctx = new CompilationContext(il, parentCtx.TypeMapper, parentCtx.Functions, parentCtx.Classes)
        {
            Runtime = parentCtx.Runtime,
            ClassConstructors = parentCtx.ClassConstructors,
            ClosureAnalyzer = parentCtx.ClosureAnalyzer,
            ArrowMethods = parentCtx.ArrowMethods,
            DisplayClasses = parentCtx.DisplayClasses,
            DisplayClassFields = parentCtx.DisplayClassFields,
            DisplayClassConstructors = parentCtx.DisplayClassConstructors,
            StaticFields = parentCtx.StaticFields,
            StaticMethods = parentCtx.StaticMethods,
            EnumMembers = parentCtx.EnumMembers,
            EnumReverse = parentCtx.EnumReverse,
            EnumKinds = parentCtx.EnumKinds,
            FunctionRestParams = parentCtx.FunctionRestParams,
            ClassGenericParams = parentCtx.ClassGenericParams,
            FunctionGenericParams = parentCtx.FunctionGenericParams,
            IsGenericFunction = parentCtx.IsGenericFunction,
            TypeMap = parentCtx.TypeMap,
            DeadCode = parentCtx.DeadCode,
            InstanceMethods = parentCtx.InstanceMethods,
            InstanceGetters = parentCtx.InstanceGetters,
            InstanceSetters = parentCtx.InstanceSetters,
            ClassSuperclass = parentCtx.ClassSuperclass,
            AsyncMethods = null,
            AsyncArrowBuilders = _asyncArrowBuilders,
            AsyncArrowOuterBuilders = _asyncArrowOuterBuilders,
            AsyncArrowParentBuilders = _asyncArrowParentBuilders
        };

        // Create arrow-specific emitter
        var arrowEmitter = new AsyncArrowMoveNextEmitter(arrowBuilder, analysis);

        // Get the body statements
        List<Stmt> bodyStatements;
        if (arrow.BlockBody != null)
        {
            bodyStatements = arrow.BlockBody;
        }
        else if (arrow.ExpressionBody != null)
        {
            // Create a synthetic return statement for expression body arrows
            var returnToken = new Token(TokenType.RETURN, "return", null, 0);
            bodyStatements = [new Stmt.Return(returnToken, arrow.ExpressionBody)];
        }
        else
        {
            bodyStatements = [];
        }

        arrowEmitter.EmitMoveNext(bodyStatements, ctx, typeof(object));
    }

    private void EmitAsyncStubMethod(MethodBuilder stubMethod, AsyncStateMachineBuilder smBuilder, List<Stmt.Parameter> parameters, bool isInstanceMethod = false)
    {
        var il = stubMethod.GetILGenerator();
        var smLocal = il.DeclareLocal(smBuilder.StateMachineType);

        // var sm = default(<StateMachine>);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, smBuilder.StateMachineType);

        // Copy 'this' to state machine if this is an instance method and uses 'this'
        if (isInstanceMethod && smBuilder.ThisField != null)
        {
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldarg_0);  // 'this' is arg 0 for instance methods
            il.Emit(OpCodes.Stfld, smBuilder.ThisField);
        }

        // Copy parameters to state machine fields
        // For instance methods, parameters start at arg 1 (arg 0 is 'this')
        int paramOffset = isInstanceMethod ? 1 : 0;
        for (int i = 0; i < parameters.Count; i++)
        {
            string paramName = parameters[i].Name.Lexeme;
            if (smBuilder.HoistedParameters.TryGetValue(paramName, out var field))
            {
                il.Emit(OpCodes.Ldloca, smLocal);
                il.Emit(OpCodes.Ldarg, i + paramOffset);
                il.Emit(OpCodes.Stfld, field);
            }
        }

        // sm.<>t__builder = AsyncTaskMethodBuilder<T>.Create();
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Call, smBuilder.GetBuilderCreateMethod());
        il.Emit(OpCodes.Stfld, smBuilder.BuilderField);

        // sm.<>1__state = -1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, smBuilder.StateField);

        // If this function has async arrows, we need to box the state machine first
        // and store the boxed reference so async arrows can share the same instance
        if (smBuilder.SelfBoxedField != null)
        {
            // Box the state machine to get a heap-allocated copy
            il.Emit(OpCodes.Ldloc, smLocal);
            il.Emit(OpCodes.Box, smBuilder.StateMachineType);
            var boxedLocal = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Stloc, boxedLocal);

            // Store the boxed reference in the state machine
            // Use Unbox to get a pointer to the boxed value, then store the reference there
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, smBuilder.StateMachineType);
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Stfld, smBuilder.SelfBoxedField);

            // Now call Start on the BOXED state machine (cast to IAsyncStateMachine)
            // builder.Start expects ref TSM, so we use Unbox to get the pointer
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, smBuilder.StateMachineType);
            il.Emit(OpCodes.Ldflda, smBuilder.BuilderField);
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, smBuilder.StateMachineType);
            il.Emit(OpCodes.Call, smBuilder.GetBuilderStartMethod());

            // return boxed.<>t__builder.Task
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, smBuilder.StateMachineType);
            il.Emit(OpCodes.Ldflda, smBuilder.BuilderField);
            il.Emit(OpCodes.Call, smBuilder.GetBuilderTaskGetter());
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // Standard path: use stack-based state machine (runtime boxes internally)
            // sm.<>t__builder.Start(ref sm);
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldflda, smBuilder.BuilderField);
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Call, smBuilder.GetBuilderStartMethod());

            // return sm.<>t__builder.Task;
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldflda, smBuilder.BuilderField);
            il.Emit(OpCodes.Call, smBuilder.GetBuilderTaskGetter());
            il.Emit(OpCodes.Ret);
        }
    }

    private void DefineEnum(Stmt.Enum enumStmt)
    {
        Dictionary<string, object> members = [];
        Dictionary<double, string> reverse = [];
        double? currentNumericValue = null;
        bool hasNumeric = false;
        bool hasString = false;

        foreach (var member in enumStmt.Members)
        {
            if (member.Value is Expr.Literal lit)
            {
                if (lit.Value is double d)
                {
                    members[member.Name.Lexeme] = d;
                    reverse[d] = member.Name.Lexeme;  // Only numeric values get reverse mapping
                    currentNumericValue = d + 1;
                    hasNumeric = true;
                }
                else if (lit.Value is string s)
                {
                    members[member.Name.Lexeme] = s;
                    // No reverse mapping for string values
                    hasString = true;
                }
            }
            else if (enumStmt.IsConst && member.Value != null)
            {
                // Const enums support computed expressions - evaluate at compile time
                var computedValue = EvaluateConstEnumExpression(member.Value, members, enumStmt.Name.Lexeme);
                if (computedValue is double d)
                {
                    members[member.Name.Lexeme] = d;
                    reverse[d] = member.Name.Lexeme;
                    currentNumericValue = d + 1;
                    hasNumeric = true;
                }
                else if (computedValue is string s)
                {
                    members[member.Name.Lexeme] = s;
                    hasString = true;
                }
            }
            else if (member.Value == null)
            {
                // Auto-increment
                currentNumericValue ??= 0;
                members[member.Name.Lexeme] = currentNumericValue.Value;
                reverse[currentNumericValue.Value] = member.Name.Lexeme;
                hasNumeric = true;
                currentNumericValue++;
            }
        }

        EnumKind kind = (hasNumeric, hasString) switch
        {
            (true, false) => EnumKind.Numeric,
            (false, true) => EnumKind.String,
            (true, true) => EnumKind.Heterogeneous,
            _ => EnumKind.Numeric
        };

        _enumMembers[enumStmt.Name.Lexeme] = members;
        _enumReverse[enumStmt.Name.Lexeme] = reverse;
        _enumKinds[enumStmt.Name.Lexeme] = kind;

        // Track const enums
        if (enumStmt.IsConst)
        {
            _constEnums.Add(enumStmt.Name.Lexeme);
        }
    }

    /// <summary>
    /// Evaluates a constant expression for const enum members during compilation.
    /// </summary>
    private object EvaluateConstEnumExpression(Expr expr, Dictionary<string, object> resolvedMembers, string enumName)
    {
        return expr switch
        {
            Expr.Literal lit => lit.Value ?? throw new Exception($"Compile Error: Const enum expression cannot be null."),

            Expr.Get g when g.Object is Expr.Variable v && v.Name.Lexeme == enumName =>
                resolvedMembers.TryGetValue(g.Name.Lexeme, out var val)
                    ? val
                    : throw new Exception($"Compile Error: Const enum member '{g.Name.Lexeme}' referenced before definition."),

            Expr.Grouping gr => EvaluateConstEnumExpression(gr.Expression, resolvedMembers, enumName),

            Expr.Unary u => EvaluateConstEnumUnary(u, resolvedMembers, enumName),

            Expr.Binary b => EvaluateConstEnumBinary(b, resolvedMembers, enumName),

            _ => throw new Exception($"Compile Error: Expression type '{expr.GetType().Name}' is not allowed in const enum initializer.")
        };
    }

    private object EvaluateConstEnumUnary(Expr.Unary unary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var operand = EvaluateConstEnumExpression(unary.Right, resolvedMembers, enumName);

        return unary.Operator.Type switch
        {
            TokenType.MINUS when operand is double d => -d,
            TokenType.PLUS when operand is double d => d,
            TokenType.TILDE when operand is double d => (double)(~(int)d),
            _ => throw new Exception($"Compile Error: Operator '{unary.Operator.Lexeme}' is not allowed in const enum expressions.")
        };
    }

    private object EvaluateConstEnumBinary(Expr.Binary binary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var left = EvaluateConstEnumExpression(binary.Left, resolvedMembers, enumName);
        var right = EvaluateConstEnumExpression(binary.Right, resolvedMembers, enumName);

        if (left is double l && right is double r)
        {
            return binary.Operator.Type switch
            {
                TokenType.PLUS => l + r,
                TokenType.MINUS => l - r,
                TokenType.STAR => l * r,
                TokenType.SLASH => l / r,
                TokenType.PERCENT => l % r,
                TokenType.STAR_STAR => Math.Pow(l, r),
                TokenType.AMPERSAND => (double)((int)l & (int)r),
                TokenType.PIPE => (double)((int)l | (int)r),
                TokenType.CARET => (double)((int)l ^ (int)r),
                TokenType.LESS_LESS => (double)((int)l << (int)r),
                TokenType.GREATER_GREATER => (double)((int)l >> (int)r),
                _ => throw new Exception($"Compile Error: Operator '{binary.Operator.Lexeme}' is not allowed in const enum expressions.")
            };
        }

        if (left is string ls && right is string rs && binary.Operator.Type == TokenType.PLUS)
        {
            return ls + rs;
        }

        throw new Exception($"Compile Error: Invalid operand types for operator '{binary.Operator.Lexeme}' in const enum expression.");
    }

    /// <summary>
    /// Defines all class methods (without emitting bodies) so they're available for
    /// direct dispatch in async state machines.
    /// </summary>
    private void DefineAllClassMethods(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt)
            {
                DefineClassMethodsOnly(classStmt);
            }
        }
    }

    /// <summary>
    /// Defines method signatures and registers them in _instanceMethods without emitting bodies.
    /// Also pre-defines the constructor so it's available for EmitNew in async contexts.
    /// </summary>
    private void DefineClassMethodsOnly(Stmt.Class classStmt)
    {
        var typeBuilder = _classBuilders[classStmt.Name.Lexeme];

        // Pre-define constructor (if not already defined)
        if (!_classConstructors.ContainsKey(classStmt.Name.Lexeme))
        {
            var constructor = classStmt.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);
            var ctorParamTypes = constructor?.Parameters.Select(_ => typeof(object)).ToArray() ?? [];

            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                ctorParamTypes
            );

            _classConstructors[classStmt.Name.Lexeme] = ctorBuilder;
        }

        // Define instance methods (skip overload signatures with no body)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null))
        {
            if (method.IsStatic || method.Name.Lexeme == "constructor")
                continue;

            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
            if (method.IsAbstract)
            {
                methodAttrs |= MethodAttributes.Abstract;
            }

            Type returnType = method.IsAsync ? typeof(Task<object>) : typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                methodAttrs,
                returnType,
                paramTypes
            );

            // Track instance method for direct dispatch
            if (!_instanceMethods.TryGetValue(typeBuilder.Name, out var classMethods))
            {
                classMethods = [];
                _instanceMethods[typeBuilder.Name] = classMethods;
            }
            classMethods[method.Name.Lexeme] = methodBuilder;

            // Store the method builder for body emission later
            if (!_preDefinedMethods.TryGetValue(classStmt.Name.Lexeme, out var preDefined))
            {
                preDefined = [];
                _preDefinedMethods[classStmt.Name.Lexeme] = preDefined;
            }
            preDefined[method.Name.Lexeme] = methodBuilder;
        }

        // Define accessors
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                string methodName = accessor.Kind.Type == TokenType.GET
                    ? $"get_{accessor.Name.Lexeme}"
                    : $"set_{accessor.Name.Lexeme}";

                Type[] paramTypes = accessor.Kind.Type == TokenType.SET
                    ? [typeof(object)]
                    : [];

                MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
                if (accessor.IsAbstract)
                {
                    methodAttrs |= MethodAttributes.Abstract;
                }

                var methodBuilder = typeBuilder.DefineMethod(
                    methodName,
                    methodAttrs,
                    typeof(object),
                    paramTypes
                );

                // Track getter/setter
                string className = typeBuilder.Name;
                if (accessor.Kind.Type == TokenType.GET)
                {
                    if (!_instanceGetters.TryGetValue(className, out var classGetters))
                    {
                        classGetters = [];
                        _instanceGetters[className] = classGetters;
                    }
                    classGetters[accessor.Name.Lexeme] = methodBuilder;
                }
                else
                {
                    if (!_instanceSetters.TryGetValue(className, out var classSetters))
                    {
                        classSetters = [];
                        _instanceSetters[className] = classSetters;
                    }
                    classSetters[accessor.Name.Lexeme] = methodBuilder;
                }

                // Store for body emission
                if (!_preDefinedAccessors.TryGetValue(classStmt.Name.Lexeme, out var preDefinedAcc))
                {
                    preDefinedAcc = [];
                    _preDefinedAccessors[classStmt.Name.Lexeme] = preDefinedAcc;
                }
                preDefinedAcc[methodName] = methodBuilder;
            }
        }
    }

    private void EmitClassMethods(Stmt.Class classStmt)
    {
        var typeBuilder = _classBuilders[classStmt.Name.Lexeme];
        var fieldsField = _instanceFieldsField[classStmt.Name.Lexeme];

        // Initialize static methods dictionary for this class
        if (!_staticMethods.ContainsKey(classStmt.Name.Lexeme))
        {
            _staticMethods[classStmt.Name.Lexeme] = new Dictionary<string, MethodBuilder>();
        }

        // Define static methods first (so we can reference them in the static constructor)
        // Skip overload signatures (no body)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null))
        {
            if (method.IsStatic && method.Name.Lexeme != "constructor")
            {
                DefineStaticMethod(typeBuilder, classStmt.Name.Lexeme, method);
            }
        }

        // Emit static constructor for static property initializers
        EmitStaticConstructor(typeBuilder, classStmt);

        // Emit constructor
        EmitConstructor(typeBuilder, classStmt, fieldsField);

        // Emit method bodies (skip overload signatures with no body)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null))
        {
            if (method.Name.Lexeme != "constructor")
            {
                if (method.IsStatic)
                {
                    EmitStaticMethodBody(classStmt.Name.Lexeme, method);
                }
                else
                {
                    EmitMethod(typeBuilder, method, fieldsField);
                }
            }
        }

        // Emit accessor methods
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                EmitAccessor(typeBuilder, accessor, fieldsField);
            }
        }
    }

    private void EmitAccessor(TypeBuilder typeBuilder, Stmt.Accessor accessor, FieldInfo fieldsField)
    {
        // Use naming convention: get_<propertyName> or set_<propertyName>
        string methodName = accessor.Kind.Type == TokenType.GET
            ? $"get_{accessor.Name.Lexeme}"
            : $"set_{accessor.Name.Lexeme}";

        string className = typeBuilder.Name;
        MethodBuilder methodBuilder;

        // Check if accessor was pre-defined in DefineClassMethodsOnly
        if (_preDefinedAccessors.TryGetValue(className, out var preDefinedAcc) &&
            preDefinedAcc.TryGetValue(methodName, out var existingAccessor))
        {
            methodBuilder = existingAccessor;
        }
        else
        {
            // Define the accessor (fallback for when DefineClassMethodsOnly wasn't called)
            Type[] paramTypes = accessor.Kind.Type == TokenType.SET
                ? [typeof(object)]
                : [];

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
            if (accessor.IsAbstract)
            {
                methodAttrs |= MethodAttributes.Abstract;
            }

            methodBuilder = typeBuilder.DefineMethod(
                methodName,
                methodAttrs,
                typeof(object),
                paramTypes
            );

            // Track getter/setter for direct dispatch
            if (accessor.Kind.Type == TokenType.GET)
            {
                if (!_instanceGetters.TryGetValue(className, out var classGetters))
                {
                    classGetters = [];
                    _instanceGetters[className] = classGetters;
                }
                classGetters[accessor.Name.Lexeme] = methodBuilder;
            }
            else
            {
                if (!_instanceSetters.TryGetValue(className, out var classSetters))
                {
                    classSetters = [];
                    _instanceSetters[className] = classSetters;
                }
                classSetters[accessor.Name.Lexeme] = methodBuilder;
            }
        }

        // Abstract accessors have no body
        if (accessor.IsAbstract)
        {
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            FieldsField = fieldsField,
            IsInstanceMethod = true,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(typeBuilder.Name, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define setter parameter if applicable
        if (accessor.Kind.Type == TokenType.SET && accessor.SetterParam != null)
        {
            ctx.DefineParameter(accessor.SetterParam.Name.Lexeme, 1);
        }

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in accessor.Body)
        {
            emitter.EmitStatement(stmt);
        }

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Default return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    private void DefineStaticMethod(TypeBuilder typeBuilder, string className, Stmt.Function method)
    {
        var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
        var methodBuilder = typeBuilder.DefineMethod(
            method.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            paramTypes
        );

        _staticMethods[className][method.Name.Lexeme] = methodBuilder;
    }

    private void EmitStaticConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt)
    {
        // Only emit if there are static fields with initializers
        var staticFieldsWithInit = classStmt.Fields.Where(f => f.IsStatic && f.Initializer != null).ToList();
        if (staticFieldsWithInit.Count == 0) return;

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _staticFields,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };

        var emitter = new ILEmitter(ctx);

        var classStaticFields = _staticFields[classStmt.Name.Lexeme];
        foreach (var field in staticFieldsWithInit)
        {
            // Emit the initializer expression
            emitter.EmitExpression(field.Initializer!);
            emitter.EmitBoxIfNeeded(field.Initializer!);

            // Store in static field using the stored FieldBuilder
            var staticField = classStaticFields[field.Name.Lexeme];
            il.Emit(OpCodes.Stsfld, staticField);
        }

        il.Emit(OpCodes.Ret);
    }

    private void EmitStaticMethodBody(string className, Stmt.Function method)
    {
        var typeBuilder = _classBuilders[className];
        var methodBuilder = _staticMethods[className][method.Name.Lexeme];

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            IsInstanceMethod = false,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };

        // Define parameters (starting at index 0, not 1 since no 'this')
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i);
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks (static method)
        emitter.EmitDefaultParameters(method.Parameters, false);

        // Abstract methods have no body to emit
        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Default return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt, FieldInfo fieldsField)
    {
        // Find constructor implementation (with body), not overload signatures
        var constructor = classStmt.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);

        // Reuse pre-defined constructor if available (from DefineClassMethodsOnly)
        ConstructorBuilder ctorBuilder;
        if (_classConstructors.TryGetValue(classStmt.Name.Lexeme, out var existingCtor))
        {
            ctorBuilder = existingCtor;
        }
        else
        {
            var paramTypes = constructor?.Parameters.Select(_ => typeof(object)).ToArray() ?? [];
            ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                paramTypes
            );
            _classConstructors[classStmt.Name.Lexeme] = ctorBuilder;
        }

        var il = ctorBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            CurrentSuperclassName = classStmt.Superclass?.Lexeme,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(classStmt.Name.Lexeme, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Initialize _fields dictionary FIRST (before calling parent constructor)
        // This allows parent constructor to access fields via SetFieldsProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);
        il.Emit(OpCodes.Stfld, fieldsField);

        // Call parent constructor
        // If the class has an explicit constructor with super(), the super() in body will handle it.
        // If the class has no explicit constructor but has a superclass, we must call the parent constructor.
        // If the class has no superclass, we call Object constructor.
        if (constructor == null && classStmt.Superclass != null && _classConstructors.TryGetValue(classStmt.Superclass.Lexeme, out var parentCtor))
        {
            // No explicit constructor but has superclass - call parent's parameterless constructor
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, parentCtor);
        }
        else
        {
            // Has explicit constructor (which should have super() call) or no superclass
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor([])!);
        }

        // Emit instance field initializers (before constructor body)
        var instanceFieldsWithInit = classStmt.Fields.Where(f => !f.IsStatic && f.Initializer != null).ToList();
        if (instanceFieldsWithInit.Count > 0)
        {
            ctx.FieldsField = fieldsField;
            ctx.IsInstanceMethod = true;
            var initEmitter = new ILEmitter(ctx);

            foreach (var field in instanceFieldsWithInit)
            {
                // Load this._fields dictionary
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fieldsField);

                // Load field name
                il.Emit(OpCodes.Ldstr, field.Name.Lexeme);

                // Emit initializer expression
                initEmitter.EmitExpression(field.Initializer!);
                initEmitter.EmitBoxIfNeeded(field.Initializer!);

                // Store in dictionary
                il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
            }
        }

        // Emit constructor body
        if (constructor != null)
        {
            ctx.FieldsField = fieldsField;
            ctx.IsInstanceMethod = true;

            // Define parameters
            for (int i = 0; i < constructor.Parameters.Count; i++)
            {
                ctx.DefineParameter(constructor.Parameters[i].Name.Lexeme, i + 1);
            }

            var emitter = new ILEmitter(ctx);

            // Emit default parameter checks (instance method)
            emitter.EmitDefaultParameters(constructor.Parameters, true);

            if (constructor.Body != null)
            {
                foreach (var stmt in constructor.Body)
                {
                    emitter.EmitStatement(stmt);
                }
            }
        }

        il.Emit(OpCodes.Ret);
    }

    private void EmitMethod(TypeBuilder typeBuilder, Stmt.Function method, FieldInfo fieldsField)
    {
        MethodBuilder methodBuilder;

        // Check if method was pre-defined in DefineClassMethodsOnly
        if (_preDefinedMethods.TryGetValue(typeBuilder.Name, out var preDefined) &&
            preDefined.TryGetValue(method.Name.Lexeme, out var existingMethod))
        {
            methodBuilder = existingMethod;
        }
        else
        {
            // Define the method (fallback for when DefineClassMethodsOnly wasn't called)
            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
            if (method.IsAbstract)
            {
                methodAttrs |= MethodAttributes.Abstract;
            }

            Type returnType = method.IsAsync ? typeof(Task<object>) : typeof(object);

            methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                methodAttrs,
                returnType,
                paramTypes
            );

            // Track instance method for direct dispatch
            if (!_instanceMethods.TryGetValue(typeBuilder.Name, out var classMethods))
            {
                classMethods = [];
                _instanceMethods[typeBuilder.Name] = classMethods;
            }
            classMethods[method.Name.Lexeme] = methodBuilder;
        }

        // Abstract methods have no body
        if (method.IsAbstract)
        {
            return;
        }

        // Async methods use state machine generation
        if (method.IsAsync)
        {
            EmitAsyncMethodBody(methodBuilder, method, fieldsField);
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            FieldsField = fieldsField,
            IsInstanceMethod = true,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(typeBuilder.Name, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + 1);
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks (instance method)
        emitter.EmitDefaultParameters(method.Parameters, true);

        // Abstract methods have no body to emit
        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Default return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitAsyncMethodBody(MethodBuilder methodBuilder, Stmt.Function method, FieldInfo fieldsField)
    {
        // Analyze async function to determine await points and hoisted variables
        var analysis = _asyncAnalyzer.Analyze(method);

        // Build state machine type
        var smBuilder = new AsyncStateMachineBuilder(_moduleBuilder, _asyncStateMachineCounter++);
        var hasAsyncArrows = analysis.AsyncArrows.Count > 0;
        smBuilder.DefineStateMachine(
            $"{methodBuilder.DeclaringType!.Name}_{method.Name.Lexeme}",
            analysis,
            typeof(object),
            isInstanceMethod: true,  // This is an instance method
            hasAsyncArrows: hasAsyncArrows
        );

        // Build state machines for any async arrows found in this method
        DefineAsyncArrowStateMachines(analysis.AsyncArrows, smBuilder);

        // Emit stub method body (creates state machine and starts it)
        EmitAsyncStubMethod(methodBuilder, smBuilder, method.Parameters, isInstanceMethod: true);

        // Create context for MoveNext emission
        var il = smBuilder.MoveNextMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            FieldsField = fieldsField,
            IsInstanceMethod = true,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null,
            AsyncArrowBuilders = _asyncArrowBuilders,
            AsyncArrowOuterBuilders = _asyncArrowOuterBuilders,
            AsyncArrowParentBuilders = _asyncArrowParentBuilders
        };

        // Emit MoveNext body
        var moveNextEmitter = new AsyncMoveNextEmitter(smBuilder, analysis);
        moveNextEmitter.EmitMoveNext(method.Body, ctx, typeof(object));

        // Emit MoveNext bodies for async arrows
        foreach (var arrowInfo in analysis.AsyncArrows)
        {
            if (_asyncArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
            {
                var arrowAnalysis = AnalyzeAsyncArrow(arrowInfo.Arrow);
                var arrow = arrowInfo.Arrow;

                List<Stmt> bodyStatements;
                if (arrow.BlockBody != null)
                {
                    bodyStatements = arrow.BlockBody;
                }
                else if (arrow.ExpressionBody != null)
                {
                    var returnToken = new Token(TokenType.RETURN, "return", null, 0);
                    bodyStatements = [new Stmt.Return(returnToken, arrow.ExpressionBody)];
                }
                else
                {
                    bodyStatements = [];
                }

                var arrowEmitter = new AsyncArrowMoveNextEmitter(arrowBuilder,
                    new AsyncStateAnalyzer.AsyncFunctionAnalysis(
                        arrowAnalysis.AwaitCount,
                        [],  // AwaitPoints not needed for emission
                        arrowAnalysis.HoistedLocals,
                        [],  // HoistedParameters - arrow params are in ParameterFields
                        false, // HasTryCatch
                        false, // UsesThis
                        []     // AsyncArrows - handled separately via _asyncArrowBuilders
                    ));
                arrowEmitter.EmitMoveNext(bodyStatements, ctx, typeof(object));
            }
        }

        // Finalize async arrow state machine types
        foreach (var arrowInfo in analysis.AsyncArrows)
        {
            if (_asyncArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
            {
                arrowBuilder.CreateType();
            }
        }

        // Finalize state machine type
        smBuilder.CreateType();
    }

    private void EmitFunctionBody(Stmt.Function funcStmt)
    {
        // Skip async functions - they use native state machine emission
        if (funcStmt.IsAsync || _asyncStateMachines.ContainsKey(funcStmt.Name.Lexeme))
            return;

        var methodBuilder = _functionBuilders[funcStmt.Name.Lexeme];
        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };

        // Add generic type parameters to context if this is a generic function
        if (_functionGenericParams.TryGetValue(funcStmt.Name.Lexeme, out var genericParams))
        {
            foreach (var gp in genericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define parameters
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            ctx.DefineParameter(funcStmt.Parameters[i].Name.Lexeme, i);
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks (static function, not instance method)
        emitter.EmitDefaultParameters(funcStmt.Parameters, false);

        // Top-level functions should always have a body
        if (funcStmt.Body == null)
        {
            throw new InvalidOperationException($"Cannot compile function '{funcStmt.Name.Lexeme}' without a body.");
        }

        foreach (var stmt in funcStmt.Body)
        {
            emitter.EmitStatement(stmt);
        }

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Default return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    private MethodBuilder? _entryPoint;

    private void EmitEntryPoint(List<Stmt> statements)
    {
        var mainMethod = _programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );

        _entryPoint = mainMethod;

        var il = mainMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };
        var emitter = new ILEmitter(ctx);

        foreach (var stmt in statements)
        {
            // Skip class, function, interface, and enum declarations (already handled)
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.Enum)
            {
                continue;
            }
            emitter.EmitStatement(stmt);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Resolves a constraint type name to a .NET Type.
    /// </summary>
    private Type ResolveConstraintType(string constraint)
    {
        return constraint switch
        {
            "number" => typeof(double),
            "string" => typeof(string),
            "boolean" => typeof(bool),
            _ when _classBuilders.TryGetValue(constraint, out var tb) => tb,
            _ => typeof(object)
        };
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

        // Write the executable
        using FileStream fileStream = new(outputPath, FileMode.Create, FileAccess.Write);
        peBlob.WriteContentTo(fileStream);

    }
}
