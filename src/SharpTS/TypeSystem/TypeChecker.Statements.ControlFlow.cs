using SharpTS.Parsing;
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
        var incomingFlow = CaptureValueFlow();
        List<ValueFlowState> exits = [];
        ValueFlowState? fallthroughFlow = null;
        using var editorScope = EnterEditorLexicalScope(switchStmt);

        Expr.Variable? discriminatedVariable = null;
        Token? discriminantProperty = null;
        if (switchStmt.Subject is Expr.Get
            {
                Object: Expr.Variable variable,
                Name: var property
            })
        {
            discriminatedVariable = variable;
            discriminantProperty = property;
        }

        TypeInfo? fallthroughType = null;

        _switchDepth++;
        var flowTarget = new ValueFlowTarget(null, false, true, null);
        _valueFlowTargets.Push(flowTarget);
        try
        {
            foreach (var caseItem in switchStmt.Cases)
            {
                JoinValueFlow(incomingFlow, incomingFlow, fallthroughFlow ?? incomingFlow);
                CheckExpr(caseItem.Value);

                TypeInfo? caseType = null;
                if (discriminatedVariable is not null &&
                    discriminantProperty is not null &&
                    caseItem.Value is Expr.Literal { Value: string literalValue })
                {
                    (_, caseType, _) = AnalyzeDiscriminatedUnionGuard(
                        discriminatedVariable.Name.Lexeme,
                        discriminantProperty.Lexeme,
                        literalValue);
                }

                TypeInfo? effectiveCaseType = (fallthroughType, caseType) switch
                {
                    (null, { } current) => current,
                    ({ } previous, null) => previous,
                    ({ } previous, { } current) => new TypeInfo.Union([previous, current]),
                    _ => null,
                };
                var caseEnvironment = new TypeEnvironment(_environment);
                if (discriminatedVariable is not null && effectiveCaseType is not null)
                    caseEnvironment.Define(discriminatedVariable.Name.Lexeme, effectiveCaseType);

                using (new EnvironmentScope(this, caseEnvironment))
                {
                    CheckStmtList(caseItem.Body);
                    fallthroughFlow = CaptureValueFlow();
                }

                bool exitsCase = caseItem.Body.LastOrDefault() is Stmt.Break or Stmt.Continue ||
                    caseItem.Body.Count > 0 && AlwaysTerminates(caseItem.Body[^1]);
                fallthroughType = exitsCase ? null : effectiveCaseType;
                if (exitsCase)
                {
                    if (caseItem.Body.LastOrDefault() is Stmt.Break or Stmt.Continue)
                        exits.Add(fallthroughFlow);
                    fallthroughFlow = null;
                }
            }

            if (switchStmt.DefaultBody != null)
            {
                JoinValueFlow(incomingFlow, incomingFlow, fallthroughFlow ?? incomingFlow);
                CheckBlock(switchStmt.DefaultBody, new TypeEnvironment(_environment));
                if (switchStmt.DefaultBody.LastOrDefault() is Stmt.Break ||
                    switchStmt.DefaultBody.Count == 0 || !AlwaysTerminates(switchStmt.DefaultBody[^1]))
                    exits.Add(CaptureValueFlow());
            }
            else
            {
                exits.Add(incomingFlow);
                if (fallthroughFlow is not null) exits.Add(fallthroughFlow);
            }
        }
        finally
        {
            _switchDepth--;
            _valueFlowTargets.Pop();
        }
        exits.AddRange(flowTarget.Breaks);
        JoinValueFlow(incomingFlow, exits.ToArray());
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
                DeclareValue(
                    catchEnv,
                    tryCatch.CatchParam,
                    tryCatch.CatchParamType == "unknown" ||
                        (tryCatch.CatchParamType is null && Options.UseUnknownInCatchVariables)
                        ? TypeInfo.Unknown.Shared
                        : TypeInfo.Any.Shared);

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
