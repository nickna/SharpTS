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
}
