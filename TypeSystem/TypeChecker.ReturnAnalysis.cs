using SharpTS.Parsing;

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
    {
        // Must have a default case to be exhaustive
        if (switchStmt.DefaultBody == null)
            return false;

        // Check if default body definitely returns
        if (!DoesBlockDefinitelyReturn(switchStmt.DefaultBody))
            return false;

        // Check if all case bodies definitely return
        // Note: this is a simplified check that doesn't handle fall-through correctly
        foreach (var caseItem in switchStmt.Cases)
        {
            if (!DoesBlockDefinitelyReturn(caseItem.Body))
            {
                // Check if the case falls through to another returning case
                // For simplicity, we require each case to return or fall through to default
                // A more sophisticated analysis would track fall-through
                return false;
            }
        }

        return true;
    }

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
