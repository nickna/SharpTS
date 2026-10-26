using SharpTS.Parsing;
using SharpTS.TypeSystem.ControlFlow;

namespace SharpTS.TypeSystem;

/// <summary>
/// Return statement analysis for ensuring non-void functions return a value on all code paths.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Checks if a list of statements definitely returns (or throws) on all code paths.
    /// </summary>
    private bool DoesBlockDefinitelyReturn(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (DoesStatementDefinitelyReturn(stmt))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if a single statement definitely returns (or throws) on all code paths.
    /// </summary>
    private bool DoesStatementDefinitelyReturn(Stmt stmt)
    {
        return stmt switch
        {
            // Return statement with a value definitely returns
            Stmt.Return ret when ret.Value != null => true,

            // Throw statement definitely terminates
            Stmt.Throw => true,

            // Block definitely returns if any statement in it definitely returns
            Stmt.Block block => DoesBlockDefinitelyReturn(block.Statements),

            // If-else definitely returns if both branches definitely return
            Stmt.If ifStmt => ifStmt.ElseBranch != null &&
                              DoesStatementDefinitelyReturn(ifStmt.ThenBranch) &&
                              DoesStatementDefinitelyReturn(ifStmt.ElseBranch),

            // Switch definitely returns if it has a default and all paths return
            Stmt.Switch switchStmt => DoesSwitchDefinitelyReturn(switchStmt),

            // Try-catch-finally: definitely returns if try and all catch blocks return,
            // OR if finally block returns (which would be unusual but valid)
            Stmt.TryCatch tryCatch => DoesTryCatchDefinitelyReturn(tryCatch),

            // Expression statements, variable declarations, etc. don't return
            _ => false
        };
    }

    /// <summary>
    /// Checks if a switch statement definitely returns on all code paths.
    /// </summary>
    private bool DoesSwitchDefinitelyReturn(Stmt.Switch switchStmt)
        => StatementCompletionAnalysis.SwitchDefinitelyReturns(switchStmt, allowEmptyReturn: false);

    /// <summary>
    /// Checks if a try-catch-finally statement definitely returns on all code paths.
    /// </summary>
    private bool DoesTryCatchDefinitelyReturn(Stmt.TryCatch tryCatch)
    {
        // If finally returns, the whole thing returns (though this is unusual)
        if (tryCatch.FinallyBlock != null && DoesBlockDefinitelyReturn(tryCatch.FinallyBlock))
            return true;

        bool tryReturns = DoesBlockDefinitelyReturn(tryCatch.TryBlock);

        // If there's no catch block (just try-finally), the try block returning is sufficient
        // because if no exception is thrown, the return will execute after finally runs
        if (tryCatch.CatchBlock == null)
        {
            return tryReturns;
        }

        // With a catch block, both try and catch must return to guarantee a return
        bool catchReturns = DoesBlockDefinitelyReturn(tryCatch.CatchBlock);
        return tryReturns && catchReturns;
    }
}
