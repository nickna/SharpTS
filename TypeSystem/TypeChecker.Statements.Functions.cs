using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Function declaration type checking - handles function statements including overloads and generics.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Hoists function declarations by registering their types before checking bodies.
    /// This enables functions to reference each other regardless of declaration order.
    /// </summary>
    private void HoistFunctionDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            // Handle top-level functions
            if (stmt is Stmt.Function funcStmt && funcStmt.Body != null)
            {
                HoistSingleFunction(funcStmt);
            }
            // Handle exported functions
            else if (stmt is Stmt.Export export && export.Declaration is Stmt.Function exportedFunc && exportedFunc.Body != null)
            {
                HoistSingleFunction(exportedFunc);
            }
        }

        // Hoist const/let/var declarations with function expressions
        // This enables mutual recursion like: const isEven = function(n) { return isOdd(n-1); };
        HoistConstFunctionExpressions(statements);
    }

    /// <summary>
    /// Hoists const/var declarations with function expression initializers.
    /// This enables mutual recursion between function expressions declared with const/var.
    /// Only registers the function type, does not check the function body.
    /// </summary>
    private void HoistConstFunctionExpressions(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case Stmt.Const constStmt when constStmt.Initializer is Expr.ArrowFunction arrow:
                    HoistConstFunctionExpression(constStmt.Name, constStmt.TypeAnnotation, arrow);
                    break;

                case Stmt.Var varStmt when varStmt.Initializer is Expr.ArrowFunction arrow:
                    HoistConstFunctionExpression(varStmt.Name, varStmt.TypeAnnotation, arrow);
                    break;

                case Stmt.Export export when export.Declaration is Stmt.Const exportedConst
                    && exportedConst.Initializer is Expr.ArrowFunction arrow:
                    HoistConstFunctionExpression(exportedConst.Name, exportedConst.TypeAnnotation, arrow);
                    break;
            }
        }
    }

    /// <summary>
    /// Hoists a single const function expression by registering its type without checking the body.
    /// </summary>
    private void HoistConstFunctionExpression(Token name, string? typeAnnotation, Expr.ArrowFunction arrow)
    {
        // Skip if already defined
        if (_environment.IsDefinedLocally(name.Lexeme)) return;

        try
        {
            // Build parameter types from the arrow function's parameter declarations
            var paramTypes = new List<TypeInfo>();
            int requiredParams = 0;
            bool hasRest = false;

            foreach (var param in arrow.Parameters)
            {
                TypeInfo paramType = param.Type != null
                    ? ToTypeInfo(param.Type)
                    : new TypeInfo.Any();

                if (param.IsRest)
                {
                    hasRest = true;
                    paramTypes.Add(new TypeInfo.Array(paramType));
                }
                else
                {
                    paramTypes.Add(paramType);
                    if (param.DefaultValue == null && !param.IsOptional)
                        requiredParams++;
                }
            }

            // Determine return type from declaration or annotation
            TypeInfo returnType;
            if (typeAnnotation != null)
            {
                // Use the declared type annotation on the const
                var declaredType = ToTypeInfo(typeAnnotation);
                if (declaredType is TypeInfo.Function funcType)
                {
                    returnType = funcType.ReturnType;
                }
                else
                {
                    returnType = new TypeInfo.Any();
                }
            }
            else if (arrow.ReturnType != null)
            {
                // Use the return type from the arrow function
                returnType = ToTypeInfo(arrow.ReturnType);
            }
            else
            {
                // No return type specified - use Any for hoisting
                // The actual type will be checked during normal processing
                returnType = new TypeInfo.Any();
            }

            // Handle 'this' type
            TypeInfo? thisType = arrow.ThisType != null ? ToTypeInfo(arrow.ThisType) : null;
            if (arrow.HasOwnThis && thisType == null)
            {
                thisType = new TypeInfo.Any();
            }

            // Build and register the function type
            var funcType2 = new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, thisType);
            _environment.Define(name.Lexeme, funcType2);
        }
        catch
        {
            // If type resolution fails, skip hoisting - will be defined during normal processing
        }
    }

    /// <summary>
    /// Hoists a single function declaration by registering its type without checking the body.
    /// If type resolution fails (e.g., references undefined types), the function will be
    /// defined later during normal statement processing.
    /// </summary>
    private void HoistSingleFunction(Stmt.Function funcStmt)
    {
        string funcName = funcStmt.Name.Lexeme;

        // Skip if already defined (e.g., from overload processing)
        // But allow overwriting built-in Any types (like process, console) with user definitions
        if (_environment.IsDefinedLocally(funcName))
        {
            var existingType = _environment.Get(funcName);
            if (existingType is not TypeInfo.Any)
                return;
        }

        try
        {
            // Build the function type
            TypeEnvironment funcEnv = new(_environment);

            // Set up environment for parsing type parameters and constraints
            TypeEnvironment previousEnvForParsing = _environment;
            _environment = funcEnv;

            // Handle generic type parameters
            // First pass: define all type parameters so they can reference each other
            List<TypeInfo.TypeParameter>? typeParams = null;
            if (funcStmt.TypeParams != null && funcStmt.TypeParams.Count > 0)
            {
                typeParams = [];
                // First, define all type parameters without constraints
                foreach (var tp in funcStmt.TypeParams)
                {
                    var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, null, null);
                    funcEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                }
                // Second, parse constraints (which may reference other type parameters)
                foreach (var tp in funcStmt.TypeParams)
                {
                    TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                    TypeInfo? defaultType = tp.Default != null ? ToTypeInfo(tp.Default) : null;
                    var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType);
                    typeParams.Add(typeParam);
                    // Redefine with the actual constraint
                    funcEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                }
            }

            // Parse parameter types and return type (environment already set above)

            try
            {
                var (paramTypes, requiredParams, hasRest, paramNames) = BuildFunctionSignature(
                    funcStmt.Parameters,
                    validateDefaults: false, // Don't validate defaults during hoisting
                    contextName: $"function '{funcStmt.Name.Lexeme}'"
                );

                TypeInfo returnType = funcStmt.ReturnType != null
                    ? ToTypeInfo(funcStmt.ReturnType)
                    : new TypeInfo.Void();

                TypeInfo? thisType = funcStmt.ThisType != null ? ToTypeInfo(funcStmt.ThisType) : null;

                // Restore environment before defining function type
                _environment = previousEnvForParsing;

                // Handle generator return types
                TypeInfo funcReturnType = returnType;
                if (funcStmt.IsGenerator)
                {
                    if (funcStmt.IsAsync && returnType is not TypeInfo.AsyncGenerator)
                    {
                        funcReturnType = new TypeInfo.AsyncGenerator(returnType);
                    }
                    else if (!funcStmt.IsAsync && returnType is not TypeInfo.Generator)
                    {
                        funcReturnType = new TypeInfo.Generator(returnType);
                    }
                }

                // Build the appropriate function type
                TypeInfo funcType;
                if (typeParams != null && typeParams.Count > 0)
                {
                    funcType = new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType, paramNames);
                }
                else
                {
                    funcType = new TypeInfo.Function(paramTypes, funcReturnType, requiredParams, hasRest, thisType, paramNames);
                }

                // Register the function type (hoisting)
                _environment.Define(funcName, funcType);
            }
            catch
            {
                // Restore environment on failure
                _environment = previousEnvForParsing;
                throw;
            }
        }
        catch
        {
            // If type resolution fails during hoisting (e.g., references undefined interface),
            // skip hoisting - the function will be defined normally during statement processing
        }
    }

    /// <summary>
    /// Handle function declarations including overloaded functions.
    /// </summary>
    private void CheckFunctionDeclaration(Stmt.Function funcStmt)
    {
        string funcName = funcStmt.Name.Lexeme;

        // Build the function type for this declaration
        TypeEnvironment funcEnv = new(_environment);

        // Set up environment for parsing type parameters and constraints
        TypeEnvironment previousEnvForParsing = _environment;
        _environment = funcEnv;

        // Handle generic type parameters
        // First pass: define all type parameters so they can reference each other
        List<TypeInfo.TypeParameter>? typeParams = null;
        if (funcStmt.TypeParams != null && funcStmt.TypeParams.Count > 0)
        {
            typeParams = [];
            // First, define all type parameters without constraints
            foreach (var tp in funcStmt.TypeParams)
            {
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, null, null);
                funcEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }
            // Second, parse constraints (which may reference other type parameters)
            foreach (var tp in funcStmt.TypeParams)
            {
                TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                TypeInfo? defaultType = tp.Default != null ? ToTypeInfo(tp.Default) : null;
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType);
                typeParams.Add(typeParam);
                // Redefine with the actual constraint
                funcEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }
        }

        // Parse parameter types and return type (environment already set above)

        var (paramTypes, requiredParams, hasRest, paramNames) = BuildFunctionSignature(
            funcStmt.Parameters,
            validateDefaults: true,
            contextName: $"function '{funcStmt.Name.Lexeme}'"
        );

        TypeInfo returnType = funcStmt.ReturnType != null
            ? ToTypeInfo(funcStmt.ReturnType)
            : new TypeInfo.Void();

        // Validate type predicate return types
        ValidateTypePredicateReturnType(returnType, funcStmt.Parameters, funcStmt.Name.Lexeme);

        // Parse explicit 'this' type if present
        TypeInfo? thisType = funcStmt.ThisType != null ? ToTypeInfo(funcStmt.ThisType) : null;

        _environment = previousEnvForParsing;

        // For generator functions, wrap the return type in Generator<> or AsyncGenerator<> if not already
        TypeInfo funcReturnType = returnType;
        if (funcStmt.IsGenerator)
        {
            if (funcStmt.IsAsync && returnType is not TypeInfo.AsyncGenerator)
            {
                funcReturnType = new TypeInfo.AsyncGenerator(returnType);
            }
            else if (!funcStmt.IsAsync && returnType is not TypeInfo.Generator)
            {
                funcReturnType = new TypeInfo.Generator(returnType);
            }
        }

        var thisFuncType = new TypeInfo.Function(paramTypes, funcReturnType, requiredParams, hasRest, thisType, paramNames);

        // Check if this is an overload signature (no body)
        if (funcStmt.Body == null)
        {
            // This is an overload signature - save for later
            if (!_pendingOverloadSignatures.TryGetValue(funcName, out var signatures))
            {
                signatures = [];
                _pendingOverloadSignatures[funcName] = signatures;
            }
            signatures.Add(thisFuncType);

            // Also save type parameters if this is a generic overload
            if (typeParams != null && typeParams.Count > 0)
            {
                _pendingOverloadTypeParams[funcName] = typeParams;
            }
            return;
        }

        // This is an implementation (has a body)
        TypeInfo funcType;

        // Check if there are pending overload signatures for this function
        if (_pendingOverloadSignatures.TryGetValue(funcName, out var pendingSignatures))
        {
            // Validate implementation is compatible with all signatures
            foreach (var sig in pendingSignatures)
            {
                if (thisFuncType.MinArity > sig.MinArity)
                {
                    throw new TypeCheckException($" Implementation of '{funcName}' requires {thisFuncType.MinArity} arguments but overload signature requires only {sig.MinArity}.");
                }
            }

            // Check if we have type parameters (generic overloaded function)
            if (_pendingOverloadTypeParams.TryGetValue(funcName, out var overloadTypeParams))
            {
                // Create generic overloaded function type
                funcType = new TypeInfo.GenericOverloadedFunction(overloadTypeParams, pendingSignatures, thisFuncType);
                _pendingOverloadTypeParams.Remove(funcName);
            }
            else
            {
                // Create non-generic overloaded function type
                funcType = new TypeInfo.OverloadedFunction(pendingSignatures, thisFuncType);
            }

            // Clear pending signatures
            _pendingOverloadSignatures.Remove(funcName);
        }
        else if (typeParams != null && typeParams.Count > 0)
        {
            // Generic function (no overloads)
            funcType = new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType, paramNames);
        }
        else
        {
            // Regular function (no overloads)
            funcType = thisFuncType;
        }

        // Define or update the function type (may have been hoisted earlier)
        // For overloaded functions, we need to update with the complete type
        if (!_environment.IsDefinedLocally(funcName) || funcType is TypeInfo.OverloadedFunction or TypeInfo.GenericOverloadedFunction)
        {
            _environment.Define(funcName, funcType);
        }

        // Register function type for typed compilation
        // For overloaded functions, use the implementation type
        if (funcType is TypeInfo.Function ft)
        {
            _typeMap.SetFunctionType(funcName, ft);
        }
        else if (funcType is TypeInfo.OverloadedFunction of)
        {
            _typeMap.SetFunctionType(funcName, of.Implementation);
        }
        else if (funcType is TypeInfo.GenericOverloadedFunction gof)
        {
            // For generic overloaded functions, use the implementation type
            _typeMap.SetFunctionType(funcName, gof.Implementation);
        }
        else if (funcType is TypeInfo.GenericFunction gf)
        {
            // For generic functions, store a function type with the unsubstituted types
            _typeMap.SetFunctionType(funcName, new TypeInfo.Function(gf.ParamTypes, gf.ReturnType, gf.RequiredParams, gf.HasRestParam, gf.ThisType));
        }

        // Add parameters to function environment and check body
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            funcEnv.Define(funcStmt.Parameters[i].Name.Lexeme, paramTypes[i]);
        }

        // Save and set context - function bodies are isolated from outer loop/switch/label context
        TypeEnvironment previousEnv = _environment;
        TypeInfo? previousReturn = _currentFunctionReturnType;
        TypeInfo? previousThisType = _currentFunctionThisType;
        bool previousInAsync = _inAsyncFunction;
        bool previousInGenerator = _inGeneratorFunction;
        int previousLoopDepth = _loopDepth;
        int previousSwitchDepth = _switchDepth;
        var previousActiveLabels = new Dictionary<string, bool>(_activeLabels);

        _environment = funcEnv;
        _currentFunctionReturnType = returnType;
        _currentFunctionThisType = thisType;
        _inAsyncFunction = funcStmt.IsAsync;
        _inGeneratorFunction = funcStmt.IsGenerator;
        _loopDepth = 0;
        _switchDepth = 0;
        _activeLabels.Clear();

        try
        {
            foreach (var bodyStmt in funcStmt.Body)
            {
                CheckStmt(bodyStmt);
            }

            // Validate that non-void functions return a value on all code paths
            // Skip for void, generators (which use yield), async functions (which return Promise),
            // and assertion predicates (which either throw or complete normally)
            if (returnType is not TypeInfo.Void &&
                returnType is not TypeInfo.Generator &&
                returnType is not TypeInfo.AsyncGenerator &&
                returnType is not TypeInfo.TypePredicate { IsAssertion: true } &&
                returnType is not TypeInfo.AssertsNonNull &&
                !funcStmt.IsGenerator &&
                !funcStmt.IsAsync)
            {
                if (!DoesBlockDefinitelyReturn(funcStmt.Body))
                {
                    throw new TypeCheckException($" Function '{funcStmt.Name.Lexeme}' must return a value of type '{returnType}'.");
                }
            }
        }
        finally
        {
            _environment = previousEnv;
            _currentFunctionReturnType = previousReturn;
            _currentFunctionThisType = previousThisType;
            _inAsyncFunction = previousInAsync;
            _inGeneratorFunction = previousInGenerator;
            _loopDepth = previousLoopDepth;
            _switchDepth = previousSwitchDepth;
            _activeLabels.Clear();
            foreach (var kvp in previousActiveLabels)
                _activeLabels[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Validates that type predicate return types reference valid parameter names.
    /// </summary>
    private void ValidateTypePredicateReturnType(TypeInfo returnType, List<Stmt.Parameter> parameters, string funcName)
    {
        string? paramToCheck = null;

        if (returnType is TypeInfo.TypePredicate pred)
        {
            paramToCheck = pred.ParameterName;
        }
        else if (returnType is TypeInfo.AssertsNonNull assertsNonNull)
        {
            paramToCheck = assertsNonNull.ParameterName;
        }

        if (paramToCheck != null)
        {
            // Check if the parameter exists in the function signature
            bool paramExists = parameters.Any(p => p.Name.Lexeme == paramToCheck);

            // Also allow 'this' as a valid target for type predicates
            if (!paramExists && paramToCheck != "this")
            {
                throw new TypeCheckException(
                    $" Type predicate in function '{funcName}' references parameter '{paramToCheck}' which is not in the function signature.");
            }
        }
    }
}
