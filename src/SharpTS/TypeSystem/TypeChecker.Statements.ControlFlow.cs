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
            CheckStmtList(statements);
        }
        finally
        {
            _environment = previous;
        }
    }

    private void CheckSwitch(Stmt.Switch switchStmt)
    {
        CheckExpr(switchStmt.Subject);

        _switchDepth++;
        try
        {
            foreach (var caseItem in switchStmt.Cases)
            {
                CheckExpr(caseItem.Value);
                foreach (var stmt in caseItem.Body)
                {
                    CheckStmt(stmt);
                }
            }

            if (switchStmt.DefaultBody != null)
            {
                foreach (var stmt in switchStmt.DefaultBody)
                {
                    CheckStmt(stmt);
                }
            }
        }
        finally
        {
            _switchDepth--;
        }
    }

    private void CheckTryCatch(Stmt.TryCatch tryCatch)
    {
        // Check try block
        foreach (var stmt in tryCatch.TryBlock)
        {
            CheckStmt(stmt);
        }

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
        if (tryCatch.CatchBlock != null && tryCatch.CatchParam != null)
        {
            TypeEnvironment catchEnv = new(_environment);
            DeclareValue(
                catchEnv,
                tryCatch.CatchParam,
                tryCatch.CatchParamType == "unknown" ||
                    (tryCatch.CatchParamType is null && Options.UseUnknownInCatchVariables)
                    ? TypeInfo.Unknown.Shared
                    : TypeInfo.Any.Shared);

            TypeEnvironment prevEnv = _environment;
            _environment = catchEnv;
            try
            {
                foreach (var stmt in tryCatch.CatchBlock)
                {
                    CheckStmt(stmt);
                }
            }
            finally
            {
                _environment = prevEnv;
            }
        }

        // Check finally block
        if (tryCatch.FinallyBlock != null)
        {
            foreach (var stmt in tryCatch.FinallyBlock)
            {
                CheckStmt(stmt);
            }
        }
    }
}
