using SharpTS.Parsing;
using SharpTS.TypeSystem.Narrowing;

namespace SharpTS.TypeSystem.ControlFlow;

/// <summary>
/// Represents a basic block in the control flow graph.
/// A basic block is a sequence of statements with:
/// - Single entry point (first statement)
/// - Single exit point (last statement)
/// - No branches except at the end
/// </summary>
public sealed class BasicBlock
{
    private static int _nextId = 0;

    /// <summary>
    /// Unique identifier for this block.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Human-readable label for debugging.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// The statements in this block.
    /// </summary>
    public List<Stmt> Statements { get; } = [];

    /// <summary>
    /// Edges coming into this block.
    /// </summary>
    public List<FlowEdge> Predecessors { get; } = [];

    /// <summary>
    /// Edges going out of this block.
    /// </summary>
    public List<FlowEdge> Successors { get; } = [];

    /// <summary>
    /// The narrowing context at the entry of this block.
    /// Set during narrowing analysis.
    /// </summary>
    public NarrowingContext? EntryContext { get; set; }

    /// <summary>
    /// The narrowing context at the exit of this block.
    /// Set during narrowing analysis.
    /// </summary>
    public NarrowingContext? ExitContext { get; set; }

    /// <summary>
    /// Whether this block has been visited during analysis.
    /// Used for fixed-point iteration.
    /// </summary>
    public bool Visited { get; set; }

    /// <summary>
    /// Whether this block's narrowing state has changed in the current iteration.
    /// Used for fixed-point convergence.
    /// </summary>
    public bool Changed { get; set; }

    public BasicBlock(string? label = null)
    {
        Id = Interlocked.Increment(ref _nextId);
        Label = label ?? $"BB{Id}";
    }

    /// <summary>
    /// Resets the ID counter. Used for testing.
    /// </summary>
    public static void ResetIdCounter()
    {
        Interlocked.Exchange(ref _nextId, 0);
    }

    /// <summary>
    /// Adds a statement to this block.
    /// </summary>
    public void AddStatement(Stmt stmt)
    {
        Statements.Add(stmt);
    }

    /// <summary>
    /// Checks if this block is empty (no statements).
    /// </summary>
    public bool IsEmpty => Statements.Count == 0;

    /// <summary>
    /// Gets the last statement in the block, if any.
    /// </summary>
    public Stmt? LastStatement => Statements.Count > 0 ? Statements[^1] : null;

    /// <summary>
    /// Checks if this block ends with a terminating statement (return, throw, break, continue).
    /// </summary>
    public bool IsTerminating => LastStatement is Stmt.Return or Stmt.Throw or Stmt.Break or Stmt.Continue;

    public override string ToString()
    {
        var stmtInfo = Statements.Count switch
        {
            0 => "empty",
            1 => "1 stmt",
            _ => $"{Statements.Count} stmts"
        };
        return $"BasicBlock({Label}, {stmtInfo}, {Predecessors.Count} preds, {Successors.Count} succs)";
    }
}
