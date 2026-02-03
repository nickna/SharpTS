using SharpTS.Parsing;
using SharpTS.TypeSystem.Narrowing;

namespace SharpTS.TypeSystem.ControlFlow;

/// <summary>
/// Propagates type narrowings through a control flow graph using dataflow analysis.
/// </summary>
public sealed class NarrowingPropagator
{
    private readonly ControlFlowGraph _cfg;
    private readonly Func<Expr, (NarrowingPath? Path, TypeInfo? NarrowedType, TypeInfo? ExcludedType)> _analyzeTypeGuard;
    private readonly Func<NarrowingPath, TypeInfo?> _getBaseType;

    /// <summary>
    /// Creates a new narrowing propagator.
    /// </summary>
    /// <param name="cfg">The control flow graph to analyze.</param>
    /// <param name="analyzeTypeGuard">Function to analyze type guards in conditions.</param>
    /// <param name="getBaseType">Function to get the base type for a narrowing path.</param>
    public NarrowingPropagator(
        ControlFlowGraph cfg,
        Func<Expr, (NarrowingPath? Path, TypeInfo? NarrowedType, TypeInfo? ExcludedType)> analyzeTypeGuard,
        Func<NarrowingPath, TypeInfo?> getBaseType)
    {
        _cfg = cfg;
        _analyzeTypeGuard = analyzeTypeGuard;
        _getBaseType = getBaseType;
    }

    /// <summary>
    /// Propagates narrowings through the CFG using a forward dataflow analysis.
    /// </summary>
    public void Propagate()
    {
        _cfg.ResetAnalysisState();

        // Initialize entry block with empty context
        _cfg.Entry.EntryContext = NarrowingContext.Empty;

        // Use a worklist algorithm for fixed-point iteration
        var worklist = new Queue<BasicBlock>();
        foreach (var block in _cfg.GetReversePostorder())
        {
            worklist.Enqueue(block);
        }

        while (worklist.Count > 0)
        {
            var block = worklist.Dequeue();
            var oldExit = block.ExitContext;

            // Compute entry context by merging predecessors
            var entryContext = ComputeEntryContext(block);
            block.EntryContext = entryContext;

            // Compute exit context by applying transfer function
            var exitContext = TransferFunction(block, entryContext);
            block.ExitContext = exitContext;

            // If exit context changed, add successors to worklist
            if (!ContextEquals(oldExit, exitContext))
            {
                foreach (var edge in block.Successors)
                {
                    if (!worklist.Contains(edge.To))
                    {
                        worklist.Enqueue(edge.To);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Computes the entry context for a block by merging predecessor exit contexts.
    /// For conditional edges, applies the appropriate narrowing from the condition.
    /// </summary>
    private NarrowingContext ComputeEntryContext(BasicBlock block)
    {
        if (block == _cfg.Entry)
        {
            return NarrowingContext.Empty;
        }

        var predecessorContexts = new List<NarrowingContext>();

        foreach (var edge in block.Predecessors)
        {
            var predContext = edge.From.ExitContext ?? NarrowingContext.Empty;

            // For conditional edges, apply the narrowing from the condition
            if (edge.Condition != null &&
                (edge.Kind == FlowEdgeKind.ConditionalTrue || edge.Kind == FlowEdgeKind.ConditionalFalse))
            {
                var guard = _analyzeTypeGuard(edge.Condition);
                if (guard.Path != null)
                {
                    var narrowedType = edge.ConditionIsTrue ? guard.NarrowedType : guard.ExcludedType;
                    if (narrowedType != null)
                    {
                        predContext = predContext.WithNarrowing(guard.Path, narrowedType);
                    }
                }
            }

            predecessorContexts.Add(predContext);
        }

        if (predecessorContexts.Count == 0)
        {
            return NarrowingContext.Empty;
        }

        if (predecessorContexts.Count == 1)
        {
            return predecessorContexts[0];
        }

        // Merge all predecessor contexts
        var result = predecessorContexts[0];
        for (int i = 1; i < predecessorContexts.Count; i++)
        {
            result = NarrowingContext.Merge(result, predecessorContexts[i]);
        }

        return result;
    }

    /// <summary>
    /// Applies the transfer function to compute exit context from entry context.
    /// Handles assignments that invalidate narrowings.
    /// </summary>
    private NarrowingContext TransferFunction(BasicBlock block, NarrowingContext entryContext)
    {
        var context = entryContext;

        foreach (var stmt in block.Statements)
        {
            context = ProcessStatementForNarrowing(stmt, context);
        }

        return context;
    }

    /// <summary>
    /// Processes a statement and returns the updated narrowing context.
    /// </summary>
    private NarrowingContext ProcessStatementForNarrowing(Stmt stmt, NarrowingContext context)
    {
        switch (stmt)
        {
            case Stmt.Expression exprStmt:
                return ProcessExpressionForNarrowing(exprStmt.Expr, context);

            case Stmt.Var varStmt when varStmt.Initializer != null:
                context = ProcessExpressionForNarrowing(varStmt.Initializer, context);
                // Variable declaration with initializer - could invalidate if reassigning
                var varPath = new NarrowingPath.Variable(varStmt.Name.Lexeme);
                return context.Invalidate(varPath);

            case Stmt.Const constStmt:
                return ProcessExpressionForNarrowing(constStmt.Initializer, context);

            default:
                return context;
        }
    }

    /// <summary>
    /// Processes an expression and returns the updated narrowing context.
    /// Handles assignments that invalidate narrowings.
    /// </summary>
    private NarrowingContext ProcessExpressionForNarrowing(Expr expr, NarrowingContext context)
    {
        switch (expr)
        {
            case Expr.Assign assign:
                var varPath = new NarrowingPath.Variable(assign.Name.Lexeme);
                return context.Invalidate(varPath);

            case Expr.Set set:
                var basePath = NarrowingPathExtractor.TryExtract(set.Object);
                if (basePath != null)
                {
                    var propPath = new NarrowingPath.PropertyAccess(basePath, set.Name.Lexeme);
                    return context.Invalidate(propPath);
                }
                return context;

            case Expr.SetIndex setIndex:
                var indexBasePath = NarrowingPathExtractor.TryExtract(setIndex.Object);
                if (indexBasePath != null)
                {
                    if (setIndex.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
                    {
                        var elementPath = new NarrowingPath.ElementAccess(indexBasePath, (int)d);
                        return context.Invalidate(elementPath);
                    }
                    // Computed index - invalidate entire array/object
                    return context.Invalidate(indexBasePath);
                }
                return context;

            case Expr.CompoundAssign compound:
                var compoundPath = new NarrowingPath.Variable(compound.Name.Lexeme);
                return context.Invalidate(compoundPath);

            case Expr.CompoundSet compoundSet:
                var compoundBasePath = NarrowingPathExtractor.TryExtract(compoundSet.Object);
                if (compoundBasePath != null)
                {
                    var compoundPropPath = new NarrowingPath.PropertyAccess(compoundBasePath, compoundSet.Name.Lexeme);
                    return context.Invalidate(compoundPropPath);
                }
                return context;

            case Expr.PrefixIncrement prefix:
                return ProcessExpressionForNarrowing(prefix.Operand, context);

            case Expr.PostfixIncrement postfix:
                return ProcessExpressionForNarrowing(postfix.Operand, context);

            default:
                return context;
        }
    }

    /// <summary>
    /// Checks if two contexts are equal (for fixed-point detection).
    /// </summary>
    private static bool ContextEquals(NarrowingContext? a, NarrowingContext? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.IsEmpty && b.IsEmpty) return true;
        if (a.IsEmpty || b.IsEmpty) return false;

        var aDict = a.Narrowings.ToDictionary(kv => kv.Key, kv => kv.Value);
        var bDict = b.Narrowings.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (aDict.Count != bDict.Count) return false;

        foreach (var (path, type) in aDict)
        {
            if (!bDict.TryGetValue(path, out var otherType) || !type.Equals(otherType))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the narrowing context at a specific statement.
    /// </summary>
    public NarrowingContext? GetContextAtStatement(Stmt stmt)
    {
        foreach (var block in _cfg.Blocks)
        {
            var context = block.EntryContext ?? NarrowingContext.Empty;
            foreach (var blockStmt in block.Statements)
            {
                if (ReferenceEquals(blockStmt, stmt))
                {
                    return context;
                }
                context = ProcessStatementForNarrowing(blockStmt, context);
            }
        }
        return null;
    }
}
