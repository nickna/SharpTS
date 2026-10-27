using SharpTS.Parsing;
using SharpTS.TypeSystem.ControlFlow;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Control flow statement type checking - handles blocks, switch statements, and try/catch/finally.
/// </summary>
public partial class TypeChecker
{
    private void CheckBlock(List<Stmt> statements, TypeEnvironment environment)
    {
        TypeEnvironment previous = _environment;
        try
        {
            _environment = environment;
            HoistClassDeclarations(statements);
            HoistLexicalDeclarations(statements);
            CheckStmtList(statements);
            if (environment.Enclosing is { } parent)
                PublishInheritedValueFlow(environment, parent);
        }
        finally
        {
            _environment = previous;
        }
    }

    private void CheckSwitch(Stmt.Switch switchStmt)
    {
        CheckExpr(switchStmt.Subject);
        using var editorScope = EnterEditorLexicalScope(switchStmt);
        var enclosingEnvironment = _environment;
        var switchEnvironment = new TypeEnvironment(enclosingEnvironment);
        Expr.Variable? discriminatedVariable = null;
        Token? discriminantProperty = null;
        BindingSymbol? discriminatedBinding = null;
        if (switchStmt.Subject is Expr.Get { Object: Expr.Variable variable, Name: var property })
        {
            discriminatedVariable = variable;
            discriminantProperty = property;
            discriminatedBinding = _environment.GetValueBinding(variable.Name.Lexeme);
        }

        using var switchScope = new EnvironmentScope(this, switchEnvironment);
        var statements = switchStmt.Clauses.SelectMany(clause => clause.Body).ToList();
        HoistClassDeclarations(statements);
        HoistFunctionDeclarations(statements);
        HoistLexicalDeclarations(statements);
        var incomingFlow = CaptureValueFlow();
        var entries = new ValueFlowState[switchStmt.Clauses.Count];
        var canNarrow = new bool[switchStmt.Clauses.Count];
        bool canNarrowUnmatched;

        // Check each test once in selection order. Default's direct entry sees
        // every failed test, even tests written after the default clause.
        using (var writes = BeginConditionWriteCapture())
        {
            for (int index = 0; index < switchStmt.Clauses.Count; index++)
            {
                if (switchStmt.Clauses[index].Value is not { } test) continue;
                CheckExpr(test);
                entries[index] = CaptureValueFlow();
                canNarrow[index] = discriminatedBinding is not null &&
                    ReferenceEquals(_environment.GetValueBinding(discriminatedVariable!.Name.Lexeme), discriminatedBinding) &&
                    !writes.Writes.ContainsKey(discriminatedBinding);
            }
            canNarrowUnmatched = discriminatedBinding is not null &&
                ReferenceEquals(_environment.GetValueBinding(discriminatedVariable!.Name.Lexeme), discriminatedBinding) &&
                !writes.Writes.ContainsKey(discriminatedBinding);
        }
        var unmatchedFlow = CaptureValueFlow();
        int defaultIndex = switchStmt.Clauses.FindIndex(clause => clause.IsDefault);
        if (defaultIndex >= 0) entries[defaultIndex] = unmatchedFlow;
        List<ValueFlowState> exits = [];
        ValueFlowState? fallthroughFlow = null;
        TypeInfo? fallthroughType = null;
        _switchDepth++;
        var flowTarget = new ValueFlowTarget(null, false, true, null);
        _valueFlowTargets.Push(flowTarget);
        try
        {
            for (int index = 0; index < switchStmt.Clauses.Count; index++)
            {
                var clause = switchStmt.Clauses[index];
                RestoreValueFlow(entries[index]);
                TypeInfo? entryType = null;
                if (discriminatedVariable is not null && discriminantProperty is not null &&
                    ReferenceEquals(_environment.GetValueBinding(discriminatedVariable.Name.Lexeme), discriminatedBinding))
                {
                    entryType = _environment.Get(discriminatedVariable.Name.Lexeme);
                    if (canNarrow[index] && clause.Value is Expr.Literal { Value: string literalValue })
                    {
                        (_, var narrowed, _) = AnalyzeDiscriminatedUnionGuard(
                            discriminatedVariable.Name.Lexeme, discriminantProperty.Lexeme, literalValue);
                        entryType = narrowed ?? entryType;
                    }
                    else if (clause.IsDefault && canNarrowUnmatched && entryType is TypeInfo.Union union)
                    {
                        var tested = switchStmt.Clauses.Select(candidate => candidate.Value)
                            .OfType<Expr.Literal>().Select(literal => literal.Value)
                            .OfType<string>().ToHashSet(StringComparer.Ordinal);
                        entryType = UnionTypeofProjections(union.FlattenedTypes.Where(member =>
                            GetDiscriminantPropertyType(member, discriminantProperty.Lexeme) is not TypeInfo.StringLiteral literal ||
                            !tested.Contains(literal.Value)));
                    }
                }
                if (fallthroughFlow is not null)
                    JoinValueFlow(incomingFlow, entries[index], fallthroughFlow);
                if (discriminatedVariable is not null && entryType is not null)
                    _environment.Define(discriminatedVariable.Name.Lexeme, fallthroughType is null
                        ? entryType : CreateUnion(entryType, fallthroughType));

                CheckStmtList(clause.Body);
                bool exitsClause = !StatementCompletionAnalysis.CanCompleteNormally(clause.Body);
                fallthroughFlow = exitsClause ? null : CaptureValueFlow();
                fallthroughType = !exitsClause && entryType is not null
                    ? _environment.Get(discriminatedVariable!.Name.Lexeme) : null;
            }
            if (fallthroughFlow is not null) exits.Add(fallthroughFlow);
            if (defaultIndex < 0) exits.Add(unmatchedFlow);
        }
        finally
        {
            _switchDepth--;
            _valueFlowTargets.Pop();
        }
        exits.AddRange(flowTarget.Breaks);
        JoinValueFlow(incomingFlow, exits.ToArray());
        PublishInheritedValueFlow(switchEnvironment, enclosingEnvironment);
    }

    private void CheckTryCatch(Stmt.TryCatch tryCatch)
    {
        var incomingFlow = CaptureValueFlow();
        var tryWrites = new ValueFlowTryWrites(_definiteAssignmentStack.Count, incomingFlow);
        // Check try block
        {
            using var editorScope = EnterEditorLexicalScope(tryCatch, keyword: TokenType.TRY);
            _valueFlowTryWrites.Push(tryWrites);
            try { CheckBlock(tryCatch.TryBlock, new TypeEnvironment(_environment)); }
            finally
            {
                _valueFlowTryWrites.Pop();
                if (_valueFlowTryWrites.TryPeek(out var parentWrites) && parentWrites.Depth == tryWrites.Depth)
                    foreach (var binding in tryWrites.Writes.Values) AddTryValueWrite(parentWrites, binding);
            }
        }
        var tryExit = CaptureValueFlow();

        // TS1196: a catch-binding annotation must be exactly 'any' or 'unknown'.
        // The parser accepts any annotation (#215); the restriction is a
        // checker diagnostic, matching tsc — never a parse error.
        if (tryCatch.CatchParamType is { } catchAnnotation
            && catchAnnotation != "any" && catchAnnotation != "unknown")
        {
            throw new TypeOperationException(
                "Catch clause variable type annotation must be 'any' or 'unknown' if specified.",
                tryCatch.CatchParam?.Line,
                tsCode: "TS1196");
        }

        // Check catch block with its parameter in scope
        if (tryCatch.CatchBlock != null)
        {
            bool tryTerminates = tryCatch.TryBlock.Count > 0 && AlwaysTerminates(tryCatch.TryBlock[^1]);
            JoinValueFlow(incomingFlow, ExceptionValueFlow(tryWrites));
            using var editorScope = EnterEditorLexicalScope(tryCatch, keyword: TokenType.CATCH);
            TypeEnvironment catchEnv = new(_environment);
            if (tryCatch.CatchParam is not null)
            {
                TypeInfo catchType = tryCatch.CatchParamType == "unknown" ||
                    (tryCatch.CatchParamType is null && Options.UseUnknownInCatchVariables)
                    ? TypeInfo.Unknown.Shared : TypeInfo.Any.Shared;
                DeclareValue(
                    catchEnv,
                    tryCatch.CatchParam,
                    catchType);
                RecordDeclaredType(catchEnv, tryCatch.CatchParam.Lexeme, catchType);
            }

            CheckBlock(tryCatch.CatchBlock, catchEnv);
            var catchExit = CaptureValueFlow();
            if (tryTerminates) JoinValueFlow(incomingFlow, catchExit);
            else if (tryCatch.CatchBlock.Count > 0 && AlwaysTerminates(tryCatch.CatchBlock[^1]))
                JoinValueFlow(incomingFlow, tryExit);
            else JoinValueFlow(incomingFlow, tryExit, catchExit);
        }

        // Check finally block
        if (tryCatch.FinallyBlock != null)
        {
            using var editorScope = EnterEditorLexicalScope(tryCatch, keyword: TokenType.FINALLY);
            CheckBlock(tryCatch.FinallyBlock, new TypeEnvironment(_environment));
        }
    }
}
