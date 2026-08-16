using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Detects <c>for (let/const …)</c> loop bindings that need a per-iteration
/// <b>reference cell</b> (the compiled analog of the interpreter's
/// <c>CreatePerIterationEnvironment</c>; see ECMA-262 13.7.4 and issue #650).
/// </summary>
/// <remarks>
/// The #649 fix keeps per-iteration loop bindings out of display classes so each
/// closure snapshots a fresh value — correct for the common
/// <c>for (let k …) { fns.push(() =&gt; k); }</c> case. It is NOT correct when the
/// loop body <em>mutates</em> the binding after the closure is created
/// (<c>{ i = i + 10; g.push(() =&gt; i); i = i - 10; }</c>): a value snapshot freezes
/// the mid-body value (10/11/12) instead of the binding's end-of-body value
/// (0/1/2). The fix is a per-iteration <see cref="System.Runtime.CompilerServices.StrongBox{T}"/>
/// shared by the loop body and every capturing closure: closures hold the cell by
/// reference, so they observe end-of-iteration mutations; a fresh cell is allocated
/// (value copied forward) each iteration so distinct iterations stay distinct.
///
/// <para>A binding needs a cell iff it is (a per-iteration let/const binding) ∩
/// (assigned in the loop <em>body</em>, not merely the <c>i++</c> update clause —
/// these are separate AST subtrees, so the update clause is naturally excluded) ∩
/// (captured by a closure). Bindings captured only at closure-creation but never
/// body-mutated stay on the cheap value-snapshot path.</para>
///
/// <para><b>Phase 1 (sync) scope.</b> Only the sync <c>ILEmitter.EmitFor</c> path
/// creates cells. To stay self-consistent — never letting a resolver dereference a
/// field that was not populated with a cell — this analyzer marks a binding
/// cell-eligible only when the loop and every capturing closure live in a fully
/// synchronous context (no <c>async</c>/generator boundary between the loop and any
/// capturer). Loops or captures that cross an async/generator boundary are left on
/// the existing snapshot path (Phase 2 will extend cells through state machines).</para>
/// </remarks>
public sealed class PerIterationCellAnalyzer : AstVisitorBase
{
    private sealed class LoopFrame
    {
        public required Stmt.For Loop { get; init; }
        public required HashSet<string> Bindings { get; init; }
        public int AsyncDepthAtEntry { get; init; }
        public int ClosureDepthAtEntry { get; init; }
        public HashSet<string> Assigned { get; } = [];
        public HashSet<string> CleanSyncCapture { get; } = [];
        public HashSet<string> Ineligible { get; } = [];
        public List<(object Closure, string Name)> Tentative { get; } = [];
    }

    // Stack of enclosing for-loop frames (innermost first), in source order.
    private readonly Stack<LoopFrame> _loopFrames = new();

    // Stack of enclosing closures (arrow / function declaration), innermost first.
    private readonly Stack<object> _closureStack = new();

    // Number of async/generator closures currently on the closure stack.
    private int _asyncDepth;

    /// <summary>For each cell-eligible for-loop, the binding names that get a cell.</summary>
    public Dictionary<Stmt.For, HashSet<string>> ForLoopCells { get; } =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// For each closure node (arrow / inner function), the captured names that are
    /// per-iteration cell bindings of an enclosing loop — the closure body must
    /// dereference these (load the captured field, then read <c>StrongBox.Value</c>).
    /// </summary>
    public Dictionary<object, HashSet<string>> ClosureCellFields { get; } =
        new(ReferenceEqualityComparer.Instance);

    public void Analyze(List<Stmt> statements)
    {
        foreach (var stmt in statements)
            Visit(stmt);
    }

    /// <summary>Cell bindings for a for-loop, or an empty set if none.</summary>
    public HashSet<string> GetForLoopCells(Stmt.For loop) =>
        ForLoopCells.TryGetValue(loop, out var cells) ? cells : [];

    /// <summary>Cell-captured field names for a closure node, or an empty set if none.</summary>
    public HashSet<string> GetClosureCellFields(object closure) =>
        ClosureCellFields.TryGetValue(closure, out var names) ? names : [];

    protected override void VisitFor(Stmt.For stmt)
    {
        // The increment clause is visited OUTSIDE the frame, so an `i++` update is
        // not counted as a body mutation (matching ECMA-262: the per-iteration copy
        // happens before the increment).
        if (stmt.Initializer != null) Visit(stmt.Initializer);
        if (stmt.Condition != null) Visit(stmt.Condition);
        if (stmt.Increment != null) Visit(stmt.Increment);

        var bindings = CollectLoopBindingNames(stmt.Initializer);
        // The cell is an IL local in every emitter (sync ILEmitter.EmitFor and the
        // state-machine base EmitFor). A cell-as-local is only valid when the whole
        // loop runs within a single execution segment — i.e. the loop body has no
        // DIRECT await/yield (suspension inside a nested closure is fine). A loop
        // whose body itself suspends would span multiple MoveNext calls, where a
        // local does not survive; those stay on the value-snapshot path (the cell
        // would need to be a hoisted state-machine field — deferred). In a fully
        // synchronous context there is never a direct suspension, so this is a no-op
        // for Phase 1.
        //
        // Covered emitters: sync (ILEmitter), async functions, generators, async arrows, and
        // async generators — all read/write the cell through their resolver / base store helper
        // and snapshot the cell reference in their closure-capture path.
        if (bindings == null || bindings.Count == 0 || HasDirectSuspension(stmt.Body))
        {
            Visit(stmt.Body);
            return;
        }

        var frame = new LoopFrame
        {
            Loop = stmt,
            Bindings = [.. bindings],
            AsyncDepthAtEntry = _asyncDepth,
            ClosureDepthAtEntry = _closureStack.Count,
        };
        _loopFrames.Push(frame);
        Visit(stmt.Body);
        _loopFrames.Pop();

        var cells = new HashSet<string>(frame.Bindings);
        cells.IntersectWith(frame.Assigned);
        cells.IntersectWith(frame.CleanSyncCapture);
        cells.ExceptWith(frame.Ineligible);
        if (cells.Count == 0) return;

        ForLoopCells[stmt] = cells;
        foreach (var (closure, name) in frame.Tentative)
        {
            if (!cells.Contains(name)) continue;
            if (!ClosureCellFields.TryGetValue(closure, out var set))
                ClosureCellFields[closure] = set = [];
            set.Add(name);
        }
    }

    /// <summary>
    /// Mirrors <c>Interpreter.CollectPerIterationBindings</c> / <c>ClosureAnalyzer.CollectLoopBindingNames</c>:
    /// <c>let</c>/<c>const</c> for-initializers bind per iteration; <c>var</c> and bare
    /// expression initializers share one binding.
    /// </summary>
    private static List<string>? CollectLoopBindingNames(Stmt? initializer)
    {
        switch (initializer)
        {
            case Stmt.Var v when !v.IsVar:
                return [v.Name.Lexeme];
            case Stmt.Const c:
                return [c.Name.Lexeme];
            case Stmt.Sequence seq:
                List<string>? names = null;
                foreach (var s in seq.Statements)
                {
                    var sub = CollectLoopBindingNames(s);
                    if (sub != null) (names ??= []).AddRange(sub);
                }
                return names;
            default:
                return null;
        }
    }

    // ---- Reference / assignment recording ----

    private void RecordReference(string name)
    {
        if (_closureStack.Count == 0) return; // not inside any closure → not a capture
        foreach (var frame in _loopFrames)
        {
            if (!frame.Bindings.Contains(name)) continue;
            // Innermost loop frame that declares this name owns it (shadowing).
            // Captured iff referenced from inside a closure nested in THIS loop's
            // body — i.e. a closure entered AFTER the frame was pushed (the loop's
            // own enclosing function does not count).
            if (_closureStack.Count <= frame.ClosureDepthAtEntry) return;
            var innermost = _closureStack.Peek();
            // Phase 1 wires only plain (sync, non-generator) ARROW capturers: their
            // bodies use LocalVariableResolver, where we add the cell dereference. Inner
            // function declarations have their own capture machinery (deferred), and any
            // async/generator boundary is Phase 2 — those make the binding ineligible so
            // it stays on the existing value-snapshot path (no resolver derefs a field
            // that was never populated with a cell).
            bool clean = _asyncDepth == frame.AsyncDepthAtEntry
                && innermost is Expr.ArrowFunction { IsAsync: false, IsGenerator: false };
            if (clean)
            {
                frame.CleanSyncCapture.Add(name);
                frame.Tentative.Add((innermost, name));
            }
            else
            {
                // An async/generator boundary sits between the loop and the capturer;
                // Phase 1 cannot wire a cell through it. Leave on the snapshot path.
                frame.Ineligible.Add(name);
            }
            return;
        }
    }

    private void RecordAssignment(string name)
    {
        foreach (var frame in _loopFrames)
        {
            if (!frame.Bindings.Contains(name)) continue;
            frame.Assigned.Add(name);
            return; // innermost owner
        }
    }

    protected override void VisitVariable(Expr.Variable expr)
    {
        RecordReference(expr.Name.Lexeme);
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        RecordAssignment(expr.Name.Lexeme);
        RecordReference(expr.Name.Lexeme);
        base.VisitAssign(expr);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        RecordAssignment(expr.Name.Lexeme);
        RecordReference(expr.Name.Lexeme);
        base.VisitCompoundAssign(expr);
    }

    protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
    {
        RecordAssignment(expr.Name.Lexeme);
        RecordReference(expr.Name.Lexeme);
        base.VisitLogicalAssign(expr);
    }

    protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr)
    {
        if (expr.Operand is Expr.Variable v)
        {
            RecordAssignment(v.Name.Lexeme);
            RecordReference(v.Name.Lexeme);
        }
        base.VisitPrefixIncrement(expr);
    }

    protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr)
    {
        if (expr.Operand is Expr.Variable v)
        {
            RecordAssignment(v.Name.Lexeme);
            RecordReference(v.Name.Lexeme);
        }
        base.VisitPostfixIncrement(expr);
    }

    // ---- Closure scopes ----

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        EnterClosure(expr, IsAsyncOrGen(expr));
        base.VisitArrowFunction(expr);
        ExitClosure(IsAsyncOrGen(expr));
    }

    protected override void VisitFunction(Stmt.Function stmt)
    {
        if (stmt.Body == null) { base.VisitFunction(stmt); return; }
        EnterClosure(stmt, IsAsyncOrGen(stmt));
        base.VisitFunction(stmt);
        ExitClosure(IsAsyncOrGen(stmt));
    }

    private void EnterClosure(object node, bool asyncOrGen)
    {
        _closureStack.Push(node);
        if (asyncOrGen) _asyncDepth++;
    }

    private void ExitClosure(bool asyncOrGen)
    {
        _closureStack.Pop();
        if (asyncOrGen) _asyncDepth--;
    }

    private static bool IsAsyncOrGen(object node) => node switch
    {
        Expr.ArrowFunction a => a.IsAsync || a.IsGenerator,
        Stmt.Function f => f.IsAsync || f.IsGenerator,
        _ => false,
    };

    /// <summary>
    /// True if <paramref name="body"/> contains an <c>await</c> or <c>yield</c> that would
    /// suspend the ENCLOSING state machine — i.e. not nested inside another closure. A loop
    /// with such a suspension spans multiple MoveNext calls, so its per-iteration cell could
    /// not live as a local; such loops are left on the snapshot path.
    /// </summary>
    private static bool HasDirectSuspension(Stmt body)
    {
        var v = new DirectSuspensionVisitor();
        v.Visit(body);
        return v.Found;
    }

    private sealed class DirectSuspensionVisitor : AstVisitorBase
    {
        public bool Found { get; private set; }

        protected override void VisitAwait(Expr.Await expr) => Found = true;
        protected override void VisitYield(Expr.Yield expr) => Found = true;

        // Do not descend into nested closures: their await/yield suspends THEM, not the
        // loop's enclosing state machine.
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
        protected override void VisitFunction(Stmt.Function stmt) { }
    }
}
