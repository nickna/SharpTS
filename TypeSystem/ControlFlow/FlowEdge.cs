using SharpTS.Parsing;

namespace SharpTS.TypeSystem.ControlFlow;

/// <summary>
/// Represents the kind of control flow edge between basic blocks.
/// </summary>
public enum FlowEdgeKind
{
    /// <summary>Normal sequential flow.</summary>
    Unconditional,

    /// <summary>Edge taken when a condition is true.</summary>
    ConditionalTrue,

    /// <summary>Edge taken when a condition is false.</summary>
    ConditionalFalse,

    /// <summary>Edge from a return statement to the exit block.</summary>
    Return,

    /// <summary>Edge from a throw statement to exception handling.</summary>
    Throw,

    /// <summary>Edge from a break statement out of a loop/switch.</summary>
    Break,

    /// <summary>Edge from a continue statement back to loop header.</summary>
    Continue,

    /// <summary>Back edge in a loop (from loop end to loop header).</summary>
    LoopBack
}

/// <summary>
/// Represents an edge in the control flow graph.
/// </summary>
/// <param name="From">The source basic block.</param>
/// <param name="To">The target basic block.</param>
/// <param name="Kind">The kind of control flow edge.</param>
/// <param name="Condition">For conditional edges, the condition expression.</param>
/// <param name="ConditionIsTrue">For conditional edges, whether the condition is true on this edge.</param>
public sealed record FlowEdge(
    BasicBlock From,
    BasicBlock To,
    FlowEdgeKind Kind,
    Expr? Condition = null,
    bool ConditionIsTrue = true)
{
    public override string ToString()
    {
        var condInfo = Condition != null ? $" ({(ConditionIsTrue ? "true" : "false")})" : "";
        return $"Edge({From.Id} -> {To.Id}, {Kind}{condInfo})";
    }
}
