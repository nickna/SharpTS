using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Result of analyzing an if statement's condition for dead code.
/// </summary>
public enum IfBranchResult
{
    /// <summary>Condition is indeterminate - both branches are reachable.</summary>
    BothReachable,

    /// <summary>Condition is always true - only then branch is reachable.</summary>
    OnlyThenReachable,

    /// <summary>Condition is always false - only else branch is reachable.</summary>
    OnlyElseReachable,
}

/// <summary>
/// Analysis result for a switch statement.
/// </summary>
/// <param name="DeadCaseIndices">Indices of case clauses that are unreachable.</param>
/// <param name="DefaultIsUnreachable">True if the default case is unreachable (exhaustive switch).</param>
/// <param name="IsExhaustive">True if all union members are covered by cases.</param>
public record SwitchAnalysis(
    HashSet<int> DeadCaseIndices,
    bool DefaultIsUnreachable,
    bool IsExhaustive
);

/// <summary>
/// Contains dead code analysis results for use by ILEmitter.
/// </summary>
/// <remarks>
/// Produced by <see cref="DeadCodeAnalyzer"/> and consumed by <see cref="ILEmitter"/>
/// to skip emitting unreachable code. Uses reference equality for AST nodes.
/// </remarks>
public class DeadCodeInfo
{
    private readonly HashSet<Stmt> _deadStatements = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.If, IfBranchResult> _ifResults = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Switch, SwitchAnalysis> _switchResults = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Mark a statement as dead (unreachable).
    /// </summary>
    public void MarkDead(Stmt stmt) => _deadStatements.Add(stmt);

    /// <summary>
    /// Check if a statement is dead (unreachable).
    /// </summary>
    public bool IsDead(Stmt stmt) => _deadStatements.Contains(stmt);

    /// <summary>
    /// Set the branch result for an if statement.
    /// </summary>
    public void SetIfResult(Stmt.If ifStmt, IfBranchResult result) => _ifResults[ifStmt] = result;

    /// <summary>
    /// Get the branch result for an if statement.
    /// </summary>
    public IfBranchResult GetIfResult(Stmt.If ifStmt) =>
        _ifResults.TryGetValue(ifStmt, out var r) ? r : IfBranchResult.BothReachable;

    /// <summary>
    /// Set the analysis result for a switch statement.
    /// </summary>
    public void SetSwitchResult(Stmt.Switch sw, SwitchAnalysis analysis) => _switchResults[sw] = analysis;

    /// <summary>
    /// Get the analysis result for a switch statement.
    /// </summary>
    public SwitchAnalysis? GetSwitchResult(Stmt.Switch sw) =>
        _switchResults.TryGetValue(sw, out var a) ? a : null;
}
