namespace SharpTS.TypeSystem.ControlFlow;

/// <summary>
/// Represents a control flow graph for a function or block of statements.
/// </summary>
public sealed class ControlFlowGraph
{
    /// <summary>
    /// The entry block of the CFG. All control flow starts here.
    /// </summary>
    public BasicBlock Entry { get; }

    /// <summary>
    /// The exit block of the CFG. All normal termination paths end here.
    /// </summary>
    public BasicBlock Exit { get; }

    /// <summary>
    /// All basic blocks in the CFG.
    /// </summary>
    public IReadOnlyList<BasicBlock> Blocks { get; }

    /// <summary>
    /// All edges in the CFG.
    /// </summary>
    public IReadOnlyList<FlowEdge> Edges { get; }

    public ControlFlowGraph(
        BasicBlock entry,
        BasicBlock exit,
        IReadOnlyList<BasicBlock> blocks,
        IReadOnlyList<FlowEdge> edges)
    {
        Entry = entry;
        Exit = exit;
        Blocks = blocks;
        Edges = edges;
    }

    /// <summary>
    /// Gets all blocks in reverse postorder (topological order for acyclic graphs).
    /// This ordering is useful for forward dataflow analysis.
    /// </summary>
    public IEnumerable<BasicBlock> GetReversePostorder()
    {
        var visited = new HashSet<BasicBlock>();
        var postorder = new List<BasicBlock>();

        void Visit(BasicBlock block)
        {
            if (!visited.Add(block)) return;

            foreach (var edge in block.Successors)
            {
                // Skip back edges to avoid infinite recursion
                if (edge.Kind != FlowEdgeKind.LoopBack)
                {
                    Visit(edge.To);
                }
            }

            postorder.Add(block);
        }

        Visit(Entry);

        // Reverse postorder
        postorder.Reverse();
        return postorder;
    }

    /// <summary>
    /// Gets all blocks that are loop headers (have incoming back edges).
    /// </summary>
    public IEnumerable<BasicBlock> GetLoopHeaders()
    {
        return Blocks.Where(b => b.Predecessors.Any(e => e.Kind == FlowEdgeKind.LoopBack));
    }

    /// <summary>
    /// Resets all analysis state on blocks.
    /// </summary>
    public void ResetAnalysisState()
    {
        foreach (var block in Blocks)
        {
            block.EntryContext = null;
            block.ExitContext = null;
            block.Visited = false;
            block.Changed = false;
        }
    }

    public override string ToString()
    {
        return $"CFG({Blocks.Count} blocks, {Edges.Count} edges)";
    }

    /// <summary>
    /// Generates a DOT representation of the CFG for visualization.
    /// </summary>
    public string ToDot()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("digraph CFG {");
        sb.AppendLine("  node [shape=box];");

        foreach (var block in Blocks)
        {
            var label = block.Label;
            if (block == Entry) label += " (entry)";
            if (block == Exit) label += " (exit)";
            if (block.Statements.Count > 0)
            {
                label += $"\\n{block.Statements.Count} stmt(s)";
            }
            sb.AppendLine($"  {block.Id} [label=\"{label}\"];");
        }

        foreach (var edge in Edges)
        {
            var style = edge.Kind switch
            {
                FlowEdgeKind.LoopBack => " [style=dashed, color=blue]",
                FlowEdgeKind.ConditionalTrue => " [label=\"T\", color=green]",
                FlowEdgeKind.ConditionalFalse => " [label=\"F\", color=red]",
                FlowEdgeKind.Return => " [color=purple]",
                FlowEdgeKind.Throw => " [color=orange]",
                FlowEdgeKind.Break => " [color=brown]",
                FlowEdgeKind.Continue => " [style=dashed, color=brown]",
                _ => ""
            };
            sb.AppendLine($"  {edge.From.Id} -> {edge.To.Id}{style};");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
