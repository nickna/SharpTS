using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

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

        var paramTypes = funcStmt.Parameters.Select(_ => typeof(object)).ToArray();
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
            // Module support for multi-module compilation
            CurrentModulePath = _currentModulePath,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule,
            DotNetNamespace = _currentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry
        };

        // Add generic type parameters to context if this is a generic function
        if (_functionGenericParams.TryGetValue(qualifiedFunctionName, out var genericParams))
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
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            NamespaceFields = _namespaceFields,
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
            TypeEmitterRegistry = _typeEmitterRegistry
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
}
