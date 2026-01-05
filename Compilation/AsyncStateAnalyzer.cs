using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes async functions to identify await points and variables that must be hoisted
/// to the state machine struct.
/// </summary>
public class AsyncStateAnalyzer
{
    /// <summary>
    /// Represents a single await point in an async function.
    /// </summary>
    public record AwaitPoint(
        int StateNumber,
        Expr.Await AwaitExpr,
        HashSet<string> LiveVariables,
        int TryBlockDepth = 0,  // 0 = not in try, 1+ = nested try depth
        int? EnclosingTryId = null  // ID of the innermost try block containing this await
    );

    /// <summary>
    /// Represents a try/catch/finally block in an async function.
    /// </summary>
    public record TryBlockInfo(
        int TryId,
        Stmt.TryCatch TryStatement,
        bool HasAwaitsInTry,
        bool HasAwaitsInCatch,
        bool HasAwaitsInFinally,
        int? ParentTryId  // For nested try blocks
    );

    /// <summary>
    /// Information about an async arrow function found inside an async function.
    /// </summary>
    public record AsyncArrowInfo(
        Expr.ArrowFunction Arrow,
        HashSet<string> Captures,
        bool CapturesThis,
        int NestingLevel,  // 0 = direct child, 1 = inside another async arrow, etc.
        Expr.ArrowFunction? ParentArrow  // The parent async arrow (null if direct child of function)
    );

    /// <summary>
    /// Complete analysis results for an async function.
    /// </summary>
    public record AsyncFunctionAnalysis(
        int AwaitPointCount,
        List<AwaitPoint> AwaitPoints,
        HashSet<string> HoistedLocals,
        HashSet<string> HoistedParameters,
        bool HasTryCatch,
        bool UsesThis,
        List<AsyncArrowInfo> AsyncArrows,
        List<TryBlockInfo> TryBlocks = null!  // Try blocks with await tracking
    )
    {
        /// <summary>
        /// Whether any try block contains await points (requires special handling).
        /// </summary>
        public bool HasAwaitsInTryBlocks =>
            TryBlocks?.Any(t => t.HasAwaitsInTry || t.HasAwaitsInCatch || t.HasAwaitsInFinally) ?? false;
    }

    // State during analysis
    private readonly List<AwaitPoint> _awaitPoints = [];
    private readonly HashSet<string> _declaredVariables = [];
    private readonly HashSet<string> _variablesUsedAfterAwait = [];
    private readonly HashSet<string> _variablesDeclaredBeforeAwait = [];
    private readonly List<AsyncArrowInfo> _asyncArrows = [];
    private readonly List<TryBlockInfo> _tryBlocks = [];
    private int _awaitCounter = 0;
    private bool _seenAwait = false;
    private bool _hasTryCatch = false;
    private bool _usesThis = false;
    private int _asyncArrowNestingLevel = 0;
    private Expr.ArrowFunction? _currentParentArrow = null;  // Track parent for nested arrows

    // Try block tracking
    private int _tryBlockCounter = 0;
    private int _currentTryBlockDepth = 0;
    private int? _currentTryBlockId = null;
    private readonly Stack<int> _tryBlockIdStack = new();

    // Track which region (try/catch/finally) we're currently in
    private enum TryRegion { None, Try, Catch, Finally }
    private TryRegion _currentTryRegion = TryRegion.None;
    private readonly Dictionary<int, (bool InTry, bool InCatch, bool InFinally)> _tryBlockAwaitFlags = [];

    /// <summary>
    /// Analyzes an async function to determine await points and hoisted variables.
    /// </summary>
    public AsyncFunctionAnalysis Analyze(Stmt.Function func)
    {
        Reset();

        // Collect parameters as variables that need hoisting
        var parameters = new HashSet<string>();
        foreach (var param in func.Parameters)
        {
            parameters.Add(param.Name.Lexeme);
            _declaredVariables.Add(param.Name.Lexeme);
            _variablesDeclaredBeforeAwait.Add(param.Name.Lexeme);
        }

        // Analyze the function body
        if (func.Body != null)
        {
            foreach (var stmt in func.Body)
            {
                AnalyzeStmt(stmt);
            }
        }

        // Variables that need hoisting: declared before await AND used after await
        var hoistedLocals = new HashSet<string>(_variablesDeclaredBeforeAwait);
        hoistedLocals.IntersectWith(_variablesUsedAfterAwait);
        hoistedLocals.ExceptWith(parameters); // Parameters are tracked separately

        // Build TryBlockInfo list from collected data
        var tryBlocks = BuildTryBlockInfoList();

        return new AsyncFunctionAnalysis(
            AwaitPointCount: _awaitPoints.Count,
            AwaitPoints: [.. _awaitPoints],
            HoistedLocals: hoistedLocals,
            HoistedParameters: parameters,
            HasTryCatch: _hasTryCatch,
            UsesThis: _usesThis,
            AsyncArrows: [.. _asyncArrows],
            TryBlocks: tryBlocks
        );
    }

    private List<TryBlockInfo> BuildTryBlockInfoList()
    {
        var result = new List<TryBlockInfo>();
        foreach (var (tryId, flags) in _tryBlockAwaitFlags)
        {
            // Find the corresponding try statement from _tryBlocks
            var existingInfo = _tryBlocks.FirstOrDefault(t => t.TryId == tryId);
            if (existingInfo != null)
            {
                result.Add(existingInfo with
                {
                    HasAwaitsInTry = flags.InTry,
                    HasAwaitsInCatch = flags.InCatch,
                    HasAwaitsInFinally = flags.InFinally
                });
            }
        }
        // Add try blocks without awaits
        foreach (var info in _tryBlocks)
        {
            if (!result.Any(r => r.TryId == info.TryId))
            {
                result.Add(info);
            }
        }
        return result;
    }

    private void Reset()
    {
        _awaitPoints.Clear();
        _declaredVariables.Clear();
        _variablesUsedAfterAwait.Clear();
        _variablesDeclaredBeforeAwait.Clear();
        _asyncArrows.Clear();
        _tryBlocks.Clear();
        _awaitCounter = 0;
        _seenAwait = false;
        _hasTryCatch = false;
        _usesThis = false;
        _asyncArrowNestingLevel = 0;
        _currentParentArrow = null;
        _tryBlockCounter = 0;
        _currentTryBlockDepth = 0;
        _currentTryBlockId = null;
        _tryBlockIdStack.Clear();
        _currentTryRegion = TryRegion.None;
        _tryBlockAwaitFlags.Clear();
    }

    private void AnalyzeStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Var v:
                // Track variable declaration
                _declaredVariables.Add(v.Name.Lexeme);
                if (!_seenAwait)
                    _variablesDeclaredBeforeAwait.Add(v.Name.Lexeme);

                if (v.Initializer != null)
                    AnalyzeExpr(v.Initializer);
                break;

            case Stmt.Expression e:
                AnalyzeExpr(e.Expr);
                break;

            case Stmt.Return r:
                if (r.Value != null)
                    AnalyzeExpr(r.Value);
                break;

            case Stmt.If i:
                AnalyzeExpr(i.Condition);
                AnalyzeStmt(i.ThenBranch);
                if (i.ElseBranch != null)
                    AnalyzeStmt(i.ElseBranch);
                break;

            case Stmt.While w:
                AnalyzeExpr(w.Condition);
                AnalyzeStmt(w.Body);
                break;

            case Stmt.ForOf f:
                // Loop variable is declared and potentially survives await
                _declaredVariables.Add(f.Variable.Lexeme);
                if (!_seenAwait)
                    _variablesDeclaredBeforeAwait.Add(f.Variable.Lexeme);

                AnalyzeExpr(f.Iterable);
                AnalyzeStmt(f.Body);
                break;

            case Stmt.Block b:
                foreach (var s in b.Statements)
                    AnalyzeStmt(s);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    AnalyzeStmt(s);
                break;

            case Stmt.TryCatch t:
                _hasTryCatch = true;
                AnalyzeTryCatchWithTracking(t);
                break;

            case Stmt.Switch s:
                AnalyzeExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    AnalyzeExpr(c.Value);
                    foreach (var cs in c.Body)
                        AnalyzeStmt(cs);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        AnalyzeStmt(ds);
                break;

            case Stmt.Throw th:
                AnalyzeExpr(th.Value);
                break;

            case Stmt.Print p:
                AnalyzeExpr(p.Expr);
                break;

            case Stmt.LabeledStatement ls:
                AnalyzeStmt(ls.Statement);
                break;

            case Stmt.Break:
            case Stmt.Continue:
            case Stmt.Function: // Nested functions don't affect our analysis
            case Stmt.Class:
            case Stmt.Interface:
            case Stmt.TypeAlias:
            case Stmt.Enum:
                break;
        }
    }

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
            AnalyzeStmt(ts);

        // Analyze catch block
        if (t.CatchBlock != null)
        {
            _currentTryRegion = TryRegion.Catch;
            if (t.CatchParam != null)
            {
                _declaredVariables.Add(t.CatchParam.Lexeme);
                if (!_seenAwait)
                    _variablesDeclaredBeforeAwait.Add(t.CatchParam.Lexeme);
            }
            foreach (var cs in t.CatchBlock)
                AnalyzeStmt(cs);
        }

        // Analyze finally block
        if (t.FinallyBlock != null)
        {
            _currentTryRegion = TryRegion.Finally;
            foreach (var fs in t.FinallyBlock)
                AnalyzeStmt(fs);
        }

        // Restore context
        _currentTryRegion = previousRegion;
        _currentTryBlockDepth--;
        if (_tryBlockIdStack.Count > 0)
            _currentTryBlockId = _tryBlockIdStack.Pop();
        else
            _currentTryBlockId = null;
    }

    private void AnalyzeExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.Await a:
                // Record this await point with try block context
                var liveVars = new HashSet<string>(_declaredVariables);
                _awaitPoints.Add(new AwaitPoint(
                    _awaitCounter++,
                    a,
                    liveVars,
                    _currentTryBlockDepth,
                    _currentTryBlockId
                ));
                _seenAwait = true;

                // Track that this try block has an await in the current region
                if (_currentTryBlockId.HasValue && _currentTryRegion != TryRegion.None)
                {
                    var tryId = _currentTryBlockId.Value;
                    if (!_tryBlockAwaitFlags.TryGetValue(tryId, out var flags))
                    {
                        flags = (false, false, false);
                    }
                    flags = _currentTryRegion switch
                    {
                        TryRegion.Try => (true, flags.InCatch, flags.InFinally),
                        TryRegion.Catch => (flags.InTry, true, flags.InFinally),
                        TryRegion.Finally => (flags.InTry, flags.InCatch, true),
                        _ => flags
                    };
                    _tryBlockAwaitFlags[tryId] = flags;
                }

                // Analyze the awaited expression
                AnalyzeExpr(a.Expression);
                break;

            case Expr.Variable v:
                // Track variable usage after await
                if (_seenAwait && _declaredVariables.Contains(v.Name.Lexeme))
                    _variablesUsedAfterAwait.Add(v.Name.Lexeme);
                break;

            case Expr.Assign a:
                // Track assignment to variable after await
                if (_seenAwait && _declaredVariables.Contains(a.Name.Lexeme))
                    _variablesUsedAfterAwait.Add(a.Name.Lexeme);
                AnalyzeExpr(a.Value);
                break;

            case Expr.Binary b:
                AnalyzeExpr(b.Left);
                AnalyzeExpr(b.Right);
                break;

            case Expr.Logical l:
                AnalyzeExpr(l.Left);
                AnalyzeExpr(l.Right);
                break;

            case Expr.Unary u:
                AnalyzeExpr(u.Right);
                break;

            case Expr.Grouping g:
                AnalyzeExpr(g.Expression);
                break;

            case Expr.Call c:
                AnalyzeExpr(c.Callee);
                foreach (var arg in c.Arguments)
                    AnalyzeExpr(arg);
                break;

            case Expr.Get g:
                AnalyzeExpr(g.Object);
                break;

            case Expr.Set s:
                AnalyzeExpr(s.Object);
                AnalyzeExpr(s.Value);
                break;

            case Expr.GetIndex gi:
                AnalyzeExpr(gi.Object);
                AnalyzeExpr(gi.Index);
                break;

            case Expr.SetIndex si:
                AnalyzeExpr(si.Object);
                AnalyzeExpr(si.Index);
                AnalyzeExpr(si.Value);
                break;

            case Expr.New n:
                foreach (var arg in n.Arguments)
                    AnalyzeExpr(arg);
                break;

            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    AnalyzeExpr(elem);
                break;

            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    AnalyzeExpr(prop.Value);
                break;

            case Expr.Ternary t:
                AnalyzeExpr(t.Condition);
                AnalyzeExpr(t.ThenBranch);
                AnalyzeExpr(t.ElseBranch);
                break;

            case Expr.NullishCoalescing nc:
                AnalyzeExpr(nc.Left);
                AnalyzeExpr(nc.Right);
                break;

            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    AnalyzeExpr(e);
                break;

            case Expr.CompoundAssign ca:
                if (_seenAwait && _declaredVariables.Contains(ca.Name.Lexeme))
                    _variablesUsedAfterAwait.Add(ca.Name.Lexeme);
                AnalyzeExpr(ca.Value);
                break;

            case Expr.CompoundSet cs:
                AnalyzeExpr(cs.Object);
                AnalyzeExpr(cs.Value);
                break;

            case Expr.CompoundSetIndex csi:
                AnalyzeExpr(csi.Object);
                AnalyzeExpr(csi.Index);
                AnalyzeExpr(csi.Value);
                break;

            case Expr.PrefixIncrement pi:
                AnalyzeExpr(pi.Operand);
                break;

            case Expr.PostfixIncrement poi:
                AnalyzeExpr(poi.Operand);
                break;

            case Expr.ArrowFunction af:
                // Arrow functions capture 'this' from lexical scope, so we need to
                // analyze if they use 'this' and if so, hoist it for the async method
                AnalyzeArrowForThis(af);

                // If this is an async arrow, collect it for separate state machine generation
                if (af.IsAsync)
                {
                    var captures = AnalyzeAsyncArrowCaptures(af);
                    var capturesThis = captures.Contains("this");
                    _asyncArrows.Add(new AsyncArrowInfo(af, captures, capturesThis, _asyncArrowNestingLevel, _currentParentArrow));

                    // Also hoist any captured variables to the outer state machine
                    // so they can be accessed by the async arrow's state machine
                    foreach (var capture in captures)
                    {
                        if (capture != "this" && _declaredVariables.Contains(capture))
                        {
                            _variablesUsedAfterAwait.Add(capture);
                        }
                    }

                    // Recursively analyze for nested async arrows
                    var previousParent = _currentParentArrow;
                    _currentParentArrow = af;
                    _asyncArrowNestingLevel++;
                    AnalyzeAsyncArrowBody(af);
                    _asyncArrowNestingLevel--;
                    _currentParentArrow = previousParent;
                }
                break;

            case Expr.This:
                _usesThis = true;
                break;

            case Expr.Super:
                // super implicitly requires 'this' to be hoisted
                _usesThis = true;
                break;

            case Expr.Literal:
                break;
        }
    }

    /// <summary>
    /// Analyzes an arrow function to detect if it uses 'this', which needs to be
    /// hoisted for the enclosing async method.
    /// </summary>
    private void AnalyzeArrowForThis(Expr.ArrowFunction af)
    {
        if (af.ExpressionBody != null)
        {
            AnalyzeArrowExprForThis(af.ExpressionBody);
        }
        if (af.BlockBody != null)
        {
            foreach (var stmt in af.BlockBody)
            {
                AnalyzeArrowStmtForThis(stmt);
            }
        }
    }

    private void AnalyzeArrowStmtForThis(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                AnalyzeArrowExprForThis(e.Expr);
                break;
            case Stmt.Var v:
                if (v.Initializer != null) AnalyzeArrowExprForThis(v.Initializer);
                break;
            case Stmt.If i:
                AnalyzeArrowExprForThis(i.Condition);
                AnalyzeArrowStmtForThis(i.ThenBranch);
                if (i.ElseBranch != null) AnalyzeArrowStmtForThis(i.ElseBranch);
                break;
            case Stmt.While w:
                AnalyzeArrowExprForThis(w.Condition);
                AnalyzeArrowStmtForThis(w.Body);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements) AnalyzeArrowStmtForThis(s);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements) AnalyzeArrowStmtForThis(s);
                break;
            case Stmt.Return r:
                if (r.Value != null) AnalyzeArrowExprForThis(r.Value);
                break;
            case Stmt.Print p:
                AnalyzeArrowExprForThis(p.Expr);
                break;
        }
    }

    private void AnalyzeArrowExprForThis(Expr expr)
    {
        switch (expr)
        {
            case Expr.This:
                _usesThis = true;
                break;
            case Expr.Super:
                _usesThis = true;
                break;
            case Expr.Binary b:
                AnalyzeArrowExprForThis(b.Left);
                AnalyzeArrowExprForThis(b.Right);
                break;
            case Expr.Logical l:
                AnalyzeArrowExprForThis(l.Left);
                AnalyzeArrowExprForThis(l.Right);
                break;
            case Expr.Unary u:
                AnalyzeArrowExprForThis(u.Right);
                break;
            case Expr.Grouping g:
                AnalyzeArrowExprForThis(g.Expression);
                break;
            case Expr.Call c:
                AnalyzeArrowExprForThis(c.Callee);
                foreach (var arg in c.Arguments) AnalyzeArrowExprForThis(arg);
                break;
            case Expr.Get g:
                AnalyzeArrowExprForThis(g.Object);
                break;
            case Expr.Set s:
                AnalyzeArrowExprForThis(s.Object);
                AnalyzeArrowExprForThis(s.Value);
                break;
            case Expr.GetIndex gi:
                AnalyzeArrowExprForThis(gi.Object);
                AnalyzeArrowExprForThis(gi.Index);
                break;
            case Expr.SetIndex si:
                AnalyzeArrowExprForThis(si.Object);
                AnalyzeArrowExprForThis(si.Index);
                AnalyzeArrowExprForThis(si.Value);
                break;
            case Expr.Assign a:
                AnalyzeArrowExprForThis(a.Value);
                break;
            case Expr.Ternary t:
                AnalyzeArrowExprForThis(t.Condition);
                AnalyzeArrowExprForThis(t.ThenBranch);
                AnalyzeArrowExprForThis(t.ElseBranch);
                break;
            case Expr.CompoundAssign ca:
                AnalyzeArrowExprForThis(ca.Value);
                break;
            case Expr.CompoundSet cs:
                AnalyzeArrowExprForThis(cs.Object);
                AnalyzeArrowExprForThis(cs.Value);
                break;
            case Expr.ArrowFunction af:
                // Nested arrow - recursively check for 'this' usage
                AnalyzeArrowForThis(af);
                break;
        }
    }

    /// <summary>
    /// Analyzes an async arrow function to determine which variables it captures
    /// from the enclosing scope.
    /// </summary>
    private HashSet<string> AnalyzeAsyncArrowCaptures(Expr.ArrowFunction af)
    {
        var captures = new HashSet<string>();
        var arrowLocals = new HashSet<string>();

        // Add arrow parameters as locals (not captures)
        foreach (var param in af.Parameters)
        {
            arrowLocals.Add(param.Name.Lexeme);
        }

        // Analyze arrow body for variable references
        if (af.ExpressionBody != null)
        {
            CollectArrowCaptures(af.ExpressionBody, arrowLocals, captures);
        }
        if (af.BlockBody != null)
        {
            foreach (var stmt in af.BlockBody)
            {
                CollectArrowCapturesFromStmt(stmt, arrowLocals, captures);
            }
        }

        return captures;
    }

    private void CollectArrowCapturesFromStmt(Stmt stmt, HashSet<string> locals, HashSet<string> captures)
    {
        switch (stmt)
        {
            case Stmt.Var v:
                locals.Add(v.Name.Lexeme);
                if (v.Initializer != null)
                    CollectArrowCaptures(v.Initializer, locals, captures);
                break;
            case Stmt.Expression e:
                CollectArrowCaptures(e.Expr, locals, captures);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    CollectArrowCaptures(r.Value, locals, captures);
                break;
            case Stmt.If i:
                CollectArrowCaptures(i.Condition, locals, captures);
                CollectArrowCapturesFromStmt(i.ThenBranch, locals, captures);
                if (i.ElseBranch != null)
                    CollectArrowCapturesFromStmt(i.ElseBranch, locals, captures);
                break;
            case Stmt.While w:
                CollectArrowCaptures(w.Condition, locals, captures);
                CollectArrowCapturesFromStmt(w.Body, locals, captures);
                break;
            case Stmt.ForOf f:
                locals.Add(f.Variable.Lexeme);
                CollectArrowCaptures(f.Iterable, locals, captures);
                CollectArrowCapturesFromStmt(f.Body, locals, captures);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    CollectArrowCapturesFromStmt(s, locals, captures);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    CollectArrowCapturesFromStmt(s, locals, captures);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    CollectArrowCapturesFromStmt(ts, locals, captures);
                if (t.CatchBlock != null)
                {
                    if (t.CatchParam != null)
                        locals.Add(t.CatchParam.Lexeme);
                    foreach (var cs in t.CatchBlock)
                        CollectArrowCapturesFromStmt(cs, locals, captures);
                }
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        CollectArrowCapturesFromStmt(fs, locals, captures);
                break;
            case Stmt.Switch s:
                CollectArrowCaptures(s.Subject, locals, captures);
                foreach (var c in s.Cases)
                {
                    CollectArrowCaptures(c.Value, locals, captures);
                    foreach (var cs in c.Body)
                        CollectArrowCapturesFromStmt(cs, locals, captures);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        CollectArrowCapturesFromStmt(ds, locals, captures);
                break;
            case Stmt.Throw th:
                CollectArrowCaptures(th.Value, locals, captures);
                break;
            case Stmt.Print p:
                CollectArrowCaptures(p.Expr, locals, captures);
                break;
        }
    }

    private void CollectArrowCaptures(Expr expr, HashSet<string> locals, HashSet<string> captures)
    {
        switch (expr)
        {
            case Expr.Variable v:
                var name = v.Name.Lexeme;
                // If not a local and is declared in outer scope, it's a capture
                if (!locals.Contains(name) && _declaredVariables.Contains(name))
                {
                    captures.Add(name);
                }
                break;
            case Expr.Assign a:
                if (!locals.Contains(a.Name.Lexeme) && _declaredVariables.Contains(a.Name.Lexeme))
                {
                    captures.Add(a.Name.Lexeme);
                }
                CollectArrowCaptures(a.Value, locals, captures);
                break;
            case Expr.CompoundAssign ca:
                if (!locals.Contains(ca.Name.Lexeme) && _declaredVariables.Contains(ca.Name.Lexeme))
                {
                    captures.Add(ca.Name.Lexeme);
                }
                CollectArrowCaptures(ca.Value, locals, captures);
                break;
            case Expr.This:
                captures.Add("this");
                break;
            case Expr.Binary b:
                CollectArrowCaptures(b.Left, locals, captures);
                CollectArrowCaptures(b.Right, locals, captures);
                break;
            case Expr.Logical l:
                CollectArrowCaptures(l.Left, locals, captures);
                CollectArrowCaptures(l.Right, locals, captures);
                break;
            case Expr.Unary u:
                CollectArrowCaptures(u.Right, locals, captures);
                break;
            case Expr.Grouping g:
                CollectArrowCaptures(g.Expression, locals, captures);
                break;
            case Expr.Call c:
                CollectArrowCaptures(c.Callee, locals, captures);
                foreach (var arg in c.Arguments)
                    CollectArrowCaptures(arg, locals, captures);
                break;
            case Expr.Get g:
                CollectArrowCaptures(g.Object, locals, captures);
                break;
            case Expr.Set s:
                CollectArrowCaptures(s.Object, locals, captures);
                CollectArrowCaptures(s.Value, locals, captures);
                break;
            case Expr.GetIndex gi:
                CollectArrowCaptures(gi.Object, locals, captures);
                CollectArrowCaptures(gi.Index, locals, captures);
                break;
            case Expr.SetIndex si:
                CollectArrowCaptures(si.Object, locals, captures);
                CollectArrowCaptures(si.Index, locals, captures);
                CollectArrowCaptures(si.Value, locals, captures);
                break;
            case Expr.New n:
                foreach (var arg in n.Arguments)
                    CollectArrowCaptures(arg, locals, captures);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    CollectArrowCaptures(elem, locals, captures);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    CollectArrowCaptures(prop.Value, locals, captures);
                break;
            case Expr.Ternary t:
                CollectArrowCaptures(t.Condition, locals, captures);
                CollectArrowCaptures(t.ThenBranch, locals, captures);
                CollectArrowCaptures(t.ElseBranch, locals, captures);
                break;
            case Expr.NullishCoalescing nc:
                CollectArrowCaptures(nc.Left, locals, captures);
                CollectArrowCaptures(nc.Right, locals, captures);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    CollectArrowCaptures(e, locals, captures);
                break;
            case Expr.Await aw:
                CollectArrowCaptures(aw.Expression, locals, captures);
                break;
            case Expr.ArrowFunction af:
                // Nested arrow functions may also capture - but we handle them separately
                // Their captures become our captures if they're from our scope
                var nestedCaptures = AnalyzeAsyncArrowCaptures(af);
                foreach (var cap in nestedCaptures)
                {
                    if (!locals.Contains(cap))
                        captures.Add(cap);
                }
                break;
            case Expr.CompoundSet cs:
                CollectArrowCaptures(cs.Object, locals, captures);
                CollectArrowCaptures(cs.Value, locals, captures);
                break;
            case Expr.CompoundSetIndex csi:
                CollectArrowCaptures(csi.Object, locals, captures);
                CollectArrowCaptures(csi.Index, locals, captures);
                CollectArrowCaptures(csi.Value, locals, captures);
                break;
            case Expr.PrefixIncrement pi:
                CollectArrowCaptures(pi.Operand, locals, captures);
                break;
            case Expr.PostfixIncrement poi:
                CollectArrowCaptures(poi.Operand, locals, captures);
                break;
        }
    }

    /// <summary>
    /// Recursively analyzes an async arrow body for nested async arrows.
    /// </summary>
    private void AnalyzeAsyncArrowBody(Expr.ArrowFunction af)
    {
        if (af.ExpressionBody != null)
        {
            AnalyzeAsyncArrowBodyExpr(af.ExpressionBody);
        }
        if (af.BlockBody != null)
        {
            foreach (var stmt in af.BlockBody)
            {
                AnalyzeAsyncArrowBodyStmt(stmt);
            }
        }
    }

    private void AnalyzeAsyncArrowBodyStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                AnalyzeAsyncArrowBodyExpr(e.Expr);
                break;
            case Stmt.Var v:
                if (v.Initializer != null)
                    AnalyzeAsyncArrowBodyExpr(v.Initializer);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    AnalyzeAsyncArrowBodyExpr(r.Value);
                break;
            case Stmt.If i:
                AnalyzeAsyncArrowBodyExpr(i.Condition);
                AnalyzeAsyncArrowBodyStmt(i.ThenBranch);
                if (i.ElseBranch != null)
                    AnalyzeAsyncArrowBodyStmt(i.ElseBranch);
                break;
            case Stmt.While w:
                AnalyzeAsyncArrowBodyExpr(w.Condition);
                AnalyzeAsyncArrowBodyStmt(w.Body);
                break;
            case Stmt.ForOf f:
                AnalyzeAsyncArrowBodyExpr(f.Iterable);
                AnalyzeAsyncArrowBodyStmt(f.Body);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    AnalyzeAsyncArrowBodyStmt(s);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    AnalyzeAsyncArrowBodyStmt(s);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    AnalyzeAsyncArrowBodyStmt(ts);
                if (t.CatchBlock != null)
                    foreach (var cs in t.CatchBlock)
                        AnalyzeAsyncArrowBodyStmt(cs);
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        AnalyzeAsyncArrowBodyStmt(fs);
                break;
            case Stmt.Switch s:
                AnalyzeAsyncArrowBodyExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    AnalyzeAsyncArrowBodyExpr(c.Value);
                    foreach (var cs in c.Body)
                        AnalyzeAsyncArrowBodyStmt(cs);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        AnalyzeAsyncArrowBodyStmt(ds);
                break;
            case Stmt.Throw th:
                AnalyzeAsyncArrowBodyExpr(th.Value);
                break;
            case Stmt.Print p:
                AnalyzeAsyncArrowBodyExpr(p.Expr);
                break;
        }
    }

    private void AnalyzeAsyncArrowBodyExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.ArrowFunction af when af.IsAsync:
                // Found a nested async arrow!
                var captures = AnalyzeAsyncArrowCaptures(af);
                var capturesThis = captures.Contains("this");
                _asyncArrows.Add(new AsyncArrowInfo(af, captures, capturesThis, _asyncArrowNestingLevel, _currentParentArrow));

                // Recursively look for deeper nested async arrows
                var previousParent = _currentParentArrow;
                _currentParentArrow = af;
                _asyncArrowNestingLevel++;
                AnalyzeAsyncArrowBody(af);
                _asyncArrowNestingLevel--;
                _currentParentArrow = previousParent;
                break;
            case Expr.ArrowFunction af:
                // Non-async nested arrow - still check for nested async arrows inside
                AnalyzeAsyncArrowBody(af);
                break;
            case Expr.Binary b:
                AnalyzeAsyncArrowBodyExpr(b.Left);
                AnalyzeAsyncArrowBodyExpr(b.Right);
                break;
            case Expr.Logical l:
                AnalyzeAsyncArrowBodyExpr(l.Left);
                AnalyzeAsyncArrowBodyExpr(l.Right);
                break;
            case Expr.Unary u:
                AnalyzeAsyncArrowBodyExpr(u.Right);
                break;
            case Expr.Grouping g:
                AnalyzeAsyncArrowBodyExpr(g.Expression);
                break;
            case Expr.Call c:
                AnalyzeAsyncArrowBodyExpr(c.Callee);
                foreach (var arg in c.Arguments)
                    AnalyzeAsyncArrowBodyExpr(arg);
                break;
            case Expr.Get g:
                AnalyzeAsyncArrowBodyExpr(g.Object);
                break;
            case Expr.Set s:
                AnalyzeAsyncArrowBodyExpr(s.Object);
                AnalyzeAsyncArrowBodyExpr(s.Value);
                break;
            case Expr.GetIndex gi:
                AnalyzeAsyncArrowBodyExpr(gi.Object);
                AnalyzeAsyncArrowBodyExpr(gi.Index);
                break;
            case Expr.SetIndex si:
                AnalyzeAsyncArrowBodyExpr(si.Object);
                AnalyzeAsyncArrowBodyExpr(si.Index);
                AnalyzeAsyncArrowBodyExpr(si.Value);
                break;
            case Expr.Assign a:
                AnalyzeAsyncArrowBodyExpr(a.Value);
                break;
            case Expr.Ternary t:
                AnalyzeAsyncArrowBodyExpr(t.Condition);
                AnalyzeAsyncArrowBodyExpr(t.ThenBranch);
                AnalyzeAsyncArrowBodyExpr(t.ElseBranch);
                break;
            case Expr.NullishCoalescing nc:
                AnalyzeAsyncArrowBodyExpr(nc.Left);
                AnalyzeAsyncArrowBodyExpr(nc.Right);
                break;
            case Expr.Await aw:
                AnalyzeAsyncArrowBodyExpr(aw.Expression);
                break;
            case Expr.New n:
                foreach (var arg in n.Arguments)
                    AnalyzeAsyncArrowBodyExpr(arg);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    AnalyzeAsyncArrowBodyExpr(elem);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    AnalyzeAsyncArrowBodyExpr(prop.Value);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    AnalyzeAsyncArrowBodyExpr(e);
                break;
            case Expr.CompoundAssign ca:
                AnalyzeAsyncArrowBodyExpr(ca.Value);
                break;
            case Expr.CompoundSet cs:
                AnalyzeAsyncArrowBodyExpr(cs.Object);
                AnalyzeAsyncArrowBodyExpr(cs.Value);
                break;
            case Expr.CompoundSetIndex csi:
                AnalyzeAsyncArrowBodyExpr(csi.Object);
                AnalyzeAsyncArrowBodyExpr(csi.Index);
                AnalyzeAsyncArrowBodyExpr(csi.Value);
                break;
            case Expr.PrefixIncrement pi:
                AnalyzeAsyncArrowBodyExpr(pi.Operand);
                break;
            case Expr.PostfixIncrement poi:
                AnalyzeAsyncArrowBodyExpr(poi.Operand);
                break;
        }
    }
}
