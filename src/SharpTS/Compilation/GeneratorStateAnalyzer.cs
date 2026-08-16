using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes generator functions to identify yield points and variables that must be hoisted
/// to the state machine struct. Uses the visitor pattern for AST traversal.
/// </summary>
public class GeneratorStateAnalyzer : AstVisitorBase
{
    /// <summary>
    /// Represents a single yield point in a generator function.
    /// </summary>
    public record YieldPoint(
        int StateNumber,
        Expr.Yield YieldExpr,
        HashSet<string> LiveVariables
    );

    /// <summary>
    /// Complete analysis results for a generator function.
    /// </summary>
    public record GeneratorFunctionAnalysis(
        int YieldPointCount,
        List<YieldPoint> YieldPoints,
        HashSet<string> HoistedLocals,
        HashSet<string> HoistedParameters,
        bool UsesThis,
        bool HasYieldStar,
        List<Stmt.ForOf> ForOfLoopsWithYield,  // for...of loops containing yields that need enumerator hoisting
        List<Stmt.ForIn> ForInLoopsWithYield,  // for...in loops containing yields that need key-list/index hoisting (#547)
        // Per-binding storage names for block-scoped let/const declarations that shadow an enclosing
        // binding, keyed by declaration/reference AST node (see GeneratorBlockScopeRenamer, #711). The
        // emitter consults the same map so each shadowing binding gets its own field/local.
        IReadOnlyDictionary<object, string> BlockScopeRenames,
        // Capturing-arrow node → (captured source name → renamed generator storage) (#767). When a
        // shadowing binding that was renamed above is captured by an inner arrow, the arrow's field is
        // sourced from the renamed storage instead of the outer same-named binding. Empty in the common
        // case; null is treated as empty by the emitter.
        IReadOnlyDictionary<object, IReadOnlyDictionary<string, string>>? BlockScopeCaptureRenames = null
    );

    // State during analysis
    private readonly List<YieldPoint> _yieldPoints = [];
    private readonly HashSet<string> _declaredVariables = [];
    private readonly HashSet<string> _variablesUsedAfterYield = [];
    private readonly HashSet<string> _variablesDeclaredBeforeYield = [];
    private readonly List<Stmt.ForOf> _forOfLoopsWithYield = [];  // for...of loops containing yields (enumerator hoisting)
    private readonly List<Stmt.ForIn> _forInLoopsWithYield = [];  // for...in loops containing yields (key-list/index hoisting, #547)
    // Loop bodies currently being analyzed (innermost on top). A loop whose body contains a
    // yield re-executes after the yield resumes, so every local used anywhere in it is live
    // across the suspension and must be hoisted to a state-machine field — otherwise the IL
    // local is wiped on MoveNext re-entry and reads back as its default. This applies to every
    // loop form (while/do-while/for/for-in/for-of), not just for...of (#497).
    private readonly Stack<LoopScope> _loopStack = new();
    private int _yieldCounter = 0;
    private bool _seenYield = false;
    private bool _usesThis = false;
    private bool _hasYieldStar = false;
    // Block-scope shadow renames for this function (#711). Maps a declaration/reference AST node to
    // the disambiguated storage name its binding uses; nodes absent from the map keep their lexeme.
    private IReadOnlyDictionary<object, string> _renames = new Dictionary<object, string>();
    // Per-arrow capture-source pivot for renamed shadows captured by inner arrows (#767).
    private IReadOnlyDictionary<object, IReadOnlyDictionary<string, string>> _captureRenames =
        new Dictionary<object, IReadOnlyDictionary<string, string>>();

    /// <summary>
    /// Analyzes a generator function to determine yield points and hoisted variables.
    /// </summary>
    public GeneratorFunctionAnalysis Analyze(Stmt.Function func)
    {
        Reset();

        // Disambiguate block-scoped let/const declarations that shadow an enclosing binding so the
        // hoisting decision below is made per-binding rather than per-name (#711).
        var renameResult = GeneratorBlockScopeRenamer.Compute(func);
        _renames = renameResult.Renames;
        _captureRenames = renameResult.CaptureRenames;

        // Collect parameters as variables that need hoisting
        HashSet<string> parameters = [];
        foreach (var param in func.Parameters)
        {
            parameters.Add(param.Name.Lexeme);
            _declaredVariables.Add(param.Name.Lexeme);
            _variablesDeclaredBeforeYield.Add(param.Name.Lexeme);
        }

        // Analyze the function body using visitor pattern
        if (func.Body != null)
        {
            foreach (var stmt in func.Body)
            {
                Visit(stmt);
            }
        }

        // Variables that need hoisting: used after a yield (regardless of declaration point).
        // Previously gated on "declared before first yield" — missed variables declared
        // BETWEEN yields but used AFTER a later yield (yaml's `*parseDocument`: `const line`
        // declared after `yield* pushSpaces` but used after `yield* pushIndicators` —
        // the value must persist across pushIndicators, which requires a state-machine field).
        // Using all declared variables as the superset is a safe over-approximation; worst
        // case we hoist a local unnecessarily.
        var hoistedLocals = new HashSet<string>(_declaredVariables);
        hoistedLocals.IntersectWith(_variablesUsedAfterYield);
        hoistedLocals.ExceptWith(parameters); // Parameters are tracked separately

        return new GeneratorFunctionAnalysis(
            YieldPointCount: _yieldPoints.Count,
            YieldPoints: [.. _yieldPoints],
            HoistedLocals: hoistedLocals,
            HoistedParameters: parameters,
            UsesThis: _usesThis,
            HasYieldStar: _hasYieldStar,
            ForOfLoopsWithYield: [.. _forOfLoopsWithYield],
            ForInLoopsWithYield: [.. _forInLoopsWithYield],
            BlockScopeRenames: _renames,
            BlockScopeCaptureRenames: _captureRenames
        );
    }

    /// <summary>
    /// Translates a declaration/reference node's source lexeme to its disambiguated storage name
    /// (#711), or returns the lexeme unchanged when the binding is not a renamed shadow.
    /// </summary>
    private string StorageName(object node, string lexeme) =>
        _renames.TryGetValue(node, out var renamed) ? renamed : lexeme;

    private void Reset()
    {
        _yieldPoints.Clear();
        _declaredVariables.Clear();
        _variablesUsedAfterYield.Clear();
        _variablesDeclaredBeforeYield.Clear();
        _forOfLoopsWithYield.Clear();
        _forInLoopsWithYield.Clear();
        _loopStack.Clear();
        _yieldCounter = 0;
        _seenYield = false;
        _usesThis = false;
        _hasYieldStar = false;
    }

    /// <summary>
    /// Tracks one enclosing loop body during analysis: the variables it touches and whether a
    /// yield occurs anywhere inside it. <see cref="ForOf"/> is non-null only for for...of loops,
    /// which additionally need their enumerator hoisted to a field.
    /// </summary>
    private sealed class LoopScope(Stmt.ForOf? forOf, Stmt.ForIn? forIn)
    {
        public readonly HashSet<string> UsedVariables = [];
        public bool ContainsYield;
        public readonly Stmt.ForOf? ForOf = forOf;
        public readonly Stmt.ForIn? ForIn = forIn;
    }

    private void EnterLoop(Stmt.ForOf? forOf = null, Stmt.ForIn? forIn = null) => _loopStack.Push(new LoopScope(forOf, forIn));

    // On leaving a loop body that contained a yield, hoist every local it used: the body
    // re-executes after the yield resumes, so those values must survive the suspension (#497).
    private void ExitLoop()
    {
        var scope = _loopStack.Pop();
        if (!scope.ContainsYield) return;
        foreach (var name in scope.UsedVariables)
        {
            if (_declaredVariables.Contains(name))
                _variablesUsedAfterYield.Add(name);
        }
    }

    #region Statement Visitor Overrides

    protected override void VisitVar(Stmt.Var stmt)
    {
        var name = StorageName(stmt, stmt.Name.Lexeme);
        _declaredVariables.Add(name);
        if (!_seenYield)
            _variablesDeclaredBeforeYield.Add(name);
        base.VisitVar(stmt);
    }

    protected override void VisitConst(Stmt.Const stmt)
    {
        var name = StorageName(stmt, stmt.Name.Lexeme);
        _declaredVariables.Add(name);
        if (!_seenYield)
            _variablesDeclaredBeforeYield.Add(name);
        base.VisitConst(stmt);
    }

    protected override void VisitWhile(Stmt.While stmt)
    {
        EnterLoop();
        base.VisitWhile(stmt);  // condition + body
        ExitLoop();
    }

    protected override void VisitDoWhile(Stmt.DoWhile stmt)
    {
        EnterLoop();
        base.VisitDoWhile(stmt);  // body + condition
        ExitLoop();
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        _declaredVariables.Add(stmt.Variable.Lexeme);
        if (!_seenYield)
            _variablesDeclaredBeforeYield.Add(stmt.Variable.Lexeme);

        // Pass the loop node so a yield inside also records it for enumerator hoisting.
        EnterLoop(stmt);
        base.VisitForOf(stmt);  // iterable + body
        ExitLoop();
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        _declaredVariables.Add(stmt.Variable.Lexeme);
        if (!_seenYield)
            _variablesDeclaredBeforeYield.Add(stmt.Variable.Lexeme);

        // Pass the loop node so a yield inside also records it for key-list/index hoisting (#547).
        EnterLoop(forIn: stmt);
        base.VisitForIn(stmt);  // object + body
        ExitLoop();
    }

    protected override void VisitFor(Stmt.For stmt)
    {
        // The initializer runs once before the loop, so it stays outside the loop scope; the
        // loop variable it declares is still tracked through its uses in condition/body/increment.
        if (stmt.Initializer != null)
            Visit(stmt.Initializer);

        // Condition, body, and increment all re-execute each iteration.
        EnterLoop();
        if (stmt.Condition != null)
            Visit(stmt.Condition);
        Visit(stmt.Body);
        if (stmt.Increment != null)
            Visit(stmt.Increment);
        ExitLoop();
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        // Visit try block
        foreach (var ts in stmt.TryBlock)
            Visit(ts);

        // Track catch parameter and visit catch block
        if (stmt.CatchBlock != null)
        {
            if (stmt.CatchParam != null)
            {
                _declaredVariables.Add(stmt.CatchParam.Lexeme);
                if (!_seenYield)
                    _variablesDeclaredBeforeYield.Add(stmt.CatchParam.Lexeme);
            }
            foreach (var cs in stmt.CatchBlock)
                Visit(cs);
        }

        // Visit finally block
        if (stmt.FinallyBlock != null)
            foreach (var fs in stmt.FinallyBlock)
                Visit(fs);
    }

    // Don't traverse into nested declarations - they don't affect our analysis
    protected override void VisitFunction(Stmt.Function stmt) { }
    protected override void VisitClass(Stmt.Class stmt) { }
    protected override void VisitInterface(Stmt.Interface stmt) { }
    protected override void VisitTypeAlias(Stmt.TypeAlias stmt) { }
    protected override void VisitEnum(Stmt.Enum stmt) { }
    protected override void VisitNamespace(Stmt.Namespace stmt) { }

    #endregion

    #region Expression Visitor Overrides

    protected override void VisitYield(Expr.Yield expr)
    {
        // Visit yield value BEFORE marking _seenYield, so variables in the yield
        // expression are not incorrectly marked as "used after yield"
        base.VisitYield(expr);

        var liveVars = new HashSet<string>(_declaredVariables);
        _yieldPoints.Add(new YieldPoint(_yieldCounter++, expr, liveVars));
        _seenYield = true;
        if (expr.IsDelegating)
            _hasYieldStar = true;

        // Mark every enclosing loop as containing a yield so its body's locals get hoisted
        // (ExitLoop), and record any enclosing for...of loop for enumerator hoisting.
        foreach (var scope in _loopStack)
        {
            scope.ContainsYield = true;
            if (scope.ForOf != null && !_forOfLoopsWithYield.Contains(scope.ForOf))
                _forOfLoopsWithYield.Add(scope.ForOf);
            if (scope.ForIn != null && !_forInLoopsWithYield.Contains(scope.ForIn))
                _forInLoopsWithYield.Add(scope.ForIn);
        }
    }

    protected override void VisitVariable(Expr.Variable expr)
    {
        var name = StorageName(expr, expr.Name.Lexeme);

        // Track variables used in any enclosing loop body (hoisted when the loop contains a yield).
        foreach (var scope in _loopStack)
            scope.UsedVariables.Add(name);

        if (_seenYield && _declaredVariables.Contains(name))
            _variablesUsedAfterYield.Add(name);
        // No base call needed - leaf node
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        var name = StorageName(expr, expr.Name.Lexeme);
        if (_seenYield && _declaredVariables.Contains(name))
            _variablesUsedAfterYield.Add(name);
        base.VisitAssign(expr);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        var name = StorageName(expr, expr.Name.Lexeme);
        if (_seenYield && _declaredVariables.Contains(name))
            _variablesUsedAfterYield.Add(name);
        base.VisitCompoundAssign(expr);
    }

    protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
    {
        var name = StorageName(expr, expr.Name.Lexeme);
        if (_seenYield && _declaredVariables.Contains(name))
            _variablesUsedAfterYield.Add(name);
        base.VisitLogicalAssign(expr);
    }

    protected override void VisitThis(Expr.This expr)
    {
        _usesThis = true;
        // No base call needed - leaf node
    }

    protected override void VisitSuper(Expr.Super expr)
    {
        _usesThis = true;
        // No base call needed - leaf node
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        // Arrow bodies contribute no yield points or hoisted locals to the enclosing
        // generator (an arrow can't `yield` to it), so the full generator analysis is
        // intentionally NOT run over them. But an arrow lexically captures `this`/`super`
        // from the generator, so when the arrow (or a nested arrow) references either, the
        // generator instance method must still materialize its `<>4__this` receiver.
        // Without this, `this` used ONLY inside an arrow left UsesThis false → no ThisField
        // → the captured `this` snapshot was null → NRE when the arrow dereferenced it
        // (#435/#669). Detection over-approximates (a `this` inside a nested *regular*
        // function, which rebinds `this`, also trips it) — harmless: the worst case is an
        // unused field, only ever read by arrows that genuinely capture `this`.
        if (!_usesThis)
        {
            var detector = new ThisUsageDetector();
            detector.Visit(expr);
            if (detector.UsesThis)
                _usesThis = true;
        }
    }

    /// <summary>
    /// Lightweight visitor that reports whether a subtree references <c>this</c> or
    /// <c>super</c>. Used to decide whether a generator that only touches <c>this</c>
    /// from inside an arrow must still materialize its receiver field.
    /// </summary>
    private sealed class ThisUsageDetector : Parsing.Visitors.AstVisitorBase
    {
        public bool UsesThis { get; private set; }
        protected override void VisitThis(Expr.This expr) => UsesThis = true;
        protected override void VisitSuper(Expr.Super expr) => UsesThis = true;
    }

    #endregion
}
