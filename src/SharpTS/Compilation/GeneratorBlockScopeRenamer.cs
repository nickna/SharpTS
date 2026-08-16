using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Result of <see cref="GeneratorBlockScopeRenamer.Compute(Stmt.Function)"/>: the per-binding storage
/// renames for the state-machine body plus the per-arrow capture-source pivot (#767).
/// </summary>
/// <param name="Renames">
/// Declaration/reference AST node → disambiguated storage name, for nodes in the STATE-MACHINE body
/// (not inside a nested closure). Consumed by the analyzer (hoist decision) and the MoveNext emitter
/// (retoken before field/local access). Empty when nothing shadows.
/// </param>
/// <param name="CaptureRenames">
/// Capturing-arrow node → (captured source name → renamed generator storage), for shadows an arrow only
/// READS. When an arrow lexically captures a shadowing block-scoped binding that <see cref="Renames"/>
/// moved to its own storage, the arrow's by-value display-class field must be sourced from that storage
/// rather than the outer same-named binding. The capture-population step consults this map to pivot the
/// SOURCE of the field (the arrow's own DC field keeps the original name). Empty unless a renamed
/// read-only shadow is closure-captured.
/// </param>
/// <param name="WriteCaptureRenames">
/// Capturing-arrow node → (captured source name → renamed generator storage), for shadows an arrow
/// WRITES (so they are lifted into the shared, name-keyed function display class <c>$functionDC</c>,
/// #674/#725). Unlike <see cref="CaptureRenames"/> (a by-value snapshot pivot), these drive the
/// rename-aware DC: the DC field is registered under the renamed storage, and the arrow body's
/// read/write of the capture is remapped to <c>$functionDC.&lt;name&gt;__bsN</c> instead of the outer
/// same-named binding's field (#838). A shadow both read and written by the same arrow is recorded here
/// (a write makes it DC-backed). Empty unless a renamed write-captured shadow exists.
/// </param>
internal sealed record BlockScopeRenameResult(
    IReadOnlyDictionary<object, string> Renames,
    IReadOnlyDictionary<object, IReadOnlyDictionary<string, string>> CaptureRenames,
    IReadOnlyDictionary<object, IReadOnlyDictionary<string, string>> WriteCaptureRenames)
{
    public static readonly BlockScopeRenameResult Empty = new(
        new Dictionary<object, string>(),
        new Dictionary<object, IReadOnlyDictionary<string, string>>(),
        new Dictionary<object, IReadOnlyDictionary<string, string>>());
}

/// <summary>
/// Computes per-binding storage names for block-scoped (<c>let</c>/<c>const</c>) declarations inside a
/// suspension state-machine body that <em>shadow</em> an enclosing binding of the same name.
/// </summary>
/// <remarks>
/// Although named for generators (where it originated, #711), this pass is mode-agnostic: every
/// suspension state machine that hoists live-across-suspension locals to name-keyed fields shares the
/// same shadow-collision root cause, so it is reused by the async-function and async-generator state
/// machines too (<see cref="AsyncStateAnalyzer"/> / <see cref="AsyncGeneratorStateAnalyzer"/>, #766).
/// The generator state machine hoists every local that lives across a <c>yield</c> into a field keyed
/// by its source name (see <see cref="HoistingManager"/>), and resolves non-hoisted locals through a
/// flat, name-keyed local map. Both flatten lexical scope, so two block-scoped bindings that share a
/// name collide on a single slot: an inner <c>{ const r = 0 }</c> clobbers an outer <c>const r</c>
/// (#711). This pass walks the body with a scope stack and, for each inner <c>let</c>/<c>const</c> that
/// shadows an enclosing binding, assigns a fresh source-illegal storage name. It records — by AST-node
/// identity — the new name for the declaration and for every reference that lexically binds to it.
/// <see cref="GeneratorStateAnalyzer"/> and <see cref="GeneratorMoveNextEmitter"/> consult the map so the
/// hoist-vs-local decision and the field/local access become per-binding instead of per-name.
///
/// Closure interaction (#767/#838): a shadowing binding that an inner <em>arrow</em> reads or writes is
/// still renamed, and the renamer records, per capturing arrow, the source name → storage mapping. A
/// READ-only capture is recorded in <see cref="BlockScopeRenameResult.CaptureRenames"/> so the
/// capture-population step sources the arrow's by-value field from the renamed generator storage (#767);
/// a WRITE capture is recorded in <see cref="BlockScopeRenameResult.WriteCaptureRenames"/> so the
/// rename-aware <c>$functionDC</c> registers its shared field under the renamed storage and the arrow
/// body's access is redirected there (#838). The arrow body is compiled in its own context and its
/// reference nodes are never rewritten — only the source pivot is recorded. One shape the capture pivot
/// still cannot honour stays OFF-LIMITS (left unrenamed, the conservative #711 behaviour): a binding read
/// or written by a nested <em>non-arrow</em> function/class (it flows through the name-keyed lambda-lift
/// path the pivot cannot redirect).
///
/// Restrictions that keep the rewrite sound:
/// <list type="bullet">
/// <item>Only <c>let</c>/<c>const</c> (<see cref="Stmt.Const"/>, <see cref="Stmt.Var"/> with
/// <c>IsVar == false</c>) declarations are renamed. <c>for</c>/<c>for-of</c>/<c>for-in</c>/<c>catch</c>
/// introduce scopes (so shadowing is detected accurately) but their loop-variable / catch-parameter
/// bindings are left alone.</item>
/// <item>Declarations and references INSIDE a nested closure are never added to <see cref="Renames"/>
/// — the closure compiles in its own context. The walk descends into arrows only to resolve which
/// renamed generator binding each capture refers to (the <see cref="CaptureRenames"/> side-map).</item>
/// </list>
/// No AST node is mutated and none is created, so TypeMap / ClosureAnalyzer node identity is preserved.
/// The walk is deterministic, so independent <see cref="GeneratorStateAnalyzer.Analyze"/> calls over the
/// same function (field definition vs. body emission) produce identical maps.
/// </remarks>
internal sealed class GeneratorBlockScopeRenamer : AstVisitorBase
{
    // Keyed by AST-node reference (records use value equality, so reference identity is mandatory here).
    private readonly Dictionary<object, string> _renames = new(ReferenceEqualityComparer.Instance);
    // Capturing-arrow node → (captured source name → renamed generator storage), for READ-only captures
    // (by-value snapshot pivot). Reference-identity keyed.
    private readonly Dictionary<object, Dictionary<string, string>> _captureSources = new(ReferenceEqualityComparer.Instance);
    // Capturing-arrow node → (captured source name → renamed generator storage), for WRITE captures
    // (shared $functionDC, #838). Reference-identity keyed.
    private readonly Dictionary<object, Dictionary<string, string>> _writeCaptureSources = new(ReferenceEqualityComparer.Instance);
    // Scope stack of name -> storage-name (storage == name when the binding is not renamed).
    private readonly List<Dictionary<string, string>> _scopes = [];
    // Names a closure makes off-limits to renaming: read by a non-arrow function/class, or written under a
    // non-arrow function/class (both name-keyed lambda-lift paths the capture pivot cannot honour).
    private readonly HashSet<string> _renameOffLimits = [];
    // Names WRITTEN inside an arrow only (no intervening non-arrow function/class): renameable and
    // DC-backed — the arrow's accesses route to the shared $functionDC field (#674/#725/#838).
    private readonly HashSet<string> _writeCaptured = [];
    private int _counter;
    // Depth of nested arrows currently being walked (0 == directly in the state-machine body).
    private int _arrowDepth;
    // The outermost arrow (directly in the state-machine body) currently being walked; captures of
    // renamed generator bindings — at any nesting depth below it — are recorded against this arrow,
    // because its display class is what the capture-population step populates.
    private Expr.ArrowFunction? _currentTopArrow;

    /// <summary>
    /// Returns the renames + capture-source pivot for <paramref name="func"/>; empty when nothing shadows.
    /// </summary>
    /// <param name="arrowReadCapturesShareStorage">
    /// When true, a binding merely READ by a nested arrow is also off-limits to renaming, because the
    /// enclosing context lifts every captured local — read or write — into a name-keyed shared display
    /// class (async free functions / async arrows: <c>ILCompiler.DefineFunctionDisplayClass</c>), so
    /// renaming the body side would desync the closure's by-name read. Generators and async generators
    /// pass false: they lift only captured-AND-mutated locals, so a read-only arrow capture flows through
    /// the per-arrow snapshot path the capture pivot can redirect (#767).
    /// </param>
    public static BlockScopeRenameResult Compute(Stmt.Function func, bool arrowReadCapturesShareStorage = false) =>
        Compute(func.Name.Lexeme, func.Parameters, func.Body, arrowReadCapturesShareStorage);

    /// <summary>
    /// Arrow-function overload (#766). Arrows have no self-name, and only a block body can hold
    /// block-scoped declarations (an expression-bodied arrow has none, so it has no shadows to rename).
    /// </summary>
    public static BlockScopeRenameResult Compute(Expr.ArrowFunction arrow, bool arrowReadCapturesShareStorage = false) =>
        Compute(selfName: null, arrow.Parameters, arrow.BlockBody, arrowReadCapturesShareStorage);

    private static BlockScopeRenameResult Compute(
        string? selfName, List<Stmt.Parameter> parameters, List<Stmt>? body, bool arrowReadCapturesShareStorage)
    {
        var renamer = new GeneratorBlockScopeRenamer();
        if (body == null) return BlockScopeRenameResult.Empty;   // expression-bodied arrow: nothing to rename

        new CaptureClassifier(renamer._renameOffLimits, renamer._writeCaptured, arrowReadCapturesShareStorage).Run(body);

        renamer.PushScope();
        // The callable's own name is in scope inside the body (a named function expression can call
        // itself by it); a nested-block let/const may shadow it, so seed it for shadow detection.
        // Never renamed (it is not a hoisted local — it resolves to the function/method itself).
        if (!string.IsNullOrEmpty(selfName))
            renamer.CurrentScope[selfName] = selfName;
        // Parameters live in the function scope; an inner block let/const may shadow them. Never renamed.
        foreach (var p in parameters)
            renamer.CurrentScope[p.Name.Lexeme] = p.Name.Lexeme;
        foreach (var stmt in body)
            renamer.Visit(stmt);
        renamer.PopScope();

        if (renamer._renames.Count == 0 && renamer._captureSources.Count == 0 && renamer._writeCaptureSources.Count == 0)
            return BlockScopeRenameResult.Empty;

        var captures = new Dictionary<object, IReadOnlyDictionary<string, string>>(ReferenceEqualityComparer.Instance);
        foreach (var (arrow, names) in renamer._captureSources)
            captures[arrow] = names;
        var writeCaptures = new Dictionary<object, IReadOnlyDictionary<string, string>>(ReferenceEqualityComparer.Instance);
        foreach (var (arrow, names) in renamer._writeCaptureSources)
            writeCaptures[arrow] = names;
        return new BlockScopeRenameResult(renamer._renames, captures, writeCaptures);
    }

    private Dictionary<string, string> CurrentScope => _scopes[^1];
    private void PushScope() => _scopes.Add([]);
    private void PopScope() => _scopes.RemoveAt(_scopes.Count - 1);

    private bool ShadowsEnclosing(string name)
    {
        for (int i = _scopes.Count - 2; i >= 0; i--)
            if (_scopes[i].ContainsKey(name)) return true;
        return false;
    }

    private string? Resolve(string name)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].TryGetValue(name, out var storage)) return storage;
        return null;
    }

    /// <summary>
    /// Declares a state-machine-body block-scoped binding (only reached when <c>_arrowDepth == 0</c>),
    /// renaming it to its own storage when it shadows an enclosing binding and is not off-limits.
    /// </summary>
    private void DeclareBlockScoped(object node, string name)
    {
        // Same-scope redeclaration is a TypeScript error; keep the first binding.
        if (CurrentScope.ContainsKey(name)) return;

        if (ShadowsEnclosing(name) && !_renameOffLimits.Contains(name))
        {
            var storage = $"<{name}>__bs{_counter++}";
            CurrentScope[name] = storage;
            _renames[node] = storage;
        }
        else
        {
            CurrentScope[name] = name;
        }
    }

    private void RecordRef(object node, string name)
    {
        var storage = Resolve(name);
        if (storage == null || storage == name) return;

        if (_arrowDepth == 0)
        {
            // Reference in the state-machine body: retoken at emit so the read/write lands on the
            // shadow's own field/local.
            _renames[node] = storage;
        }
        else if (_currentTopArrow != null)
        {
            // Reference inside a nested arrow that lexically binds to a renamed generator shadow. The
            // reference node itself is NOT renamed (the arrow body compiles in its own context); instead
            // the source name → storage pivot is recorded so the emit phase can redirect it. A WRITE
            // capture is DC-backed (the arrow shares $functionDC.<storage>, #838); a read-only capture is
            // snapshot-backed (the arrow's by-value field is sourced from <storage>, #767).
            var sink = _writeCaptured.Contains(name) ? _writeCaptureSources : _captureSources;
            if (!sink.TryGetValue(_currentTopArrow, out var names))
                sink[_currentTopArrow] = names = [];
            names[name] = storage;
        }
    }

    #region Declarations

    protected override void VisitConst(Stmt.Const stmt)
    {
        base.VisitConst(stmt);   // initializer is evaluated before the binding enters scope
        if (_arrowDepth > 0)
        {
            CurrentScope.TryAdd(stmt.Name.Lexeme, stmt.Name.Lexeme);   // arrow-local: resolution only, never renamed
            return;
        }
        DeclareBlockScoped(stmt, stmt.Name.Lexeme);
    }

    protected override void VisitVar(Stmt.Var stmt)
    {
        base.VisitVar(stmt);
        if (_arrowDepth > 0)
        {
            CurrentScope.TryAdd(stmt.Name.Lexeme, stmt.Name.Lexeme);   // arrow-local: resolution only, never renamed
            return;
        }
        if (stmt.IsVar)
        {
            // `var` is function-scoped: record it in the bottom scope so a later block-scoped
            // let/const that shadows it is detected, but never rename it (one binding per name).
            _scopes[0].TryAdd(stmt.Name.Lexeme, stmt.Name.Lexeme);
        }
        else
        {
            DeclareBlockScoped(stmt, stmt.Name.Lexeme);
        }
    }

    #endregion

    #region References

    protected override void VisitVariable(Expr.Variable expr) => RecordRef(expr, expr.Name.Lexeme);

    protected override void VisitAssign(Expr.Assign expr)
    {
        RecordRef(expr, expr.Name.Lexeme);
        base.VisitAssign(expr);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        RecordRef(expr, expr.Name.Lexeme);
        base.VisitCompoundAssign(expr);
    }

    protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
    {
        RecordRef(expr, expr.Name.Lexeme);
        base.VisitLogicalAssign(expr);
    }

    // Increment/decrement record the operand Variable (via the default traversal → VisitVariable),
    // matching how the emitter resolves it (it reads/writes through the operand variable node).

    #endregion

    #region Scope-introducing statements

    protected override void VisitBlock(Stmt.Block stmt)
    {
        PushScope();
        base.VisitBlock(stmt);
        PopScope();
    }

    protected override void VisitFor(Stmt.For stmt)
    {
        // The for-statement owns a scope for any let/const declared in its initializer.
        PushScope();
        base.VisitFor(stmt);
        PopScope();
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        Visit(stmt.Iterable);   // iterable is evaluated in the enclosing scope
        PushScope();
        CurrentScope[stmt.Variable.Lexeme] = stmt.Variable.Lexeme;   // loop var: detected, not renamed
        Visit(stmt.Body);
        PopScope();
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        Visit(stmt.Object);
        PushScope();
        CurrentScope[stmt.Variable.Lexeme] = stmt.Variable.Lexeme;
        Visit(stmt.Body);
        PopScope();
    }

    protected override void VisitSwitch(Stmt.Switch stmt)
    {
        Visit(stmt.Subject);
        PushScope();   // all cases share one block scope (ECMA-262 14.12)
        foreach (var c in stmt.Cases)
        {
            Visit(c.Value);
            foreach (var s in c.Body) Visit(s);
        }
        if (stmt.DefaultBody != null)
            foreach (var s in stmt.DefaultBody) Visit(s);
        PopScope();
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        PushScope();
        foreach (var s in stmt.TryBlock) Visit(s);
        PopScope();

        if (stmt.CatchBlock != null)
        {
            PushScope();
            if (stmt.CatchParam != null)
                CurrentScope[stmt.CatchParam.Lexeme] = stmt.CatchParam.Lexeme;   // catch param: detected, not renamed
            foreach (var s in stmt.CatchBlock) Visit(s);
            PopScope();
        }

        if (stmt.FinallyBlock != null)
        {
            PushScope();
            foreach (var s in stmt.FinallyBlock) Visit(s);
            PopScope();
        }
    }

    #endregion

    #region Nested closures

    /// <summary>
    /// Descends into a nested arrow with its own scope so a reference inside it that lexically binds to
    /// a renamed generator shadow is recorded as a capture-source pivot (#767). Declarations inside the
    /// arrow are the arrow's own bindings (resolution only, never renamed). The arrow's body compiles in
    /// its own context, so no reference node inside it is added to <see cref="_renames"/>.
    /// </summary>
    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        bool isTop = _arrowDepth == 0;
        if (isTop) _currentTopArrow = expr;
        _arrowDepth++;
        PushScope();
        foreach (var p in expr.Parameters)
            CurrentScope[p.Name.Lexeme] = p.Name.Lexeme;   // arrow params shadow but are the arrow's own
        base.VisitArrowFunction(expr);   // default traversal: param defaults + expression/block body
        PopScope();
        _arrowDepth--;
        if (isTop) _currentTopArrow = null;
    }

    // Non-arrow functions and classes rebind/lift their captures through name-keyed machinery the
    // capture pivot does not cover, so a binding they read is already OFF-LIMITS (CaptureClassifier).
    // Their interiors contribute no renames or pivots; do not descend.
    protected override void VisitFunction(Stmt.Function stmt) { }
    protected override void VisitClass(Stmt.Class stmt) { }
    protected override void VisitClassExpr(Expr.ClassExpr expr) { }

    #endregion

    /// <summary>
    /// Classifies, for each name a nested closure flows through name-keyed machinery, whether it is
    /// off-limits to renaming or renameable-but-DC-backed:
    /// <list type="bullet">
    /// <item>READ inside a non-arrow function / class, or WRITTEN under a non-arrow function / class →
    /// OFF-LIMITS (the lambda-lift path keys by the original name, which the capture pivot cannot
    /// redirect).</item>
    /// <item>WRITTEN inside an arrow only (no intervening non-arrow function / class) → WRITE-CAPTURED:
    /// renameable, with the arrow's accesses routed to the shared <c>$functionDC.&lt;storage&gt;</c>
    /// (#674/#725/#838).</item>
    /// <item>READ-only inside an arrow → renameable via the by-value snapshot pivot (#767).</item>
    /// </list>
    /// </summary>
    private sealed class CaptureClassifier : AstVisitorBase
    {
        private readonly HashSet<string> _offLimits;
        private readonly HashSet<string> _writeCaptured;
        // True when even a read by an arrow shares name-keyed storage (async functions / arrows).
        private readonly bool _arrowReadsOffLimits;
        private int _closureDepth;     // inside any closure (arrow / function / class)
        private int _nonArrowDepth;    // inside a function / class (not an arrow)
        private int _asyncArrowDepth;  // inside an async arrow (its writes use the unwired promotion path)

        public CaptureClassifier(HashSet<string> offLimits, HashSet<string> writeCaptured, bool arrowReadsOffLimits)
        {
            _offLimits = offLimits;
            _writeCaptured = writeCaptured;
            _arrowReadsOffLimits = arrowReadsOffLimits;
        }

        public void Run(List<Stmt> body)
        {
            foreach (var s in body) Visit(s);
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expr)
        {
            _closureDepth++;
            if (expr.IsAsync) _asyncArrowDepth++;
            base.VisitArrowFunction(expr);
            if (expr.IsAsync) _asyncArrowDepth--;
            _closureDepth--;
        }

        protected override void VisitFunction(Stmt.Function stmt) { Enter(); base.VisitFunction(stmt); Exit(); }
        protected override void VisitClass(Stmt.Class stmt) { Enter(); base.VisitClass(stmt); Exit(); }
        protected override void VisitClassExpr(Expr.ClassExpr expr) { Enter(); base.VisitClassExpr(expr); Exit(); }

        private void Enter() { _closureDepth++; _nonArrowDepth++; }
        private void Exit() { _closureDepth--; _nonArrowDepth--; }

        // Reads referenced by a non-arrow closure are off-limits (lambda-lift keys by name). Reads by an
        // arrow are off-limits only when the enclosing context shares read captures by name (async).
        protected override void VisitVariable(Expr.Variable expr)
        {
            if (_nonArrowDepth > 0 || (_arrowReadsOffLimits && _closureDepth > 0))
                _offLimits.Add(expr.Name.Lexeme);
        }

        // Writes inside ANY closure are off-limits (captured-and-mutated → $functionDC, keyed by name).
        protected override void VisitAssign(Expr.Assign expr)
        {
            MarkWrite(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            MarkWrite(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
        {
            MarkWrite(expr.Name.Lexeme);
            base.VisitLogicalAssign(expr);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr)
        {
            if (expr.Operand is Expr.Variable v) MarkWrite(v.Name.Lexeme);
            base.VisitPrefixIncrement(expr);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr)
        {
            if (expr.Operand is Expr.Variable v) MarkWrite(v.Name.Lexeme);
            base.VisitPostfixIncrement(expr);
        }

        // A write under a non-arrow function / class flows through the name-keyed lambda-lift path, and a
        // write inside an async arrow flows through the async-arrow promotion path (#625/#682) which is
        // NOT rename-aware — both stay off-limits. A write inside a SYNC arrow only flows through the
        // shared $functionDC, which the rename-aware DC redirects (#838), so it stays renameable.
        private void MarkWrite(string name)
        {
            if (_nonArrowDepth > 0 || _asyncArrowDepth > 0) _offLimits.Add(name);
            else if (_closureDepth > 0) _writeCaptured.Add(name);
        }
    }
}
