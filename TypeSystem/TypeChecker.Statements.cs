using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Statement type checking - CheckStmt and the main dispatch switch.
/// </summary>
/// <remarks>
/// Contains the main statement dispatch (CheckStmt) and inline handling for simple statements.
/// Complex statement handlers are split into separate partial files:
/// <list type="bullet">
///   <item><description><c>TypeChecker.Statements.Classes.cs</c> - Class declaration checking</description></item>
///   <item><description><c>TypeChecker.Statements.Interfaces.cs</c> - Interface declaration checking</description></item>
///   <item><description><c>TypeChecker.Statements.Functions.cs</c> - Function declaration and overload handling</description></item>
///   <item><description><c>TypeChecker.Statements.Enums.cs</c> - Enum declaration with const enum support</description></item>
///   <item><description><c>TypeChecker.Statements.ControlFlow.cs</c> - Block, switch, try/catch checking</description></item>
///   <item><description><c>TypeChecker.Statements.Modules.cs</c> - Export statement checking</description></item>
/// </list>
/// </remarks>
public partial class TypeChecker
{
    private void CheckStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                CheckBlock(block.Statements, new TypeEnvironment(_environment));
                break;
            case Stmt.Sequence seq:
                // Execute in current scope (no new environment)
                foreach (var s in seq.Statements)
                    CheckStmt(s);
                break;

            case Stmt.LabeledStatement labeledStmt:
                {
                    string labelName = labeledStmt.Label.Lexeme;

                    // Check for label shadowing
                    if (_activeLabels.ContainsKey(labelName))
                    {
                        throw new TypeCheckException($"Label '{labelName}' already declared in this scope");
                    }

                    // Determine if this label is on a loop (for continue validation)
                    bool isOnLoop = labeledStmt.Statement is Stmt.While
                                 or Stmt.For
                                 or Stmt.DoWhile
                                 or Stmt.ForOf
                                 or Stmt.ForIn
                                 or Stmt.LabeledStatement; // Allow chained labels

                    // If chained label, inherit loop status from inner
                    if (labeledStmt.Statement is Stmt.LabeledStatement innerLabeled)
                    {
                        // We need to peek ahead - for now, mark as potentially a loop
                        // The inner labeled statement will be checked recursively
                        isOnLoop = true;
                    }

                    _activeLabels[labelName] = isOnLoop;
                    try
                    {
                        CheckStmt(labeledStmt.Statement);
                    }
                    finally
                    {
                        _activeLabels.Remove(labelName);
                    }
                }
                break;

            case Stmt.Interface interfaceStmt:
                CheckInterfaceDeclaration(interfaceStmt);
                break;

            case Stmt.TypeAlias typeAlias:
                if (typeAlias.TypeParameters != null && typeAlias.TypeParameters.Count > 0)
                {
                    // Generic type alias: type Foo<T, U> = ...
                    var typeParamNames = typeAlias.TypeParameters.Select(tp => tp.Name.Lexeme).ToList();

                    // Validate spread constraints: any ...T in the definition must have T constrained to array type
                    ValidateTypeAliasSpreadConstraints(typeAlias);

                    _environment.DefineGenericTypeAlias(typeAlias.Name.Lexeme, typeAlias.TypeDefinition, typeParamNames);
                }
                else
                {
                    // Simple type alias: type Foo = ...
                    _environment.DefineTypeAlias(typeAlias.Name.Lexeme, typeAlias.TypeDefinition);
                }
                break;

            case Stmt.Enum enumStmt:
                CheckEnumDeclaration(enumStmt);
                break;

            case Stmt.Namespace ns:
                CheckNamespace(ns);
                break;

            case Stmt.ImportAlias importAlias:
                CheckImportAlias(importAlias);
                break;

            case Stmt.Class classStmt:
                CheckClassDeclaration(classStmt);
                break;

            case Stmt.Var varStmt:
                TypeInfo? declaredType = null;
                if (varStmt.TypeAnnotation != null)
                {
                    declaredType = ToTypeInfo(varStmt.TypeAnnotation);
                }

                // Definite assignment assertion: let x!: number;
                // The parser ensures ! always has a type annotation and no initializer.
                // Use the declared type - the assertion means "trust me, it will be assigned".
                if (varStmt.HasDefiniteAssignmentAssertion)
                {
                    // declaredType is already set from the type annotation (parser guarantees this)
                    _environment.Define(varStmt.Name.Lexeme, declaredType!);
                    break;
                }

                if (varStmt.Initializer != null)
                {
                    // Special case: array literal assigned to tuple type (contextual typing)
                    if (declaredType is TypeInfo.Tuple tupleType && varStmt.Initializer is Expr.ArrayLiteral arrayLit)
                    {
                        CheckArrayLiteralAgainstTuple(arrayLit, tupleType, varStmt.Name.Lexeme);
                    }
                    else
                    {
                        // Mark object literals as fresh if directly assigned with type annotation
                        Expr initializer = varStmt.Initializer;
                        bool checkExcessProps = false;

                        if (declaredType != null && initializer is Expr.ObjectLiteral objLit)
                        {
                            initializer = objLit with { IsFresh = true };
                            checkExcessProps = true;
                        }

                        // Pass contextual type to arrow functions for parameter type inference
                        TypeInfo initializerType;
                        if (declaredType != null && initializer is Expr.ArrowFunction arrowFn)
                        {
                            initializerType = CheckArrowFunction(arrowFn, declaredType);
                        }
                        else
                        {
                            initializerType = CheckExpr(initializer);
                        }

                        if (declaredType != null)
                        {
                            // Perform excess property check for fresh object literals
                            if (checkExcessProps && initializerType is TypeInfo.Record actualRecord)
                            {
                                CheckExcessProperties(actualRecord, declaredType, varStmt.Initializer);
                            }

                            if (!IsCompatible(declaredType, initializerType))
                            {
                                throw new TypeMismatchException(declaredType, initializerType, varStmt.Name.Line);
                            }
                        }
                        else
                        {
                            // No type annotation - widen literal types for inference
                            initializerType = WidenLiteralType(initializerType);
                        }

                        declaredType ??= initializerType;
                    }
                }

                declaredType ??= new TypeInfo.Any();
                _environment.Define(varStmt.Name.Lexeme, declaredType);
                break;

            case Stmt.Const constStmt:
            {
                TypeInfo constDeclaredType;

                if (constStmt.TypeAnnotation == "unique symbol")
                {
                    // Validate initializer is Symbol() call
                    if (constStmt.Initializer is not Expr.Call call ||
                        call.Callee is not Expr.Variable v ||
                        v.Name.Lexeme != "Symbol")
                    {
                        throw new TypeCheckException(
                            $"'unique symbol' must be initialized with Symbol() at line {constStmt.Name.Line}.");
                    }
                    // Validate Symbol() argument if present
                    if (call.Arguments.Count > 0)
                    {
                        var argType = CheckExpr(call.Arguments[0]);
                        if (argType is not TypeInfo.String && argType is not TypeInfo.StringLiteral && argType is not TypeInfo.Any)
                            throw new TypeCheckException($"Symbol() description must be a string.");
                    }
                    // Create unique symbol type for this declaration
                    constDeclaredType = new TypeInfo.UniqueSymbol(
                        constStmt.Name.Lexeme,
                        $"typeof {constStmt.Name.Lexeme}");
                }
                else if (constStmt.TypeAnnotation != null)
                {
                    constDeclaredType = ToTypeInfo(constStmt.TypeAnnotation);
                    var initType = CheckExpr(constStmt.Initializer);
                    if (!IsCompatible(constDeclaredType, initType))
                    {
                        throw new TypeMismatchException(constDeclaredType, initType, constStmt.Name.Line);
                    }
                }
                else
                {
                    // No type annotation - infer from initializer, but keep literal types for const
                    constDeclaredType = CheckExpr(constStmt.Initializer);
                }

                _environment.Define(constStmt.Name.Lexeme, constDeclaredType);
                break;
            }

            case Stmt.Function funcStmt:
                CheckFunctionDeclaration(funcStmt);
                break;

            case Stmt.Return returnStmt:
                if (_currentFunctionReturnType != null)
                {
                    // Special case: array literal returned to tuple return type (contextual typing)
                    if (_currentFunctionReturnType is TypeInfo.Tuple tupleRetType &&
                        returnStmt.Value is Expr.ArrayLiteral arrayLitRet)
                    {
                        CheckArrayLiteralAgainstTuple(arrayLitRet, tupleRetType, "return value");
                    }
                    else
                    {
                        TypeInfo actualReturnType = returnStmt.Value != null
                            ? CheckExpr(returnStmt.Value)
                            : new TypeInfo.Void();

                        // For async functions, the return type is Promise<T> but we can return T directly
                        // (the runtime automatically wraps it in a Promise)
                        TypeInfo expectedReturnType = _currentFunctionReturnType;
                        if (_inAsyncFunction && expectedReturnType is TypeInfo.Promise promiseType)
                        {
                            expectedReturnType = promiseType.ValueType;
                        }

                        // For generator functions, the return value becomes the final iterator result value.
                        // If no explicit return type is declared (void), allow any return value.
                        // The return type in generators is separate from the yield type.
                        if (_inGeneratorFunction && expectedReturnType is TypeInfo.Void)
                        {
                            // Allow any return value in generators with no explicit return type
                        }
                        else if (!IsCompatible(expectedReturnType, actualReturnType))
                        {
                             throw new TypeMismatchException(_currentFunctionReturnType, actualReturnType, returnStmt.Keyword.Line);
                        }
                    }
                }
                else if (returnStmt.Value != null)
                {
                    CheckExpr(returnStmt.Value);
                }
                break;

            case Stmt.Expression exprStmt:
                CheckExpr(exprStmt.Expr);
                // Apply assertion narrowing for calls to assertion functions
                if (exprStmt.Expr is Expr.Call assertCall)
                {
                    ApplyAssertionNarrowing(assertCall);
                }
                break;

            case Stmt.If ifStmt:
                CheckExpr(ifStmt.Condition);

                var guard = AnalyzeTypeGuard(ifStmt.Condition);
                if (guard.VarName != null)
                {
                    // Then branch with narrowed type
                    var thenEnv = new TypeEnvironment(_environment);
                    thenEnv.Define(guard.VarName, guard.NarrowedType!);
                    using (new EnvironmentScope(this, thenEnv))
                    {
                        CheckStmt(ifStmt.ThenBranch);
                    }

                    // Else branch with excluded type
                    if (ifStmt.ElseBranch != null && guard.ExcludedType != null)
                    {
                        var elseEnv = new TypeEnvironment(_environment);
                        elseEnv.Define(guard.VarName, guard.ExcludedType);
                        using (new EnvironmentScope(this, elseEnv))
                        {
                            CheckStmt(ifStmt.ElseBranch);
                        }
                    }
                    else if (ifStmt.ElseBranch != null)
                    {
                        CheckStmt(ifStmt.ElseBranch);
                    }

                    // Control flow narrowing: if then-branch terminates and no else branch,
                    // subsequent code in the same scope sees the excluded type
                    if (ifStmt.ElseBranch == null && guard.ExcludedType != null && AlwaysTerminates(ifStmt.ThenBranch))
                    {
                        _environment.Define(guard.VarName, guard.ExcludedType);
                    }
                    // If else-branch terminates and then-branch doesn't,
                    // subsequent code sees the narrowed type
                    else if (ifStmt.ElseBranch != null && guard.NarrowedType != null &&
                             AlwaysTerminates(ifStmt.ElseBranch) && !AlwaysTerminates(ifStmt.ThenBranch))
                    {
                        _environment.Define(guard.VarName, guard.NarrowedType);
                    }
                }
                else
                {
                    CheckStmt(ifStmt.ThenBranch);
                    if (ifStmt.ElseBranch != null) CheckStmt(ifStmt.ElseBranch);
                }
                break;

            case Stmt.While whileStmt:
                CheckExpr(whileStmt.Condition);
                _loopDepth++;
                try
                {
                    CheckStmt(whileStmt.Body);
                }
                finally
                {
                    _loopDepth--;
                }
                break;

            case Stmt.DoWhile doWhileStmt:
                _loopDepth++;
                try
                {
                    CheckStmt(doWhileStmt.Body);
                }
                finally
                {
                    _loopDepth--;
                }
                CheckExpr(doWhileStmt.Condition);
                break;

            case Stmt.For forStmt:
                if (forStmt.Initializer != null)
                    CheckStmt(forStmt.Initializer);
                if (forStmt.Condition != null)
                    CheckExpr(forStmt.Condition);
                _loopDepth++;
                try
                {
                    CheckStmt(forStmt.Body);
                }
                finally
                {
                    _loopDepth--;
                }
                if (forStmt.Increment != null)
                    CheckExpr(forStmt.Increment);
                break;

            case Stmt.ForOf forOf:
                TypeInfo iterableType = CheckExpr(forOf.Iterable);
                TypeInfo elementType = new TypeInfo.Any();

                // Get element type from array
                if (iterableType is TypeInfo.Array arr)
                {
                    elementType = arr.ElementType;
                }
                // Map iteration yields [key, value] tuples
                else if (iterableType is TypeInfo.Map mapType)
                {
                    elementType = TypeInfo.Tuple.FromTypes([mapType.KeyType, mapType.ValueType], 2);
                }
                // Set iteration yields values
                else if (iterableType is TypeInfo.Set setType)
                {
                    elementType = setType.ElementType;
                }
                // Iterator yields its element type
                else if (iterableType is TypeInfo.Iterator iterType)
                {
                    elementType = iterType.ElementType;
                }

                // Create new scope and define the loop variable
                TypeEnvironment forOfEnv = new(_environment);
                forOfEnv.Define(forOf.Variable.Lexeme, elementType);

                TypeEnvironment prevForOfEnv = _environment;
                _environment = forOfEnv;
                _loopDepth++;
                try
                {
                    CheckStmt(forOf.Body);
                }
                finally
                {
                    _loopDepth--;
                    _environment = prevForOfEnv;
                }
                break;

            case Stmt.ForIn forIn:
                TypeInfo objType = CheckExpr(forIn.Object);

                // for...in iterates over object keys, so element type is always string
                TypeInfo keyType = new TypeInfo.String();

                // Validate that the iterable is an object-like type
                if (objType is not (TypeInfo.Record or TypeInfo.Instance or TypeInfo.Array or TypeInfo.Any or TypeInfo.Class))
                {
                    throw new TypeCheckException($"'for...in' requires an object, got {objType}");
                }

                // Create new scope and define the loop variable
                TypeEnvironment forInEnv = new(_environment);
                forInEnv.Define(forIn.Variable.Lexeme, keyType);

                TypeEnvironment prevForInEnv = _environment;
                _environment = forInEnv;
                _loopDepth++;
                try
                {
                    CheckStmt(forIn.Body);
                }
                finally
                {
                    _loopDepth--;
                    _environment = prevForInEnv;
                }
                break;

            case Stmt.Break breakStmt:
                if (breakStmt.Label != null)
                {
                    // Labeled break: must target a valid label
                    string labelName = breakStmt.Label.Lexeme;
                    if (!_activeLabels.ContainsKey(labelName))
                    {
                        throw new TypeCheckException($"Label '{labelName}' not found");
                    }
                }
                else
                {
                    // Unlabeled break: must be inside a loop or switch
                    if (_loopDepth == 0 && _switchDepth == 0)
                    {
                        throw new TypeOperationException("'break' can only be used inside a loop or switch");
                    }
                }
                break;

            case Stmt.Switch switchStmt:
                CheckSwitch(switchStmt);
                break;

            case Stmt.TryCatch tryCatch:
                CheckTryCatch(tryCatch);
                break;

            case Stmt.Throw throwStmt:
                CheckExpr(throwStmt.Value);
                break;

            case Stmt.Continue continueStmt:
                if (continueStmt.Label != null)
                {
                    // Labeled continue: must target a valid label on a loop
                    string labelName = continueStmt.Label.Lexeme;
                    if (!_activeLabels.TryGetValue(labelName, out bool isOnLoop))
                    {
                        throw new TypeCheckException($"Label '{labelName}' not found");
                    }
                    if (!isOnLoop)
                    {
                        throw new TypeOperationException($"Cannot continue to non-loop label '{labelName}'");
                    }
                }
                else
                {
                    // Unlabeled continue: must be inside a loop
                    if (_loopDepth == 0)
                    {
                        throw new TypeOperationException("'continue' can only be used inside a loop");
                    }
                }
                break;

            case Stmt.Print printStmt:
                CheckExpr(printStmt.Expr);
                break;

            case Stmt.Import importStmt:
                // Imports are handled in CheckModules() during multi-module type checking.
                // In single-file mode, imports are an error since there's no module to import from.
                if (_currentModule == null)
                {
                    throw new TypeCheckException("Import statements require module mode. " +
                                       "Use 'dotnet run -- --compile' with multi-file support", importStmt.Keyword.Line);
                }
                // When in module mode, imports are resolved and bound in BindModuleImports()
                break;

            case Stmt.Export exportStmt:
                // Check the declaration or expression being exported
                CheckExportStatement(exportStmt);
                break;

            case Stmt.FileDirective directive:
                // Validate file-level directives like @Namespace
                ValidateFileDirective(directive);
                break;

            case Stmt.Directive:
                // Directives like "use strict" are processed at the start of type checking.
                // They have no type checking side effects beyond setting strict mode.
                break;
        }
    }

    /// <summary>
    /// Validates file-level directives like @Namespace.
    /// </summary>
    private void ValidateFileDirective(Stmt.FileDirective directive)
    {
        foreach (var decorator in directive.Decorators)
        {
            if (decorator.Expression is Expr.Call call &&
                call.Callee is Expr.Variable v &&
                v.Name.Lexeme == "Namespace")
            {
                if (call.Arguments.Count != 1)
                {
                    throw new TypeCheckException("@Namespace requires exactly one string argument", decorator.AtToken.Line);
                }
                if (call.Arguments[0] is not Expr.Literal { Value: string })
                {
                    throw new TypeCheckException("@Namespace argument must be a string literal", decorator.AtToken.Line);
                }
            }
            else
            {
                throw new TypeCheckException("Unknown file-level directive. Only @Namespace is supported", decorator.AtToken.Line);
            }
        }
    }

    /// <summary>
    /// Determines if a statement always terminates (returns, throws, etc.).
    /// Used for control flow analysis to determine if narrowed types persist.
    /// </summary>
    private static bool AlwaysTerminates(Stmt stmt) => stmt switch
    {
        Stmt.Return => true,
        Stmt.Throw => true,
        Stmt.Block block => block.Statements.Count > 0 && AlwaysTerminates(block.Statements[^1]),
        Stmt.Sequence seq => seq.Statements.Count > 0 && AlwaysTerminates(seq.Statements[^1]),
        Stmt.If ifStmt => AlwaysTerminates(ifStmt.ThenBranch) &&
                         ifStmt.ElseBranch != null && AlwaysTerminates(ifStmt.ElseBranch),
        _ => false
    };

    /// <summary>
    /// Validates that any spread elements in a type alias definition reference type parameters
    /// that are constrained to array-like types (e.g., T extends unknown[]).
    /// </summary>
    private void ValidateTypeAliasSpreadConstraints(Stmt.TypeAlias typeAlias)
    {
        if (typeAlias.TypeParameters == null || typeAlias.TypeParameters.Count == 0)
            return;

        // Build a dictionary of type parameter constraints
        var constraints = new Dictionary<string, string?>();
        foreach (var tp in typeAlias.TypeParameters)
        {
            constraints[tp.Name.Lexeme] = tp.Constraint;
        }

        // Find spread patterns like ...T in the definition
        string definition = typeAlias.TypeDefinition;
        int idx = 0;
        while ((idx = definition.IndexOf("...", idx, StringComparison.Ordinal)) >= 0)
        {
            idx += 3; // Skip past "..."
            if (idx >= definition.Length)
                break;

            // Skip whitespace
            while (idx < definition.Length && char.IsWhiteSpace(definition[idx]))
                idx++;

            if (idx >= definition.Length)
                break;

            // Extract the type name after ...
            int start = idx;
            while (idx < definition.Length && (char.IsLetterOrDigit(definition[idx]) || definition[idx] == '_'))
                idx++;

            if (start == idx)
                continue;

            string typeName = definition[start..idx];

            // Check if this is a type parameter with an array-like constraint
            if (constraints.TryGetValue(typeName, out var constraint))
            {
                // Type parameter found - check constraint
                if (string.IsNullOrEmpty(constraint) || !IsArrayLikeConstraint(constraint))
                {
                    throw new TypeCheckException(
                        $" A rest element type must be an array type. " +
                        $"Type parameter '{typeName}' is not constrained to an array type.");
                }
            }
            // If not a type parameter (e.g., a concrete type like ...number[]), that's fine
        }
    }

    /// <summary>
    /// Checks if a constraint string represents an array-like type.
    /// </summary>
    private static bool IsArrayLikeConstraint(string constraint)
    {
        string trimmed = constraint.Trim();
        // Check for common array-like constraints
        return trimmed.EndsWith("[]", StringComparison.Ordinal) ||  // T extends number[], string[], unknown[], etc.
               trimmed == "unknown[]" ||
               trimmed.StartsWith("[", StringComparison.Ordinal) || // T extends [string, number], etc. (tuples)
               trimmed == "readonly unknown[]" ||
               trimmed.StartsWith("readonly ", StringComparison.Ordinal) && trimmed.EndsWith("[]", StringComparison.Ordinal) ||
               trimmed.StartsWith("Array<", StringComparison.Ordinal); // T extends Array<unknown>
    }

    /// <summary>
    /// Applies type narrowing from assertion function calls.
    /// When a function with "asserts x is T" or "asserts x" return type is called,
    /// the variable x is narrowed in all subsequent code.
    /// </summary>
    private void ApplyAssertionNarrowing(Expr.Call call)
    {
        // Get the callee's type
        TypeInfo? calleeType = null;

        if (call.Callee is Expr.Variable funcVar)
        {
            calleeType = _environment.Get(funcVar.Name.Lexeme);
        }
        else if (call.Callee is Expr.Get getExpr)
        {
            var objType = CheckExpr(getExpr.Object);
            calleeType = GetMemberType(objType, getExpr.Name.Lexeme);
        }

        if (calleeType == null) return;

        // Get the return type
        TypeInfo? returnType = calleeType switch
        {
            TypeInfo.Function func => func.ReturnType,
            TypeInfo.GenericFunction gf => gf.ReturnType,
            _ => null
        };

        // Handle "asserts x is T" - narrow to the predicate type
        if (returnType is TypeInfo.TypePredicate pred && pred.IsAssertion)
        {
            // For simplicity, assume first argument corresponds to the predicate parameter
            if (call.Arguments.Count > 0 && call.Arguments[0] is Expr.Variable argVar)
            {
                // Narrow the type in the current environment
                _environment.Define(argVar.Name.Lexeme, pred.PredicateType);
            }
        }
        // Handle "asserts x" - narrow to exclude null/undefined
        else if (returnType is TypeInfo.AssertsNonNull assertsNonNull)
        {
            // For simplicity, assume first argument corresponds to the asserted parameter
            if (call.Arguments.Count > 0 && call.Arguments[0] is Expr.Variable argVar)
            {
                var currentType = _environment.Get(argVar.Name.Lexeme);
                if (currentType != null)
                {
                    // Remove null and undefined from the type
                    TypeInfo narrowedType = ExcludeNullUndefined(currentType);
                    _environment.Define(argVar.Name.Lexeme, narrowedType);
                }
            }
        }
    }

    /// <summary>
    /// Removes null and undefined from a type.
    /// </summary>
    private static TypeInfo ExcludeNullUndefined(TypeInfo type)
    {
        if (type is TypeInfo.Union union)
        {
            var remaining = union.FlattenedTypes
                .Where(t => t is not TypeInfo.Null and not TypeInfo.Undefined)
                .ToList();

            if (remaining.Count == 0) return new TypeInfo.Never();
            if (remaining.Count == 1) return remaining[0];
            return new TypeInfo.Union(remaining);
        }

        // If the type itself is null or undefined, return never
        if (type is TypeInfo.Null or TypeInfo.Undefined)
            return new TypeInfo.Never();

        return type;
    }
}
