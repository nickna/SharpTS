using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Statement type checking - CheckStmt and the main dispatch switch.
/// </summary>
/// <remarks>
/// Contains the main statement dispatch (CheckStmt) via <see cref="IStmtVisitor{TResult}"/>
/// and inline handling for simple statements. Complex statement handlers are split into
/// separate partial files:
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
    /// <summary>
    /// Type-checks a statement. Dispatches to the appropriate Visit* method via the registry.
    /// </summary>
    /// <param name="stmt">The statement AST node to type-check.</param>
    private void CheckStmt(Stmt stmt)
    {
        _registry.DispatchStmt(stmt, this);
    }

    // Statement handlers - called by the registry

    internal VoidResult VisitBlock(Stmt.Block stmt)
    {
        CheckBlock(stmt.Statements, new TypeEnvironment(_environment));
        return VoidResult.Instance;
    }

    internal VoidResult VisitSequence(Stmt.Sequence stmt)
    {
        foreach (var s in stmt.Statements)
            CheckStmt(s);
        return VoidResult.Instance;
    }

    internal VoidResult VisitLabeledStatement(Stmt.LabeledStatement stmt)
    {
        string labelName = stmt.Label.Lexeme;

        // Check for label shadowing
        if (_activeLabels.ContainsKey(labelName))
        {
            throw new TypeCheckException($"Label '{labelName}' already declared in this scope");
        }

        // Determine if this label is on a loop (for continue validation)
        bool isOnLoop = stmt.Statement is Stmt.While
                     or Stmt.For
                     or Stmt.DoWhile
                     or Stmt.ForOf
                     or Stmt.ForIn
                     or Stmt.LabeledStatement; // Allow chained labels

        // If chained label, inherit loop status from inner
        if (stmt.Statement is Stmt.LabeledStatement)
        {
            isOnLoop = true;
        }

        _activeLabels[labelName] = isOnLoop;
        try
        {
            CheckStmt(stmt.Statement);
        }
        finally
        {
            _activeLabels.Remove(labelName);
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitInterface(Stmt.Interface stmt)
    {
        CheckInterfaceDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitTypeAlias(Stmt.TypeAlias stmt)
    {
        if (stmt.TypeParameters != null && stmt.TypeParameters.Count > 0)
        {
            var typeParamNames = stmt.TypeParameters.Select(tp => tp.Name.Lexeme).ToList();
            ValidateTypeAliasSpreadConstraints(stmt);
            _environment.DefineGenericTypeAlias(stmt.Name.Lexeme, stmt.TypeDefinition, typeParamNames);
        }
        else
        {
            _environment.DefineTypeAlias(stmt.Name.Lexeme, stmt.TypeDefinition);
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitEnum(Stmt.Enum stmt)
    {
        CheckEnumDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitNamespace(Stmt.Namespace stmt)
    {
        CheckNamespace(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitImportAlias(Stmt.ImportAlias stmt)
    {
        CheckImportAlias(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitClass(Stmt.Class stmt)
    {
        CheckClassDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitVar(Stmt.Var stmt)
    {
        TypeInfo? declaredType = null;
        if (stmt.TypeAnnotation != null)
        {
            declaredType = ToTypeInfo(stmt.TypeAnnotation);
        }

        if (stmt.HasDefiniteAssignmentAssertion)
        {
            _environment.Define(stmt.Name.Lexeme, declaredType!);
            // Record the declared type for assignment checking
            RecordDeclaredType(stmt.Name.Lexeme, declaredType!);
            // Register as local variable for escape analysis
            _escapeAnalyzer.DefineVariable(stmt.Name.Lexeme);
            return VoidResult.Instance;
        }

        if (stmt.Initializer != null)
        {
            var provisionalType = declaredType ?? new TypeInfo.Any();
            _environment.Define(stmt.Name.Lexeme, provisionalType);
            // Register as local variable for escape analysis
            _escapeAnalyzer.DefineVariable(stmt.Name.Lexeme);

            // Track variable-to-variable aliases for narrowing invalidation
            // e.g., "const alias = obj" tracks that "alias" is an alias for "obj"
            if (stmt.Initializer is Expr.Variable initVar)
            {
                var initType = _environment.Get(initVar.Name.Lexeme);
                if (initType != null && IsObjectType(initType))
                {
                    _variableAliases[stmt.Name.Lexeme] = initVar.Name.Lexeme;
                }
            }

            if (declaredType is TypeInfo.Tuple tupleType && stmt.Initializer is Expr.ArrayLiteral arrayLit)
            {
                CheckArrayLiteralAgainstTuple(arrayLit, tupleType, stmt.Name.Lexeme);
            }
            else
            {
                Expr initializer = stmt.Initializer;
                bool checkExcessProps = false;

                if (declaredType != null && initializer is Expr.ObjectLiteral objLit)
                {
                    initializer = objLit with { IsFresh = true };
                    checkExcessProps = true;
                }

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
                    if (checkExcessProps && initializerType is TypeInfo.Record actualRecord)
                    {
                        CheckExcessProperties(actualRecord, declaredType, stmt.Initializer);
                    }

                    if (!IsCompatible(declaredType, initializerType))
                    {
                        throw new TypeMismatchException(declaredType, initializerType, stmt.Name.Line);
                    }
                }
                else
                {
                    initializerType = WidenLiteralType(initializerType);
                    declaredType = initializerType;
                    _environment.Define(stmt.Name.Lexeme, declaredType);
                }

                declaredType ??= initializerType;
            }
            // Record the declared type for assignment checking
            RecordDeclaredType(stmt.Name.Lexeme, declaredType!);
            return VoidResult.Instance;
        }

        declaredType ??= new TypeInfo.Any();
        _environment.Define(stmt.Name.Lexeme, declaredType);
        // Record the declared type for assignment checking
        RecordDeclaredType(stmt.Name.Lexeme, declaredType);
        return VoidResult.Instance;
    }

    internal VoidResult VisitConst(Stmt.Const stmt)
    {
        TypeInfo constDeclaredType;

        // Track variable-to-variable aliases for narrowing invalidation
        // e.g., "const alias = obj" tracks that "alias" is an alias for "obj"
        if (stmt.Initializer is Expr.Variable initVar)
        {
            var initType = _environment.Get(initVar.Name.Lexeme);
            if (initType != null && IsObjectType(initType))
            {
                _variableAliases[stmt.Name.Lexeme] = initVar.Name.Lexeme;
            }
        }

        if (stmt.TypeAnnotation == "unique symbol")
        {
            if (stmt.Initializer is not Expr.Call call ||
                call.Callee is not Expr.Variable v ||
                v.Name.Lexeme != "Symbol")
            {
                throw new TypeCheckException(
                    $"'unique symbol' must be initialized with Symbol() at line {stmt.Name.Line}.");
            }
            if (call.Arguments.Count > 0)
            {
                var argType = CheckExpr(call.Arguments[0]);
                if (argType is not TypeInfo.String && argType is not TypeInfo.StringLiteral && argType is not TypeInfo.Any)
                    throw new TypeCheckException($"Symbol() description must be a string.");
            }
            constDeclaredType = new TypeInfo.UniqueSymbol(
                stmt.Name.Lexeme,
                $"typeof {stmt.Name.Lexeme}");
        }
        else if (stmt.TypeAnnotation != null)
        {
            constDeclaredType = ToTypeInfo(stmt.TypeAnnotation);
            _environment.Define(stmt.Name.Lexeme, constDeclaredType);
            var initType = CheckExpr(stmt.Initializer);
            if (!IsCompatible(constDeclaredType, initType))
            {
                throw new TypeMismatchException(constDeclaredType, initType, stmt.Name.Line);
            }
        }
        else
        {
            _environment.Define(stmt.Name.Lexeme, new TypeInfo.Any());
            constDeclaredType = CheckExpr(stmt.Initializer);
            _environment.Define(stmt.Name.Lexeme, constDeclaredType);
        }

        _environment.Define(stmt.Name.Lexeme, constDeclaredType);
        // Register as local variable for escape analysis
        _escapeAnalyzer.DefineVariable(stmt.Name.Lexeme);
        return VoidResult.Instance;
    }

    internal VoidResult VisitFunction(Stmt.Function stmt)
    {
        CheckFunctionDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitReturn(Stmt.Return stmt)
    {
        if (_inStaticBlock)
        {
            throw new TypeCheckException("Return statements are not allowed in static blocks.");
        }
        if (_currentFunctionReturnType != null)
        {
            if (_currentFunctionReturnType is TypeInfo.Tuple tupleRetType &&
                stmt.Value is Expr.ArrayLiteral arrayLitRet)
            {
                CheckArrayLiteralAgainstTuple(arrayLitRet, tupleRetType, "return value");
            }
            else
            {
                TypeInfo actualReturnType = stmt.Value != null
                    ? CheckExpr(stmt.Value)
                    : new TypeInfo.Void();

                TypeInfo expectedReturnType = _currentFunctionReturnType;
                if (_inAsyncFunction && expectedReturnType is TypeInfo.Promise promiseType)
                {
                    expectedReturnType = promiseType.ValueType;
                }

                if (_inGeneratorFunction && expectedReturnType is TypeInfo.Void)
                {
                    // Allow any return value in generators with no explicit return type
                }
                else if (!IsCompatible(expectedReturnType, actualReturnType))
                {
                    throw new TypeMismatchException(_currentFunctionReturnType, actualReturnType, stmt.Keyword.Line);
                }
            }
        }
        else if (stmt.Value != null)
        {
            CheckExpr(stmt.Value);
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitExpression(Stmt.Expression stmt)
    {
        CheckExpr(stmt.Expr);
        if (stmt.Expr is Expr.Call assertCall)
        {
            ApplyAssertionNarrowing(assertCall);
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitIf(Stmt.If stmt)
    {
        CheckExpr(stmt.Condition);

        // Try compound type guard analysis first (handles && conditions like x !== null && y !== null)
        var compoundGuards = AnalyzeCompoundTypeGuards(stmt.Condition);

        if (compoundGuards.Count > 0)
        {
            // Apply all narrowings for compound conditions
            ApplyCompoundNarrowings(stmt, compoundGuards);
        }
        else
        {
            // Fall back to legacy type guard analysis for other patterns (typeof, instanceof, etc.)
            var guard = AnalyzeTypeGuard(stmt.Condition);

            if (guard.VarName != null)
            {
                var thenEnv = new TypeEnvironment(_environment);
                thenEnv.Define(guard.VarName, guard.NarrowedType!);
                using (new EnvironmentScope(this, thenEnv))
                {
                    CheckStmt(stmt.ThenBranch);
                }

                if (stmt.ElseBranch != null && guard.ExcludedType != null)
                {
                    var elseEnv = new TypeEnvironment(_environment);
                    elseEnv.Define(guard.VarName, guard.ExcludedType);
                    using (new EnvironmentScope(this, elseEnv))
                    {
                        CheckStmt(stmt.ElseBranch);
                    }
                }
                else if (stmt.ElseBranch != null)
                {
                    CheckStmt(stmt.ElseBranch);
                }

                if (stmt.ElseBranch == null && guard.ExcludedType != null && AlwaysTerminates(stmt.ThenBranch))
                {
                    _environment.Define(guard.VarName, guard.ExcludedType);
                }
                else if (stmt.ElseBranch != null && guard.NarrowedType != null &&
                         AlwaysTerminates(stmt.ElseBranch) && !AlwaysTerminates(stmt.ThenBranch))
                {
                    _environment.Define(guard.VarName, guard.NarrowedType);
                }
            }
            else
            {
                CheckStmt(stmt.ThenBranch);
                if (stmt.ElseBranch != null) CheckStmt(stmt.ElseBranch);
            }
        }
        return VoidResult.Instance;
    }

    /// <summary>
    /// Applies compound narrowings from multiple type guards (e.g., x !== null && y !== null).
    /// </summary>
    private void ApplyCompoundNarrowings(
        Stmt.If stmt,
        List<(Narrowing.NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> narrowings)
    {
        // Separate variable and property path narrowings
        var varNarrowings = narrowings.Where(n => n.Path is Narrowing.NarrowingPath.Variable).ToList();
        var propNarrowings = narrowings.Where(n => n.Path is not Narrowing.NarrowingPath.Variable).ToList();

        // Build then branch environment with all variable narrowings
        var thenEnv = new TypeEnvironment(_environment);
        foreach (var (path, narrowedType, _) in varNarrowings)
        {
            if (path is Narrowing.NarrowingPath.Variable varPath)
            {
                thenEnv.Define(varPath.Name, narrowedType);
            }
        }

        // Build then branch context with all property narrowings
        var thenContext = Narrowing.NarrowingContext.Empty;
        foreach (var (path, narrowedType, _) in propNarrowings)
        {
            thenContext = thenContext.WithNarrowing(path, narrowedType);
        }

        // Check then branch with all narrowings applied
        using (new EnvironmentScope(this, thenEnv))
        {
            if (!thenContext.IsEmpty)
            {
                PushNarrowingContext(thenContext);
            }
            try
            {
                CheckStmt(stmt.ThenBranch);
            }
            finally
            {
                if (!thenContext.IsEmpty)
                {
                    PopNarrowingContext();
                }
            }
        }

        // For else branch, apply excluded types
        if (stmt.ElseBranch != null)
        {
            // For a single condition, we can apply the excluded type in the else branch
            // For compound conditions (&&), the else branch means at least one condition is false,
            // so we can't safely narrow all variables to their excluded types
            if (narrowings.Count == 1)
            {
                var (path, _, excludedType) = narrowings[0];

                // Build else branch environment with excluded type for variables
                var elseEnv = new TypeEnvironment(_environment);
                var elseContext = Narrowing.NarrowingContext.Empty;

                if (path is Narrowing.NarrowingPath.Variable varPath)
                {
                    elseEnv.Define(varPath.Name, excludedType);
                }
                else
                {
                    elseContext = elseContext.WithNarrowing(path, excludedType);
                }

                using (new EnvironmentScope(this, elseEnv))
                {
                    if (!elseContext.IsEmpty)
                    {
                        PushNarrowingContext(elseContext);
                    }
                    try
                    {
                        CheckStmt(stmt.ElseBranch);
                    }
                    finally
                    {
                        if (!elseContext.IsEmpty)
                        {
                            PopNarrowingContext();
                        }
                    }
                }
            }
            else
            {
                // For compound conditions, just check without narrowing
                CheckStmt(stmt.ElseBranch);
            }
        }

        // Handle early termination: if then branch terminates, apply excluded types after
        if (stmt.ElseBranch == null && AlwaysTerminates(stmt.ThenBranch))
        {
            foreach (var (path, _, excludedType) in narrowings)
            {
                if (path is Narrowing.NarrowingPath.Variable varPath)
                {
                    _environment.Define(varPath.Name, excludedType);
                }
                else
                {
                    AddNarrowing(path, excludedType);
                }
            }
        }
    }

    internal VoidResult VisitWhile(Stmt.While stmt)
    {
        CheckExpr(stmt.Condition);

        // Analyze type guard from condition using path-based analysis
        // Try compound type guard analysis first (handles && conditions)
        var conditionNarrowings = AnalyzeCompoundTypeGuards(stmt.Condition);

        // If no compound narrowings found, try simple path analysis
        if (conditionNarrowings.Count == 0)
        {
            var (path, narrowedType, excludedType) = AnalyzePathTypeGuard(stmt.Condition);
            if (path != null && narrowedType != null && excludedType != null)
            {
                conditionNarrowings.Add((path, narrowedType, excludedType));
            }
        }

        // Analyze the loop body for assignments that would invalidate narrowings on subsequent iterations
        var assignedPaths = ControlFlow.LoopAssignmentAnalyzer.GetAssignedPaths(stmt.Body);

        _loopDepth++;
        try
        {
            // Always push a scope for the loop body to contain invalidations
            PushNarrowingScope();
            try
            {
                // Invalidate narrowings for any paths assigned within the loop
                // This handles the case where narrowings from outer scope would be invalid on subsequent iterations
                foreach (var assignedPath in assignedPaths)
                {
                    InvalidateNarrowingsFor(assignedPath);
                }

                // Apply narrowings from the condition (for compound conditions like x !== null && y !== null)
                // But only if the path is not assigned within the loop
                foreach (var (condPath, narrowedType, _) in conditionNarrowings)
                {
                    if (!IsPathAffectedByAssignments(condPath, assignedPaths))
                    {
                        AddNarrowing(condPath, narrowedType);
                    }
                }

                CheckStmt(stmt.Body);
            }
            finally
            {
                PopNarrowingScope();
            }
        }
        finally
        {
            _loopDepth--;
        }

        // After loop exits, the condition was false
        // Apply the negated narrowings (excluded types)
        foreach (var (condPath, _, excludedType) in conditionNarrowings)
        {
            AddNarrowing(condPath, excludedType);
        }

        return VoidResult.Instance;
    }

    internal VoidResult VisitDoWhile(Stmt.DoWhile stmt)
    {
        // Analyze the loop body for assignments that would invalidate narrowings on subsequent iterations
        var assignedPaths = ControlFlow.LoopAssignmentAnalyzer.GetAssignedPaths(stmt.Body);

        _loopDepth++;
        try
        {
            // Always push a scope for the loop body to contain invalidations
            PushNarrowingScope();
            try
            {
                // Invalidate narrowings for any paths assigned within the loop
                foreach (var assignedPath in assignedPaths)
                {
                    InvalidateNarrowingsFor(assignedPath);
                }
                CheckStmt(stmt.Body);
            }
            finally
            {
                PopNarrowingScope();
            }
        }
        finally { _loopDepth--; }
        CheckExpr(stmt.Condition);
        return VoidResult.Instance;
    }

    internal VoidResult VisitFor(Stmt.For stmt)
    {
        if (stmt.Initializer != null)
            CheckStmt(stmt.Initializer);

        // Analyze type guard from condition if present using path-based analysis
        // Try compound type guard analysis first (handles && conditions)
        List<(Narrowing.NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> conditionNarrowings = [];

        if (stmt.Condition != null)
        {
            CheckExpr(stmt.Condition);
            conditionNarrowings = AnalyzeCompoundTypeGuards(stmt.Condition);

            // If no compound narrowings found, try simple path analysis
            if (conditionNarrowings.Count == 0)
            {
                var (path, narrowedType, excludedType) = AnalyzePathTypeGuard(stmt.Condition);
                if (path != null && narrowedType != null && excludedType != null)
                {
                    conditionNarrowings.Add((path, narrowedType, excludedType));
                }
            }
        }

        // Analyze the loop body for assignments that would invalidate narrowings on subsequent iterations
        var assignedPaths = ControlFlow.LoopAssignmentAnalyzer.GetAssignedPaths(stmt.Body);
        if (stmt.Increment != null)
        {
            // Include increment expression in analysis
            var incrementPaths = ControlFlow.LoopAssignmentAnalyzer.GetAssignedPaths(new Stmt.Expression(stmt.Increment));
            foreach (var p in incrementPaths)
                assignedPaths.Add(p);
        }

        _loopDepth++;
        try
        {
            // Always push a scope for the loop body to contain invalidations
            PushNarrowingScope();
            try
            {
                // Invalidate narrowings for any paths assigned within the loop
                // This handles the case where narrowings from outer scope would be invalid on subsequent iterations
                foreach (var assignedPath in assignedPaths)
                {
                    InvalidateNarrowingsFor(assignedPath);
                }

                // Apply narrowings from the condition (for compound conditions like x !== null && y !== null)
                // But only if the path is not assigned within the loop
                foreach (var (condPath, narrowedType, _) in conditionNarrowings)
                {
                    if (!IsPathAffectedByAssignments(condPath, assignedPaths))
                    {
                        AddNarrowing(condPath, narrowedType);
                    }
                }

                CheckStmt(stmt.Body);
            }
            finally
            {
                PopNarrowingScope();
            }
        }
        finally
        {
            _loopDepth--;
        }

        if (stmt.Increment != null)
            CheckExpr(stmt.Increment);

        // After loop exits, the condition was false (if there was a condition)
        // Apply the negated narrowings (excluded types)
        foreach (var (condPath, _, excludedType) in conditionNarrowings)
        {
            AddNarrowing(condPath, excludedType);
        }

        return VoidResult.Instance;
    }

    internal VoidResult VisitForOf(Stmt.ForOf stmt)
    {
        TypeInfo iterableType = CheckExpr(stmt.Iterable);
        TypeInfo elementType = new TypeInfo.Any();

        if (iterableType is TypeInfo.Array arr)
            elementType = arr.ElementType;
        else if (iterableType is TypeInfo.Map mapType)
            elementType = TypeInfo.Tuple.FromTypes([mapType.KeyType, mapType.ValueType], 2);
        else if (iterableType is TypeInfo.Set setType)
            elementType = setType.ElementType;
        else if (iterableType is TypeInfo.Iterator iterType)
            elementType = iterType.ElementType;

        // Analyze the loop body for assignments that would invalidate narrowings on subsequent iterations
        var assignedPaths = ControlFlow.LoopAssignmentAnalyzer.GetAssignedPaths(stmt.Body);

        TypeEnvironment forOfEnv = new(_environment);
        forOfEnv.Define(stmt.Variable.Lexeme, elementType);

        TypeEnvironment prevForOfEnv = _environment;
        _environment = forOfEnv;
        _loopDepth++;
        try
        {
            // Always push a scope for the loop body to contain invalidations
            PushNarrowingScope();
            try
            {
                // Invalidate narrowings for any paths assigned within the loop
                foreach (var assignedPath in assignedPaths)
                {
                    InvalidateNarrowingsFor(assignedPath);
                }
                CheckStmt(stmt.Body);
            }
            finally
            {
                PopNarrowingScope();
            }
        }
        finally
        {
            _loopDepth--;
            _environment = prevForOfEnv;
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitForIn(Stmt.ForIn stmt)
    {
        TypeInfo objType = CheckExpr(stmt.Object);
        TypeInfo keyType = new TypeInfo.String();

        if (objType is not (TypeInfo.Record or TypeInfo.Instance or TypeInfo.Array or TypeInfo.Any or TypeInfo.Class))
        {
            throw new TypeCheckException($"'for...in' requires an object, got {objType}");
        }

        // Analyze the loop body for assignments that would invalidate narrowings on subsequent iterations
        var assignedPaths = ControlFlow.LoopAssignmentAnalyzer.GetAssignedPaths(stmt.Body);

        TypeEnvironment forInEnv = new(_environment);
        forInEnv.Define(stmt.Variable.Lexeme, keyType);

        TypeEnvironment prevForInEnv = _environment;
        _environment = forInEnv;
        _loopDepth++;
        try
        {
            // Always push a scope for the loop body to contain invalidations
            PushNarrowingScope();
            try
            {
                // Invalidate narrowings for any paths assigned within the loop
                foreach (var assignedPath in assignedPaths)
                {
                    InvalidateNarrowingsFor(assignedPath);
                }
                CheckStmt(stmt.Body);
            }
            finally
            {
                PopNarrowingScope();
            }
        }
        finally
        {
            _loopDepth--;
            _environment = prevForInEnv;
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitBreak(Stmt.Break stmt)
    {
        if (stmt.Label != null)
        {
            string labelName = stmt.Label.Lexeme;
            if (!_activeLabels.ContainsKey(labelName))
            {
                throw new TypeCheckException($"Label '{labelName}' not found");
            }
        }
        else
        {
            if (_loopDepth == 0 && _switchDepth == 0)
            {
                throw new TypeOperationException("'break' can only be used inside a loop or switch");
            }
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitSwitch(Stmt.Switch stmt)
    {
        CheckSwitch(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitTryCatch(Stmt.TryCatch stmt)
    {
        CheckTryCatch(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitThrow(Stmt.Throw stmt)
    {
        CheckExpr(stmt.Value);
        return VoidResult.Instance;
    }

    internal VoidResult VisitContinue(Stmt.Continue stmt)
    {
        if (stmt.Label != null)
        {
            string labelName = stmt.Label.Lexeme;
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
            if (_loopDepth == 0)
            {
                throw new TypeOperationException("'continue' can only be used inside a loop");
            }
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitPrint(Stmt.Print stmt)
    {
        CheckExpr(stmt.Expr);
        return VoidResult.Instance;
    }

    internal VoidResult VisitImport(Stmt.Import stmt)
    {
        if (_currentModule == null)
        {
            throw new TypeCheckException("Import statements require module mode. " +
                               "Use 'dotnet run -- --compile' with multi-file support", stmt.Keyword.Line);
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitImportRequire(Stmt.ImportRequire stmt)
    {
        CheckImportRequire(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitExport(Stmt.Export stmt)
    {
        CheckExportStatement(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitFileDirective(Stmt.FileDirective stmt)
    {
        ValidateFileDirective(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitDirective(Stmt.Directive stmt) => VoidResult.Instance;
    internal VoidResult VisitStaticBlock(Stmt.StaticBlock stmt) => VoidResult.Instance;

    internal VoidResult VisitDeclareModule(Stmt.DeclareModule stmt)
    {
        CheckDeclareModuleStatement(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitDeclareGlobal(Stmt.DeclareGlobal stmt)
    {
        CheckDeclareGlobalStatement(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitUsing(Stmt.Using stmt)
    {
        CheckUsingDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitField(Stmt.Field stmt) => VoidResult.Instance;
    internal VoidResult VisitAccessor(Stmt.Accessor stmt) => VoidResult.Instance;
    internal VoidResult VisitAutoAccessor(Stmt.AutoAccessor stmt) => VoidResult.Instance;

    /// <summary>
    /// Type checks a 'using' or 'await using' declaration.
    /// Validates the basic structure and defines variables in scope.
    /// Actual dispose method validation is done at runtime for flexibility.
    /// </summary>
    private void CheckUsingDeclaration(Stmt.Using usingStmt)
    {
        // 'await using' is only valid inside async functions
        if (usingStmt.IsAsync && !_inAsyncFunction)
        {
            throw new TypeCheckException(
                "'await using' is only allowed inside an async function.",
                usingStmt.Keyword.Line);
        }

        foreach (var binding in usingStmt.Bindings)
        {
            TypeInfo initType = CheckExpr(binding.Initializer);

            // Check for declared type annotation
            TypeInfo? declaredType = null;
            if (binding.TypeAnnotation != null)
            {
                declaredType = ToTypeInfo(binding.TypeAnnotation);
                if (!IsCompatible(declaredType, initType))
                {
                    throw new TypeMismatchException(declaredType, initType, binding.Name!.Line);
                }
            }

            TypeInfo resourceType = declaredType ?? initType;

            // Validate that the type is object-like (can have Symbol.dispose method)
            // Primitive types cannot have dispose methods
            if (resourceType is TypeInfo.Primitive prim && !IsNullablePrimitive(prim))
            {
                throw new TypeCheckException(
                    $"Type '{resourceType}' cannot be used with 'using' - it cannot have a disposal method.",
                    usingStmt.Keyword.Line);
            }

            // Define variable (const-like - cannot reassign)
            if (binding.Name != null)
            {
                _environment.Define(binding.Name.Lexeme, resourceType);
            }
        }
    }

    /// <summary>
    /// Checks if a primitive type is nullable (null or undefined).
    /// </summary>
    private static bool IsNullablePrimitive(TypeInfo.Primitive prim)
    {
        return prim.Type == TokenType.NULL || prim.Type == TokenType.UNDEFINED;
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
            // Look up the parameter index by name from the function type
            int paramIndex = FindParameterIndex(calleeType, pred.ParameterName);
            if (paramIndex >= 0 && paramIndex < call.Arguments.Count &&
                call.Arguments[paramIndex] is Expr.Variable argVar)
            {
                // Narrow the type in the current environment
                _environment.Define(argVar.Name.Lexeme, pred.PredicateType);
            }
        }
        // Handle "asserts x" - narrow to exclude null/undefined
        else if (returnType is TypeInfo.AssertsNonNull assertsNonNull)
        {
            // Look up the parameter index by name from the function type
            int paramIndex = FindParameterIndex(calleeType, assertsNonNull.ParameterName);
            if (paramIndex >= 0 && paramIndex < call.Arguments.Count &&
                call.Arguments[paramIndex] is Expr.Variable argVar)
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
