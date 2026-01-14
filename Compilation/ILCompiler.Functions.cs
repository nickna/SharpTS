using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Top-level function definition and emission for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    private void DefineFunction(Stmt.Function funcStmt)
    {
        // Check if this is an async generator function - use combined state machine
        // Must check this FIRST since it has both IsAsync and IsGenerator true
        if (funcStmt.IsAsync && funcStmt.IsGenerator)
        {
            DefineAsyncGeneratorFunction(funcStmt);
            return;
        }

        // Check if this is an async function - use native IL state machine
        if (funcStmt.IsAsync)
        {
            DefineAsyncFunction(funcStmt);
            return;
        }

        // Check if this is a generator function - use generator state machine
        if (funcStmt.IsGenerator)
        {
            DefineGeneratorFunction(funcStmt);
            return;
        }

        var ctx = GetDefinitionContext();

        // Get qualified function name (module-prefixed in multi-module compilation)
        string qualifiedFunctionName = ctx.GetQualifiedFunctionName(funcStmt.Name.Lexeme);

        // Track simple name -> module mapping for later lookups
        if (_currentModulePath != null)
        {
            _functionToModule[funcStmt.Name.Lexeme] = _currentModulePath;
        }

        // Resolve typed parameters from TypeMap
        // Note: Return type remains 'object' for now to avoid breaking entry point code
        // that expects object on the stack for Task checking and logging.
        var funcType = _typeMap?.GetFunctionType(qualifiedFunctionName);
        var paramTypes = ParameterTypeResolver.ResolveParameters(
            funcStmt.Parameters, _typeMapper, funcType);

        var methodBuilder = _programType.DefineMethod(
            qualifiedFunctionName,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            paramTypes
        );

        // Handle generic type parameters
        bool isGeneric = funcStmt.TypeParams != null && funcStmt.TypeParams.Count > 0;
        _isGenericFunction[qualifiedFunctionName] = isGeneric;

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

            _functionGenericParams[qualifiedFunctionName] = genericParams;
        }

        _functionBuilders[qualifiedFunctionName] = methodBuilder;

        // Generate overloads for functions with default parameters
        var overloadSignatures = OverloadGenerator.GetOverloadSignatures(
            funcStmt.Parameters, paramTypes);
        if (overloadSignatures.Count > 0)
        {
            _functionOverloads[qualifiedFunctionName] = [];
            foreach (var overloadParams in overloadSignatures)
            {
                var overload = _programType.DefineMethod(
                    qualifiedFunctionName,
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(object),
                    overloadParams
                );
                _functionOverloads[qualifiedFunctionName].Add(overload);
            }
        }

        // Track rest parameter info
        var restParam = funcStmt.Parameters.FirstOrDefault(p => p.IsRest);
        if (restParam != null)
        {
            int restIndex = funcStmt.Parameters.IndexOf(restParam);
            int regularCount = funcStmt.Parameters.Count(p => !p.IsRest);
            _functionRestParams[qualifiedFunctionName] = (restIndex, regularCount);
        }
    }

    private void EmitFunctionBody(Stmt.Function funcStmt)
    {
        // Get qualified function name (must match what DefineFunction used)
        string qualifiedFunctionName = GetDefinitionContext().GetQualifiedFunctionName(funcStmt.Name.Lexeme);

        // Skip async functions - they use native state machine emission
        if (funcStmt.IsAsync || _asyncStateMachines.ContainsKey(qualifiedFunctionName))
            return;

        // Skip generator functions - they use generator state machine emission
        if (funcStmt.IsGenerator || _generatorStateMachines.ContainsKey(qualifiedFunctionName))
            return;

        var methodBuilder = _functionBuilders[qualifiedFunctionName];
        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders, _types)
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
            FunctionOverloads = _functionOverloads,
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
            TopLevelStaticVars = _topLevelStaticVars,
            // Module support for multi-module compilation
            CurrentModulePath = _currentModulePath,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule,
            DotNetNamespace = _currentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            ClassExprBuilders = _classExprBuilders,
            UnionGenerator = _unionGenerator
        };

        // Add generic type parameters to context if this is a generic function
        if (_functionGenericParams.TryGetValue(qualifiedFunctionName, out var genericParams))
        {
            foreach (var gp in genericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define parameters with their types
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            Type paramType = i < methodParams.Length ? methodParams[i].ParameterType : typeof(object);
            ctx.DefineParameter(funcStmt.Parameters[i].Name.Lexeme, i, paramType);
        }

        var emitter = new ILEmitter(ctx);

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
            // Default return null (return type is object for now)
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Finds a user-defined main() function with the expected signature.
    /// Returns the function and whether it's async, or null if no valid main exists.
    /// </summary>
    /// <remarks>
    /// Expected signature: function main(args: string[]): void
    /// Or async: async function main(args: string[]): Promise&lt;void&gt;
    /// </remarks>
    private (Stmt.Function Func, bool IsAsync)? FindMainFunction(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Function func && func.Name.Lexeme == "main" && func.Body != null)
            {
                // Validate signature: exactly one parameter (args: string[])
                if (func.Parameters.Count != 1)
                    continue;

                var param = func.Parameters[0];
                // Parameter should be named 'args' with type 'string[]'
                if (param.Type != "string[]")
                    continue;

                // Return type should be void (or null for implicit) for sync,
                // or Promise<void> for async
                if (func.IsAsync)
                {
                    if (func.ReturnType != null && func.ReturnType != "Promise<void>")
                        continue;
                }
                else
                {
                    if (func.ReturnType != null && func.ReturnType != "void")
                        continue;
                }

                return (func, func.IsAsync);
            }
        }
        return null;
    }

    private void EmitEntryPoint(List<Stmt> statements)
    {
        // For EXE target, check if user defined a main() function
        if (_outputTarget == OutputTarget.Exe)
        {
            var mainFunc = FindMainFunction(statements);
            if (mainFunc != null)
            {
                EmitExeEntryPointWithUserMain(statements, mainFunc.Value.Func, mainFunc.Value.IsAsync);
                return;
            }
        }

        // Default behavior: synthetic Main with top-level statements
        EmitDefaultEntryPoint(statements);
    }

    /// <summary>
    /// Emits the default entry point where top-level statements run as the program.
    /// Used for DLL target or EXE without user-defined main().
    /// </summary>
    private void EmitDefaultEntryPoint(List<Stmt> statements)
    {
        var mainMethod = _programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );

        _entryPoint = mainMethod;

        var il = mainMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders, _types)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            ClassExprBuilders = _classExprBuilders,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            FunctionOverloads = _functionOverloads,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            NamespaceFields = _namespaceFields,
            TopLevelStaticVars = _topLevelStaticVars,
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
            DotNetNamespace = _currentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            // Class expression support
            VarToClassExpr = _varToClassExpr,
            ClassExprStaticFields = _classExprStaticFields,
            ClassExprStaticMethods = _classExprStaticMethods,
            ClassExprConstructors = _classExprConstructors,
            ClassExprSuperclass = _classExprSuperclass,
            UnionGenerator = _unionGenerator
        };

        // Initialize namespace static fields before any code that might reference them
        InitializeNamespaceFields(il);

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in statements)
        {
            // Skip class, function, interface, and enum declarations (already handled)
            // Note: Namespace statements are NOT skipped - they need to emit member storage
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.Enum)
            {
                continue;
            }

            // Special handling for expression statements to wait for top-level async calls
            if (stmt is Stmt.Expression exprStmt)
            {
                emitter.EmitExpression(exprStmt.Expr);

                // Check if the result is a Task<object> and wait for it
                // This provides "top-level await" behavior for compiled code
                var notTaskLabel = il.DefineLabel();
                var doneLabel = il.DefineLabel();

                il.Emit(OpCodes.Dup);  // Keep copy for Task check
                il.Emit(OpCodes.Isinst, _types.TaskOfObject);
                il.Emit(OpCodes.Brfalse, notTaskLabel);

                // It's a Task<object> - wait for it
                il.Emit(OpCodes.Castclass, _types.TaskOfObject);
                var getAwaiter = _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
                il.Emit(OpCodes.Call, getAwaiter);
                var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
                il.Emit(OpCodes.Stloc, awaiterLocal);
                il.Emit(OpCodes.Ldloca, awaiterLocal);
                var getResult = _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult");
                il.Emit(OpCodes.Call, getResult);
                il.Emit(OpCodes.Pop);  // Discard the result
                il.Emit(OpCodes.Br, doneLabel);

                il.MarkLabel(notTaskLabel);
                il.Emit(OpCodes.Pop);  // Not a Task, just pop the original value

                il.MarkLabel(doneLabel);
            }
            else
            {
                emitter.EmitStatement(stmt);
            }
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits an entry point that calls the user's main(args) function.
    /// Used for EXE target when a valid main() function is defined.
    /// </summary>
    private void EmitExeEntryPointWithUserMain(List<Stmt> statements, Stmt.Function mainFunc, bool isAsync)
    {
        // PE entry point must return void (or int for exit code)
        // For async main, we create a void Main that awaits the async main
        var mainMethod = _programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(string[])]  // Accept string[] args from .NET runtime
        );

        _entryPoint = mainMethod;

        var il = mainMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders, _types)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            ClassExprBuilders = _classExprBuilders,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            FunctionOverloads = _functionOverloads,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            NamespaceFields = _namespaceFields,
            TopLevelStaticVars = _topLevelStaticVars,
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
            DotNetNamespace = _currentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            VarToClassExpr = _varToClassExpr,
            ClassExprStaticFields = _classExprStaticFields,
            ClassExprStaticMethods = _classExprStaticMethods,
            ClassExprConstructors = _classExprConstructors,
            ClassExprSuperclass = _classExprSuperclass,
            UnionGenerator = _unionGenerator
        };

        // Initialize namespace static fields before any code
        InitializeNamespaceFields(il);

        var emitter = new ILEmitter(ctx);

        // Execute top-level statements (module initialization), excluding the main function
        foreach (var stmt in statements)
        {
            // Skip declarations (handled in earlier phases), including main()
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.Enum)
            {
                continue;
            }

            // Run top-level code (imports, variable initialization, etc.)
            if (stmt is Stmt.Expression exprStmt)
            {
                emitter.EmitExpression(exprStmt.Expr);

                // Check for async calls and wait for them
                var notTaskLabel = il.DefineLabel();
                var doneLabel = il.DefineLabel();

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Isinst, _types.TaskOfObject);
                il.Emit(OpCodes.Brfalse, notTaskLabel);

                il.Emit(OpCodes.Castclass, _types.TaskOfObject);
                var getAwaiter = _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
                il.Emit(OpCodes.Call, getAwaiter);
                var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
                il.Emit(OpCodes.Stloc, awaiterLocal);
                il.Emit(OpCodes.Ldloca, awaiterLocal);
                var getResult = _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult");
                il.Emit(OpCodes.Call, getResult);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Br, doneLabel);

                il.MarkLabel(notTaskLabel);
                il.Emit(OpCodes.Pop);

                il.MarkLabel(doneLabel);
            }
            else
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Now call the user's main(args) function
        // Load the args parameter (arg 0) - string[] is a reference type, no boxing needed
        il.Emit(OpCodes.Ldarg_0);  // Load string[] args (reference types implicitly convert to object)

        // Call the user's main function
        var userMainMethod = _functionBuilders[mainFunc.Name.Lexeme];
        il.Emit(OpCodes.Call, userMainMethod);

        if (isAsync)
        {
            // Async main returns Task<object> - we need to await it synchronously
            // Call GetAwaiter().GetResult() to block until completion
            il.Emit(OpCodes.Castclass, _types.TaskOfObject);
            var getAwaiter = _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
            il.Emit(OpCodes.Call, getAwaiter);
            var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
            il.Emit(OpCodes.Stloc, awaiterLocal);
            il.Emit(OpCodes.Ldloca, awaiterLocal);
            var getResult = _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult");
            il.Emit(OpCodes.Call, getResult);
            il.Emit(OpCodes.Pop);  // Discard the result
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // Sync main returns object, but we expect void behavior - just pop and return
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }
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

    /// <summary>
    /// Emits forwarding bodies for function overloads.
    /// Must be called after EmitFunctionBody so the full method is available.
    /// </summary>
    private void EmitFunctionOverloads(Stmt.Function funcStmt)
    {
        string qualifiedFunctionName = GetDefinitionContext().GetQualifiedFunctionName(funcStmt.Name.Lexeme);

        // Skip if no overloads were generated
        if (!_functionOverloads.TryGetValue(qualifiedFunctionName, out var overloads) || overloads.Count == 0)
            return;

        var fullMethod = _functionBuilders[qualifiedFunctionName];

        // For each overload, emit a forwarding body that calls the full method
        int overloadIndex = 0;
        for (int arity = funcStmt.Parameters.Count - 1; arity >= GetFirstDefaultIndex(funcStmt.Parameters); arity--)
        {
            var overload = overloads[overloadIndex++];
            var il = overload.GetILGenerator();

            // Create a minimal context just for emitting default value expressions
            var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders, _types)
            {
                Runtime = _runtime,
                TypeMap = _typeMap
            };
            var emitter = new ILEmitter(ctx);

            OverloadGenerator.EmitOverloadBody(
                il,
                fullMethod,
                funcStmt.Parameters,
                arity,
                isStatic: true,
                emitter
            );
        }
    }

    /// <summary>
    /// Gets the index of the first parameter with a default value.
    /// Returns -1 if no default parameters exist.
    /// </summary>
    private static int GetFirstDefaultIndex(List<Stmt.Parameter> parameters)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].DefaultValue != null)
                return i;
        }
        return -1;
    }
}
