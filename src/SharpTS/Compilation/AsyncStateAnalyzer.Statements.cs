using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class AsyncStateAnalyzer
{
    #region Statement Visitor Overrides

    protected override void VisitVar(Stmt.Var stmt)
    {
        // Track variable declaration under its (possibly disambiguated) storage name (#766).
        var name = StorageName(stmt, stmt.Name.Lexeme);
        _declaredVariables.Add(name);

        base.VisitVar(stmt);
    }

    protected override void VisitConst(Stmt.Const stmt)
    {
        // Track const variable declaration under its (possibly disambiguated) storage name (#766).
        var name = StorageName(stmt, stmt.Name.Lexeme);
        _declaredVariables.Add(name);

        base.VisitConst(stmt);
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        // Loop variable is declared and potentially survives await
        _declaredVariables.Add(stmt.Variable.Lexeme);

        if (stmt.IsAsync)
        {
            // `for await…of` suspends on the iterator protocol: it awaits iterator.next() each
            // iteration and iterator.return() on early exit. Each needs a reserved state (resume
            // label + awaiter field), matching the two inline awaits EmitForAwaitOf emits (#631).
            // Allocation order mirrors emission order: the iterable is evaluated before the loop, so
            // its awaits come first; the next() await before the body; the return() await after.
            Visit(stmt.Iterable);
            RecordAwaitPoint(null);   // iterator.next() — awaited at the loop head
            Visit(stmt.Body);
            RecordAwaitPoint(null);   // iterator.return() — awaited in the break cleanup
            return;
        }

        base.VisitForOf(stmt);
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        // Loop variable is declared and potentially survives await
        _declaredVariables.Add(stmt.Variable.Lexeme);

        base.VisitForIn(stmt);
    }

    protected override void VisitFor(Stmt.For stmt)
    {
        // Visit initializer first (may declare loop variable)
        if (stmt.Initializer != null)
            Visit(stmt.Initializer);

        int awaitCountAtLoopHead = _awaitPoints.Count;

        // Track variables used in condition and increment
        if (stmt.Condition != null)
            Visit(stmt.Condition);

        // Visit body for await detection
        Visit(stmt.Body);

        if (stmt.Increment != null)
            Visit(stmt.Increment);

        // A suspension in the loop condition/body/increment creates a backedge
        // into a later MoveNext invocation. The ordinary forward walk sees the
        // condition before that suspension, so a local used only by the next
        // condition evaluation would otherwise remain an IL local and reset on
        // resume (#1443). Treat every directly referenced function local in the
        // repeating region as live across the suspension. This is conservative
        // but preserves lexical identity through the existing rename map.
        if (_awaitPoints.Count > awaitCountAtLoopHead)
        {
            var usages = new LoopBackedgeVariableCollector(_renames);
            if (stmt.Condition != null)
                usages.Visit(stmt.Condition);
            usages.Visit(stmt.Body);
            if (stmt.Increment != null)
                usages.Visit(stmt.Increment);

            foreach (var name in usages.Names)
                if (_declaredVariables.Contains(name))
                    _variablesUsedAfterAwait.Add(name);
        }
    }

    /// <summary>
    /// Collects direct state-machine variable references from a repeating loop
    /// region without recording suspension points a second time. Nested
    /// callables have their own state and are handled by the normal capture
    /// analysis, so their bodies are deliberately skipped.
    /// </summary>
    private sealed class LoopBackedgeVariableCollector(
        IReadOnlyDictionary<object, string> renames) : AstVisitorBase
    {
        public HashSet<string> Names { get; } = [];

        private void Record(object node, string lexeme) =>
            Names.Add(renames.TryGetValue(node, out var renamed) ? renamed : lexeme);

        protected override void VisitVariable(Expr.Variable expr) =>
            Record(expr, expr.Name.Lexeme);

        protected override void VisitAssign(Expr.Assign expr)
        {
            Record(expr, expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Record(expr, expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
        {
            Record(expr, expr.Name.Lexeme);
            base.VisitLogicalAssign(expr);
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
        protected override void VisitFunction(Stmt.Function stmt) { }
        protected override void VisitClass(Stmt.Class stmt) { }
        protected override void VisitClassExpr(Expr.ClassExpr expr) { }
    }

    protected override void VisitReturn(Stmt.Return stmt)
    {
        base.VisitReturn(stmt);

        // A private async method is emitted as Task<object>. When an async function returns
        // that call directly, ECMAScript promise resolution adopts the returned promise rather
        // than resolving with the host Task as an ordinary object. Reserve a suspension point
        // for the matching implicit await in AsyncFunctionMoveNextEmitter.
        if (stmt.Value is Expr.CallPrivate)
            RecordAwaitPoint(null);
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        _hasTryCatch = true;
        AnalyzeTryCatchWithTracking(stmt);
        // Don't call base - AnalyzeTryCatchWithTracking handles all traversal
    }

    // Don't traverse into nested declarations - they don't affect our analysis
    protected override void VisitFunction(Stmt.Function stmt) { }
    protected override void VisitClass(Stmt.Class stmt) { }
    protected override void VisitInterface(Stmt.Interface stmt) { }
    protected override void VisitTypeAlias(Stmt.TypeAlias stmt) { }
    protected override void VisitEnum(Stmt.Enum stmt) { }
    protected override void VisitNamespace(Stmt.Namespace stmt) { }

    #endregion

    private void AnalyzeTryCatchWithTracking(Stmt.TryCatch t)
    {
        // Assign an ID to this try block
        int tryId = _tryBlockCounter++;
        int? parentTryId = _currentTryBlockId;

        // Push try block context
        if (_currentTryBlockId.HasValue)
            _tryBlockIdStack.Push(_currentTryBlockId.Value);
        _currentTryBlockId = tryId;
        _currentTryBlockDepth++;

        // Record try block info (await flags will be updated during analysis)
        _tryBlocks.Add(new TryBlockInfo(
            tryId,
            t,
            HasAwaitsInTry: false,  // Will be updated
            HasAwaitsInCatch: false,
            HasAwaitsInFinally: false,
            ParentTryId: parentTryId
        ));

        // Analyze try block
        var previousRegion = _currentTryRegion;
        _currentTryRegion = TryRegion.Try;
        foreach (var ts in t.TryBlock)
            Visit(ts);

        // Analyze catch block
        if (t.CatchBlock != null)
        {
            _currentTryRegion = TryRegion.Catch;
            if (t.CatchParam != null)
            {
                _declaredVariables.Add(t.CatchParam.Lexeme);
                _catchParameters.Add(t.CatchParam.Lexeme);  // Track as catch param (should not be hoisted)
            }
            foreach (var cs in t.CatchBlock)
                Visit(cs);
        }

        // Analyze finally block
        if (t.FinallyBlock != null)
        {
            _currentTryRegion = TryRegion.Finally;
            foreach (var fs in t.FinallyBlock)
                Visit(fs);
        }

        // Restore context
        _currentTryRegion = previousRegion;
        _currentTryBlockDepth--;
        if (_tryBlockIdStack.Count > 0)
            _currentTryBlockId = _tryBlockIdStack.Pop();
        else
            _currentTryBlockId = null;
    }
}
