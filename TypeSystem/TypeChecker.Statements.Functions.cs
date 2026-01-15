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

            // Handle generic type parameters
            List<TypeInfo.TypeParameter>? typeParams = null;
            if (funcStmt.TypeParams != null && funcStmt.TypeParams.Count > 0)
            {
                typeParams = [];
                foreach (var tp in funcStmt.TypeParams)
                {
                    TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                    var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint);
                    typeParams.Add(typeParam);
                    funcEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                }
            }

            // Parse parameter types and return type
            TypeEnvironment previousEnvForParsing = _environment;
            _environment = funcEnv;

            try
            {
                var (paramTypes, requiredParams, hasRest) = BuildFunctionSignature(
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
                    funcType = new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType);
                }
                else
                {
                    funcType = new TypeInfo.Function(paramTypes, funcReturnType, requiredParams, hasRest, thisType);
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

        // Handle generic type parameters
        List<TypeInfo.TypeParameter>? typeParams = null;
        if (funcStmt.TypeParams != null && funcStmt.TypeParams.Count > 0)
        {
            typeParams = [];
            foreach (var tp in funcStmt.TypeParams)
            {
                TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint);
                typeParams.Add(typeParam);
                funcEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }
        }

        // Parse parameter types and return type
        TypeEnvironment previousEnvForParsing = _environment;
        _environment = funcEnv;

        var (paramTypes, requiredParams, hasRest) = BuildFunctionSignature(
            funcStmt.Parameters,
            validateDefaults: true,
            contextName: $"function '{funcStmt.Name.Lexeme}'"
        );

        TypeInfo returnType = funcStmt.ReturnType != null
            ? ToTypeInfo(funcStmt.ReturnType)
            : new TypeInfo.Void();

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

        var thisFuncType = new TypeInfo.Function(paramTypes, funcReturnType, requiredParams, hasRest, thisType);

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

            // Create overloaded function type
            funcType = new TypeInfo.OverloadedFunction(pendingSignatures, thisFuncType);

            // Clear pending signatures
            _pendingOverloadSignatures.Remove(funcName);
        }
        else if (typeParams != null && typeParams.Count > 0)
        {
            // Generic function (no overloads)
            funcType = new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType);
        }
        else
        {
            // Regular function (no overloads)
            funcType = thisFuncType;
        }

        // Define or update the function type (may have been hoisted earlier)
        // For overloaded functions, we need to update with the complete type
        if (!_environment.IsDefinedLocally(funcName) || funcType is TypeInfo.OverloadedFunction)
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
}
