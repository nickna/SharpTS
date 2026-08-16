using System.Linq;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Relocates <b>non-capturing</b> nested function-like declarations to the module top level so
/// the mature top-level state-machine pipeline (<c>GeneratorStateMachineBuilder</c> /
/// <c>AsyncStateMachineBuilder</c> / <c>AsyncGeneratorStateMachineBuilder</c>) can lower them.
///
/// <para>Two shapes the in-place emitters cannot handle, and which this pass fixes by lifting:</para>
/// <list type="number">
/// <item><b>Case A</b> — a nested generator/async <c>function</c> declaration anywhere (the inner
/// function machinery would emit it as a plain method, so its <c>yield</c>/<c>await</c> fails:
/// "Yield not supported in this context"). Fixes the non-capturing half of #501.</item>
/// <item><b>Case B</b> — a plain <c>function</c> declaration whose nearest enclosing function-like
/// is itself a state machine (generator/async): the state-machine MoveNext emitter has no arm for
/// <c>Stmt.Function</c> ("Unhandled statement type in ILEmitter: Function"). Fixes #470.</item>
/// </list>
///
/// <para><b>Compile-path only.</b> Unlike <see cref="SharpTS.Parsing.GeneratorArrowLifter"/> (which
/// runs in the parser for both interpreter and compiler), this pass runs inside the IL compiler.
/// The interpreter handles nested declarations correctly via real closures, so relocating them
/// there would be a regression. Running here keeps the interpreter untouched.</para>
///
/// <para><b>Alias relocation.</b> Each lifted declaration is given a fresh, collision-proof name
/// (<c>__nestedFn_&lt;name&gt;_N</c>) and appended to the module body. A <c>var &lt;name&gt; =
/// __nestedFn_&lt;name&gt;_N;</c> alias is then inserted at the top of the original enclosing
/// statement list (so the body's references resolve to the relocated function through an ordinary
/// local binding), and into the lifted body itself when the function recurses. This avoids
/// scope-aware identifier renaming entirely — JavaScript's own scoping resolves the references via
/// the injected binding, and a nested redeclaration naturally shadows it.</para>
///
/// <para>The fresh top-level name is essential: compiled name resolution currently lets a top-level
/// <c>function</c> shadow a same-named local/parameter, so keeping the original name at module scope
/// would hijack unrelated same-named bindings elsewhere. A unique name no user code references can't.
/// The alias is a value reference to a top-level function inside the (possibly generator) enclosing
/// body — see the generator captured-field exclusion in <c>ILCompiler.Generators.cs</c> that makes
/// such references resolve correctly.</para>
///
/// <para><b>Safety guards</b> — the pass refuses to lift (the declaration stays nested and fails to
/// compile exactly as before — a clean failure, never a miscompile) when:</para>
/// <list type="bullet">
/// <item><b>Capturing</b> (#583 §1): a free variable resolves to an enclosing function scope
/// (<see cref="ClosureAnalyzer.GetCaptureSource"/> non-null). Moving it to the top level would
/// break the closure. The function's own name (recursion) is not treated as a capture.</item>
/// <item><b>Inside a namespace</b> (#583 §3): a namespace member is reachable by bare name, so
/// relocating a body out of the namespace would change how the names it references resolve. The
/// pass never descends into a <see cref="Stmt.Namespace"/>.</item>
/// <item><b>Name collides with a top-level binding</b>: the injected <c>var &lt;name&gt;</c> alias
/// would be hijacked by a same-named top-level <c>function</c> (the resolution quirk above), so a
/// candidate whose name matches an existing top-level binding is left nested.</item>
/// </list>
///
/// <para><b>Known limitation (#583 §2):</b> a relocated declaration becomes a single top-level
/// function, so its identity is shared across separate invocations of the enclosing function rather
/// than fresh per call. Same class of limitation as <see cref="GeneratorArrowLifter"/> / #534.</para>
/// </summary>
internal sealed class NestedFunctionLifter
{
    private readonly ClosureAnalyzer _analyzer;
    private readonly HashSet<Stmt.Function> _safeCandidates;
    private readonly Dictionary<Stmt.Function, List<string>> _lambdaForwards;
    /// <summary>
    /// The subset of <see cref="_lambdaForwards"/> that capture an enclosing FUNCTION scope (#583 §1 /
    /// #534), as opposed to a module-level block/loop binding (#622). A function-scope capture is
    /// read live (by reference) through the enclosing function's display class at call time, never by
    /// value at arrow creation, so its forwarding binding is HOISTED to the top of the body (like a
    /// real function declaration). A module-block/loop capture instead stays in place so each loop
    /// iteration rebuilds a fresh arrow over that iteration's binding.
    /// </summary>
    private readonly HashSet<Stmt.Function> _hoistedForwards;
    private readonly List<Stmt.Function> _lifted = new();
    private int _counter;

    // Source positions of the AST being rewritten, when the caller is tracking them.
    private SharpTS.Parsing.SpanTable? _spans;

    private NestedFunctionLifter(
        ClosureAnalyzer analyzer,
        HashSet<Stmt.Function> safeCandidates,
        Dictionary<Stmt.Function, List<string>> lambdaForwards,
        HashSet<Stmt.Function> hoistedForwards)
    {
        _analyzer = analyzer;
        _safeCandidates = safeCandidates;
        _lambdaForwards = lambdaForwards;
        _hoistedForwards = hoistedForwards;
    }

    /// <summary>
    /// Returns a statement list with all liftable non-capturing nested function-likes relocated to
    /// the top level. Returns the input list unchanged (by reference) when nothing needs lifting,
    /// so untouched programs pay only a cheap structural pre-scan.
    /// </summary>
    /// <param name="spans">
    /// When supplied, statements rebuilt around a relocated declaration inherit the position of the
    /// statements they replace, and the <c>var</c> aliases left behind are marked
    /// compiler-generated.
    /// </param>
    public static List<Stmt> Lift(List<Stmt> module, SharpTS.Parsing.SpanTable? spans = null)
    {
        // Cheap structural pre-scan: collect declarations whose SHAPE qualifies (case A/B, or a
        // declaration inside a module-level block/loop), without running closure analysis. The
        // overwhelmingly common module has none, so we return early. We iterate the module statements
        // directly (not via CollectShapeCandidates) because module top-level declarations are NOT
        // block/loop bindings — they stay reachable after a lift — so they must not seed the
        // enclosing-binding set the capture guard checks against.
        var scan = new ShapeScan();
        foreach (var stmt in module)
            CollectShapeCandidatesStmt(stmt, enclosingIsStateMachine: false, insideFunction: false, insideModuleBlock: false, enclosingBlockBindings: [], scan);
        if (scan.Candidates.Count == 0) return module;

        // Capture analysis is needed to tell a safe (module/global) reference from an unsafe
        // enclosing-scope capture. Run our own pass on the original AST; the main pipeline
        // re-analyses the transformed AST in Phase 2.
        var analyzer = new ClosureAnalyzer();
        analyzer.Analyze(module);

        // A candidate is liftable when it captures nothing from an enclosing FUNCTION scope.
        var reservedTopLevelNames = CollectTopLevelBindingNames(module);
        var safe = new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);
        // Module-block declarations that CAPTURE an enclosing block/loop binding are lambda-lifted:
        // each captured binding becomes a leading parameter of the relocated top-level declaration,
        // forwarded by an in-place arrow that closes over it (see LambdaLiftCandidate). Maps such a
        // function to the ordered list of capture names to forward.
        var lambdaForwards = new Dictionary<Stmt.Function, List<string>>(ReferenceEqualityComparer.Instance);
        // The subset of lambdaForwards that capture an enclosing FUNCTION scope (#534/#583 §1): their
        // forwarding binding is hoisted to the body top (matching function-declaration hoisting) so a
        // forward reference resolves. Module-block/loop captures (#622) are NOT added here — they stay
        // in place for per-iteration freshness.
        var hoistedForwards = new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);
        foreach (var f in scan.Candidates)
        {
            bool isModuleBlock = scan.ModuleBlockEnclosingBindings.TryGetValue(f, out var blockBindings);

            // A reference into an intermediate FUNCTION scope is a real closure capture (#583 §1). Only
            // an INSIDE-FUNCTION candidate can have one (a module-block candidate has no enclosing
            // function). Lambda-lift it: the captured function-scope bindings (and the function's own
            // name when it recurses) become leading parameters of the relocated top-level declaration,
            // forwarded by an in-place arrow that closes over them. The arrow may sit inside a generator/
            // async body, which now binds captured display instances correctly (see
            // GeneratorMoveNextEmitter.EmitArrowFunction). Declines (leaves nested — a clean failure) for
            // bodies using this/arguments or rest/default params, which the forwarding arrow can't carry.
            if (!IsNonCapturing(analyzer, f))
            {
                if (!isModuleBlock && TryComputeFunctionCaptureForward(analyzer, f, out var fnForwarded))
                {
                    lambdaForwards[f] = fnForwarded;
                    hoistedForwards.Add(f);
                }
                continue;
            }

            // A module-block candidate that captures an enclosing block/loop binding (e.g. a
            // generator in a `for` reading the loop variable) can't move to module top level as-is —
            // that name doesn't exist there. Lambda-lift it: the captured bindings become leading
            // parameters of the relocated declaration, forwarded by an in-place arrow that closes
            // over them. The compiler cannot emit a generator/async that captures locals, so this
            // declaration-with-parameters form is the only route that handles all three function
            // kinds uniformly (#622). The type checker has already rejected any reference to a
            // captured binding before its declaration (or to the function before its own), so the
            // arrow's snapshot of each capture at its in-place position is always well-defined.
            if (isModuleBlock && CapturesAnyOf(analyzer, f, blockBindings!))
            {
                if (TryComputeLambdaForward(analyzer, f, blockBindings!, out var forwarded))
                    lambdaForwards[f] = forwarded;
                // Otherwise leave nested — a clean failure, never a miscompile.
                continue;
            }

            // The injected alias for a MODULE-BLOCK candidate is a `var` that hoists to module
            // scope, so a same-named top-level binding would collide with it — keep declining those.
            // An INSIDE-FUNCTION candidate's alias is function-scoped and correctly shadows a
            // same-named top-level function now that in-scope locals win that resolution (#607),
            // so the name-collision guard is unnecessary there: without this relaxation a liftable
            // nested generator/async whose name matched a top-level binding failed to compile with
            // "Yield not supported in this context" instead of being lifted.
            if (isModuleBlock && reservedTopLevelNames.Contains(f.Name.Lexeme)) continue;
            safe.Add(f);
        }
        if (safe.Count == 0 && lambdaForwards.Count == 0) return module;

        var lifter = new NestedFunctionLifter(analyzer, safe, lambdaForwards, hoistedForwards) { _spans = spans };
        var rewritten = lifter.ProcessTopLevel(module);
        if (lifter._lifted.Count == 0) return module;

        // Append lifted declarations (they hoist, so trailing position is runtime-equivalent and
        // lets the source-order type checker see any module-level bindings the body reads first).
        var result = new List<Stmt>(rewritten.Count + lifter._lifted.Count);
        result.AddRange(rewritten);
        result.AddRange(lifter._lifted);
        return result;
    }

    /// <summary>
    /// True when the SHAPE of <paramref name="f"/> requires lifting: it is itself a generator/async
    /// (case A), or it is a plain function whose nearest enclosing function-like is a state machine
    /// (case B). Overload signatures (no body) never qualify.
    /// </summary>
    private static bool IsCandidateShape(Stmt.Function f, bool enclosingIsStateMachine)
        => f.Body != null && (f.IsGenerator || f.IsAsync || enclosingIsStateMachine);

    /// <summary>
    /// A declaration is liftable only if every free variable resolves to module/global scope. A
    /// reference into an intermediate function scope is a real closure capture (#583 §1) and blocks
    /// lifting. The function's own name is excluded — recursion resolves through the self-alias the
    /// lifter injects into the relocated body, not a captured outer binding.
    /// </summary>
    private static bool IsNonCapturing(ClosureAnalyzer analyzer, Stmt.Function f)
    {
        foreach (var captured in analyzer.GetCaptures(f))
        {
            if (captured == f.Name.Lexeme) continue;
            if (analyzer.GetCaptureSource(f, captured) != null) return false;
        }
        return true;
    }

    /// <summary>True if <paramref name="f"/> captures any name in <paramref name="names"/> (its own
    /// name excluded — recursion is handled by the self-alias, not a captured binding).</summary>
    private static bool CapturesAnyOf(ClosureAnalyzer analyzer, Stmt.Function f, HashSet<string> names)
    {
        foreach (var captured in analyzer.GetCaptures(f))
        {
            if (captured == f.Name.Lexeme) continue;
            if (names.Contains(captured)) return true;
        }
        return false;
    }

    /// <summary>
    /// Decides whether a module-block declaration that captures enclosing block/loop bindings can be
    /// lambda-lifted, and if so produces the ordered list of capture names to forward as leading
    /// parameters. Declines (returns false — the declaration stays nested, a clean failure) when the
    /// forwarding arrow cannot faithfully reproduce the call:
    /// <list type="bullet">
    /// <item>rest or default parameters — forwarding them through the arrow miscompiles (spread call
    /// args and expression-body arrow defaults are not yet reliable);</item>
    /// <item>the body uses <c>this</c> or <c>arguments</c> — a plain top-level function reached
    /// through an arrow has neither the original receiver nor the original argument list.</item>
    /// </list>
    /// The forwarded set is exactly the captured names that resolve to an enclosing block/loop
    /// binding (a module top-level binding is reachable by the relocated function directly, so it is
    /// not forwarded). The function's own name is included when it recurses, so the relocated body's
    /// self-calls resolve to the forwarded arrow.
    /// </summary>
    private static bool TryComputeLambdaForward(
        ClosureAnalyzer analyzer, Stmt.Function f, HashSet<string> blockBindings, out List<string> forwarded)
    {
        forwarded = [];

        foreach (var p in f.Parameters)
            if (p.IsRest || p.DefaultValue != null)
                return false;

        if (DeclinesForThisOrArguments(f))
            return false;

        // Ordinal sort gives a deterministic parameter order shared by the relocated declaration's
        // leading parameters and the arrow's leading call arguments.
        forwarded = analyzer.GetCaptures(f)
            .Where(blockBindings.Contains)
            .OrderBy(c => c, System.StringComparer.Ordinal)
            .ToList();
        return forwarded.Count > 0;
    }

    /// <summary>
    /// The inside-function analogue of <see cref="TryComputeLambdaForward"/> (#583 §1): produces the
    /// ordered list of captures to forward as leading parameters when relocating a nested declaration
    /// that captures an enclosing FUNCTION scope. The forwarded set is every free variable resolving to
    /// such a scope — including the function's own name when it recurses, so the relocated body's
    /// self-calls resolve to the forwarded arrow (which closes over its own <c>let</c> binding). Declines
    /// (returns false → stays nested, a clean failure) on rest/default parameters or a body using
    /// <c>this</c>/<c>arguments</c>, exactly as the module-block path does.
    /// </summary>
    private static bool TryComputeFunctionCaptureForward(
        ClosureAnalyzer analyzer, Stmt.Function f, out List<string> forwarded)
    {
        forwarded = [];

        foreach (var p in f.Parameters)
            if (p.IsRest || p.DefaultValue != null)
                return false;

        if (DeclinesForThisOrArguments(f))
            return false;

        // Self-recursion can't be lambda-lifted here: the relocated body's self-calls must resolve to the
        // forwarding arrow, but a compiled arrow snapshots its captures by value — and the arrow's own
        // `let` binding is still in its temporal dead zone when the arrow is created, so it would capture
        // an unassigned (null) self and crash on the first recursive call. Leave such a declaration nested
        // (a clean "not supported" failure, never a miscompile). Non-recursive captures lift fine.
        if (analyzer.GetCaptures(f).Contains(f.Name.Lexeme))
            return false;

        forwarded = analyzer.GetCaptures(f)
            .Where(c => analyzer.GetCaptureSource(f, c) != null)
            .OrderBy(c => c, System.StringComparer.Ordinal)
            .ToList();
        return forwarded.Count > 0;
    }

    /// <summary>
    /// Whether a candidate must be declined because its body reads <c>this</c> or <c>arguments</c> that
    /// a plain top-level function reached through a forwarding arrow could not supply. A non-generator
    /// declines on either. A GENERATOR declines only on <c>arguments</c> (#775): a <c>function*</c>
    /// expression binds its own dynamic <c>this</c>, and the compiled free-function generator stub
    /// threads that receiver in via the thread-local <c>$TSFunction._currentFunctionThis</c> (captured
    /// into <c>&lt;&gt;4__this</c> at creation), so a <c>this</c>-using generator body lambda-lifts fine.
    /// </summary>
    private static bool DeclinesForThisOrArguments(Stmt.Function f) =>
        f.IsGenerator ? UsesThisOrArguments(f.Body, includeThis: false)
                      : UsesThisOrArguments(f.Body, includeThis: true);

    /// <summary>
    /// True if any statement in <paramref name="body"/> reads <c>arguments</c> (and, when
    /// <paramref name="includeThis"/> is set, <c>this</c>). Deliberately over-approximates: it descends
    /// through nested function/arrow boundaries (which rebind both), so a nested function's own
    /// <c>this</c>/<c>arguments</c> also trips it. A false positive only declines a lambda-lift (a clean
    /// failure), never miscompiles.
    /// </summary>
    private static bool UsesThisOrArguments(List<Stmt>? body, bool includeThis = true)
    {
        if (body == null) return false;
        var scanner = new ThisArgumentsScanner(includeThis);
        foreach (var stmt in body)
        {
            scanner.Visit(stmt);
            if (scanner.Found) return true;
        }
        return scanner.Found;
    }

    private sealed class ThisArgumentsScanner(bool includeThis) : Parsing.Visitors.AstVisitorBase
    {
        public bool Found { get; private set; }

        protected override void VisitThis(Expr.This expr)
        {
            if (!includeThis) return;
            Found = true;
            ShouldContinue = false;
        }

        protected override void VisitVariable(Expr.Variable expr)
        {
            if (expr.Name.Lexeme == "arguments")
            {
                Found = true;
                ShouldContinue = false;
            }
        }
    }

    /// <summary>Accumulates the structural candidate scan results.</summary>
    private sealed class ShapeScan
    {
        public readonly List<Stmt.Function> Candidates = [];
        /// <summary>For module-level-block candidates only: the block/loop-scoped binding names in
        /// scope around the declaration. A candidate capturing any of these can't be lifted to
        /// module scope (the names don't exist there).</summary>
        public readonly Dictionary<Stmt.Function, HashSet<string>> ModuleBlockEnclosingBindings = new(ReferenceEqualityComparer.Instance);
    }

    #region Structural candidate collection (read-only, no closure analysis)

    private static void CollectShapeCandidates(List<Stmt> body, bool enclosingIsStateMachine, bool insideFunction, bool insideModuleBlock, HashSet<string> enclosingBlockBindings, ShapeScan scan)
    {
        // A statement list opens a lexical scope: its own declarations are visible to nested
        // functions, so add them to the enclosing-binding set used by the module-block capture
        // guard. Only tracked at module level — inside a function the inner-function machinery
        // resolves captures itself.
        var bindings = !insideFunction ? WithBlockBindings(enclosingBlockBindings, body) : enclosingBlockBindings;
        foreach (var stmt in body)
            CollectShapeCandidatesStmt(stmt, enclosingIsStateMachine, insideFunction, insideModuleBlock, bindings, scan);
    }

    private static void CollectShapeCandidatesStmt(Stmt stmt, bool enclosingIsStateMachine, bool insideFunction, bool insideModuleBlock, HashSet<string> enclosingBlockBindings, ShapeScan scan)
    {
        switch (stmt)
        {
            case Stmt.Function f when f.Body != null:
                // (1) A declaration nested INSIDE a function-like whose shape needs the top-level
                // state-machine pipeline (gen/async, or a plain fn inside a state machine). Captures
                // into the enclosing function are tracked correctly and blocked by IsNonCapturing.
                if (insideFunction && IsCandidateShape(f, enclosingIsStateMachine))
                    scan.Candidates.Add(f);
                // (2) Any function/generator/async declared directly inside a module-level block,
                // loop, or `if` (no enclosing function): it is bound by neither the top-level
                // definition pass (which doesn't recurse into blocks) nor the inner-function pass
                // (which only fires inside a function), so a reference throws "Undefined variable"
                // in compiled mode (#605). Record it with the block/loop bindings in scope so Lift
                // can drop it if it captures one of them (a clean failure, never a miscompile).
                else if (!insideFunction && insideModuleBlock)
                {
                    scan.Candidates.Add(f);
                    scan.ModuleBlockEnclosingBindings[f] = enclosingBlockBindings;
                }
                // A nested function's own body establishes a fresh enclosing kind for its children.
                CollectShapeCandidates(f.Body, f.IsGenerator || f.IsAsync, insideFunction: true, insideModuleBlock: false, enclosingBlockBindings, scan);
                break;
            case Stmt.Block b:
                CollectShapeCandidates(b.Statements, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, enclosingBlockBindings, scan);
                break;
            case Stmt.Sequence s:
                CollectShapeCandidates(s.Statements, enclosingIsStateMachine, insideFunction, insideModuleBlock, enclosingBlockBindings, scan);
                break;
            case Stmt.If i:
                CollectShapeCandidatesStmt(i.ThenBranch, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, enclosingBlockBindings, scan);
                if (i.ElseBranch != null) CollectShapeCandidatesStmt(i.ElseBranch, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, enclosingBlockBindings, scan);
                break;
            case Stmt.While w:
                CollectShapeCandidatesStmt(w.Body, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, enclosingBlockBindings, scan);
                break;
            case Stmt.DoWhile d:
                CollectShapeCandidatesStmt(d.Body, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, enclosingBlockBindings, scan);
                break;
            case Stmt.For fo:
                // The loop variable is scoped to the loop body — add it to the bindings so a body
                // declaration that captures it (the #605 `for (let k…) { function* g(){ yield k } }`
                // case) is recognized as capturing and left nested.
                var forBindings = !insideFunction ? WithDeclaration(enclosingBlockBindings, fo.Initializer) : enclosingBlockBindings;
                if (fo.Initializer != null) CollectShapeCandidatesStmt(fo.Initializer, enclosingIsStateMachine, insideFunction, insideModuleBlock, enclosingBlockBindings, scan);
                CollectShapeCandidatesStmt(fo.Body, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, forBindings, scan);
                break;
            case Stmt.ForOf fof:
                var forOfBindings = !insideFunction ? WithName(enclosingBlockBindings, fof.Variable.Lexeme) : enclosingBlockBindings;
                CollectShapeCandidatesStmt(fof.Body, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, forOfBindings, scan);
                break;
            case Stmt.ForIn fin:
                var forInBindings = !insideFunction ? WithName(enclosingBlockBindings, fin.Variable.Lexeme) : enclosingBlockBindings;
                CollectShapeCandidatesStmt(fin.Body, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, forInBindings, scan);
                break;
            case Stmt.LabeledStatement l:
                CollectShapeCandidatesStmt(l.Statement, enclosingIsStateMachine, insideFunction, insideModuleBlock, enclosingBlockBindings, scan);
                break;
            case Stmt.TryCatch t:
                CollectShapeCandidates(t.TryBlock, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, enclosingBlockBindings, scan);
                if (t.CatchBlock != null)
                {
                    var catchBindings = !insideFunction ? WithName(enclosingBlockBindings, t.CatchParam?.Lexeme) : enclosingBlockBindings;
                    CollectShapeCandidates(t.CatchBlock, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, catchBindings, scan);
                }
                if (t.FinallyBlock != null) CollectShapeCandidates(t.FinallyBlock, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, enclosingBlockBindings, scan);
                break;
            case Stmt.Switch sw:
                foreach (var c in sw.Cases) CollectShapeCandidates(c.Body, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, enclosingBlockBindings, scan);
                if (sw.DefaultBody != null) CollectShapeCandidates(sw.DefaultBody, enclosingIsStateMachine, insideFunction, insideModuleBlock: !insideFunction, enclosingBlockBindings, scan);
                break;
            case Stmt.Class cls:
                // Method bodies are function-likes regardless of where the class sits.
                foreach (var m in cls.Methods)
                    if (m.Body != null) CollectShapeCandidates(m.Body, m.IsGenerator || m.IsAsync, insideFunction: true, insideModuleBlock: false, enclosingBlockBindings, scan);
                break;
            case Stmt.Export ex when ex.Declaration != null:
                CollectShapeCandidatesStmt(ex.Declaration, enclosingIsStateMachine, insideFunction, insideModuleBlock, enclosingBlockBindings, scan);
                break;
            // Stmt.Namespace is intentionally NOT traversed (#583 §3 lift barrier).
        }
    }

    /// <summary>Returns <paramref name="current"/> extended with the names declared directly in
    /// <paramref name="blockStmts"/> (a new set only when something is added).</summary>
    private static HashSet<string> WithBlockBindings(HashSet<string> current, List<Stmt> blockStmts)
    {
        HashSet<string>? added = null;
        foreach (var s in blockStmts)
        {
            var name = DeclaredName(s);
            if (name != null && !current.Contains(name))
                (added ??= new HashSet<string>(current)).Add(name);
        }
        return added ?? current;
    }

    private static HashSet<string> WithDeclaration(HashSet<string> current, Stmt? decl)
        => decl == null ? current : WithName(current, DeclaredName(decl));

    private static HashSet<string> WithName(HashSet<string> current, string? name)
        => name == null || current.Contains(name) ? current : new HashSet<string>(current) { name };

    /// <summary>The single binding name a declaration statement introduces, or null.</summary>
    private static string? DeclaredName(Stmt stmt) => stmt switch
    {
        Stmt.Function f => f.Name.Lexeme,
        Stmt.Class c => c.Name.Lexeme,
        Stmt.Var v => v.Name.Lexeme,
        Stmt.Const co => co.Name.Lexeme,
        Stmt.Enum e => e.Name.Lexeme,
        Stmt.Export { Declaration: not null } ex => DeclaredName(ex.Declaration),
        _ => null
    };

    #endregion

    #region Transform (extracts safe candidates, injects aliases)

    /// <summary>
    /// Processes the module body. Top-level declarations are never lifted (already at module scope),
    /// but their bodies are walked so nested declarations can be extracted.
    /// </summary>
    private List<Stmt> ProcessTopLevel(List<Stmt> module)
    {
        var result = new List<Stmt>(module.Count);
        bool changed = false;
        foreach (var stmt in module)
        {
            var rewritten = ProcessStmt(stmt, enclosingIsStateMachine: false, enclosingIsAsyncFunction: false, enclosingIsGeneratorClassMethod: false);
            if (!ReferenceEquals(rewritten, stmt)) changed = true;
            result.Add(rewritten);
        }
        return changed ? result : module;
    }

    /// <summary>
    /// Processes a statement list (a function body or block). Safe nested function-likes are moved to
    /// the module top level under a fresh name and replaced, at the top of this list, by a
    /// <c>var &lt;name&gt; = &lt;freshName&gt;;</c> alias so references in this scope still resolve.
    /// </summary>
    private List<Stmt> ProcessBody(List<Stmt> body, bool enclosingIsStateMachine, bool enclosingIsAsyncFunction, bool enclosingIsGeneratorClassMethod)
    {
        List<Stmt>? result = null;
        List<Stmt>? aliases = null;
        for (int i = 0; i < body.Count; i++)
        {
            var stmt = body[i];

            if (stmt is Stmt.Function f && f.Body != null)
            {
                if (_safeCandidates.Contains(f))
                {
                    // Non-capturing relocation: hoist a `var name = freshName;` alias to the top of
                    // this body (function declarations hoist, so the alias must too).
                    aliases ??= new List<Stmt>();
                    aliases.Add(LiftCandidate(f));
                    result ??= new List<Stmt>(body.GetRange(0, i));
                    continue; // drop the declaration from this body
                }
                if (_lambdaForwards.TryGetValue(f, out var forwarded))
                {
                    // Capturing relocation: replace the declaration with a forwarding arrow.
                    //
                    // Function-scope captures (#534/#583 §1) whose enclosing function is a PLAIN
                    // function OR a plain ASYNC function are HOISTED to the top of this body (alongside
                    // non-capturing aliases): there the forwarding arrow reads its captures live (by
                    // reference) at call time — an async function routes a nested arrow's captures through
                    // a shared function display class (ILCompiler.WireAsyncMethodFunctionDC), exactly like
                    // a plain function, so an earlier creation position is harmless — and hoisting matches
                    // function-declaration hoisting, so the forward reference the GeneratorArrowLifter
                    // creates (it appends the lifted `function* __genArrow_N` at body END) resolves. The
                    // async-function case (#924) is the async analog of the plain-function #534 fix.
                    //
                    // When the enclosing function is a top-level / module-level / nested-in-plain-function
                    // GENERATOR (sync `function*` or `async function*`), the binding is ALSO hoisted (#945):
                    // such a generator wires a shared function display class (#674), and the forwarding
                    // arrow — marked IsLiftedForwarder below — has its read-only forwarded captures routed
                    // through that DC (see ComputeMutatedCapturedGeneratorVars), so it reads them live at
                    // call time and the earlier hoisted creation position is harmless. The generator-
                    // encloser case (#945) is the generator analog of the async-function #924 fix.
                    //
                    // When the enclosing function is a class GENERATOR METHOD (sync or async, static or
                    // instance), keep the binding in place: a static generator method has no function
                    // display class at all and an instance one wires it only for write-captures, so a
                    // hoisted forwarding arrow would snapshot the captured local by value at creation and
                    // read a stale value. Those decline cleanly (a clean failure, never a miscompile).
                    // Module-block/loop captures (#622) likewise stay in place so each loop iteration
                    // rebuilds a fresh arrow over that iteration's binding.
                    result ??= new List<Stmt>(body.GetRange(0, i));
                    bool hoist = _hoistedForwards.Contains(f) && !enclosingIsGeneratorClassMethod;
                    var binding = LambdaLiftCandidate(f, forwarded, hoisted: hoist);
                    if (hoist)
                        (aliases ??= new List<Stmt>()).Add(binding);
                    else
                        result.Add(binding);
                    continue;
                }
            }

            var rewritten = ProcessStmt(stmt, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
            if (result != null)
                result.Add(rewritten);
            else if (!ReferenceEquals(rewritten, stmt))
                result = new List<Stmt>(body.GetRange(0, i)) { rewritten };
        }

        if (result == null) return body;
        if (aliases != null) result.InsertRange(0, aliases);
        return result;
    }

    /// <summary>
    /// Relocates <paramref name="f"/> to the module top level under a fresh name (recursing first so
    /// its own nested candidates are extracted) and returns the <c>var</c> alias that should stand in
    /// for it in the original scope.
    /// </summary>
    private Stmt LiftCandidate(Stmt.Function f)
    {
        var freshName = $"__nestedFn_{f.Name.Lexeme}_{_counter++}";
        var freshToken = new Token(TokenType.IDENTIFIER, freshName, null, f.Name.Line);

        // The relocated function becomes a top-level declaration, so it is never a class method.
        var liftedBody = ProcessBody(f.Body!, f.IsGenerator || f.IsAsync, f.IsAsync && !f.IsGenerator, enclosingIsGeneratorClassMethod: false);

        // Recursion: the relocated body still calls itself by the original name. Bind that name to
        // the fresh declaration with a self-alias at the top of the body.
        if (_analyzer.GetCaptures(f).Contains(f.Name.Lexeme))
        {
            var withSelfAlias = new List<Stmt>(liftedBody.Count + 1) { MakeAlias(f.Name, freshToken) };
            withSelfAlias.AddRange(liftedBody);
            liftedBody = withSelfAlias;
        }

        _lifted.Add(f with { Name = freshToken, Body = liftedBody });
        return MakeAlias(f.Name, freshToken);
    }

    /// <summary>Builds <c>var &lt;original&gt; = &lt;fresh&gt;;</c>.</summary>
    private Stmt MakeAlias(Token original, Token fresh)
    {
        var alias = new Stmt.Var(original, TypeAnnotation: null, Initializer: new Expr.Variable(fresh), IsVar: true);
        _spans?.MarkHidden(alias);
        return alias;
    }

    /// <summary>
    /// Lambda-lifts a capturing declaration: relocates it to a top-level declaration whose leading
    /// parameters are the captured bindings (<paramref name="forwarded"/>), and returns a
    /// <c>&lt;name&gt; = (&lt;params&gt;) =&gt; &lt;fresh&gt;(&lt;captures&gt;, &lt;params&gt;);</c> arrow
    /// that stands in for it. The arrow closes over the captured bindings and forwards them, so a
    /// generator/async relocated this way — which the compiler cannot emit as a capturing closure
    /// directly — still observes its captures.
    ///
    /// <para>When <paramref name="hoisted"/> is true (a function-scope capture, #534/#583 §1), the
    /// binding is a function-scoped <c>var</c> the caller hoists to the body top, matching
    /// function-declaration hoisting so a forward reference resolves; the arrow reads its captures live
    /// at call time, so the earlier creation position is harmless. When false (a module-level block/loop
    /// capture, #622), it is a block-scoped <c>let</c> the caller leaves in place, so each loop
    /// iteration rebuilds a fresh arrow over that iteration's bindings.</para>
    /// </summary>
    private Stmt LambdaLiftCandidate(Stmt.Function f, List<string> forwarded, bool hoisted)
    {
        var freshName = $"__nestedFn_{f.Name.Lexeme}_{_counter++}";
        var freshToken = new Token(TokenType.IDENTIFIER, freshName, null, f.Name.Line);

        // Recurse first so nested candidates inside the relocated body are also handled. The relocated
        // function becomes a top-level declaration, so it is never a class method.
        var liftedBody = ProcessBody(f.Body!, f.IsGenerator || f.IsAsync, f.IsAsync && !f.IsGenerator, enclosingIsGeneratorClassMethod: false);

        // Relocated declaration: captured bindings become leading (untyped) parameters, followed by
        // the original parameters. Body references to the captured names now resolve to these
        // parameters, so the body needs no renaming.
        var liftedParams = new List<Stmt.Parameter>(forwarded.Count + f.Parameters.Count);
        foreach (var name in forwarded)
            liftedParams.Add(new Stmt.Parameter(new Token(TokenType.IDENTIFIER, name, null, f.Name.Line), Type: null));
        foreach (var p in f.Parameters)
            liftedParams.Add(p with { });
        _lifted.Add(f with { Name = freshToken, Parameters = liftedParams, Body = liftedBody });

        // Forwarding arrow: original parameters in, captured bindings + those parameters forwarded
        // to the relocated declaration. Not a generator/async itself — it returns whatever the
        // relocated declaration produces (the iterator for a generator, the promise for async).
        var callArgs = new List<Expr>(forwarded.Count + f.Parameters.Count);
        foreach (var name in forwarded)
            callArgs.Add(new Expr.Variable(new Token(TokenType.IDENTIFIER, name, null, f.Name.Line)));
        foreach (var p in f.Parameters)
            callArgs.Add(new Expr.Variable(p.Name));

        var call = new Expr.Call(
            new Expr.Variable(freshToken),
            new Token(TokenType.LEFT_PAREN, "(", null, f.Name.Line),
            TypeArgs: null,
            Arguments: callArgs);

        var arrow = new Expr.ArrowFunction(
            Name: null,
            TypeParams: null,
            ThisType: null,
            Parameters: [.. f.Parameters.Select(p => p with { })],
            ExpressionBody: call,
            BlockBody: null,
            ReturnType: null,
            HasOwnThis: false,
            IsAsync: false,
            IsGenerator: false)
        {
            // #945: when this forwarder is HOISTED into a generator encloser's body, mark it so the
            // generator function-DC pass routes its read-only forwarded captures through the shared
            // display class (live read), not a stale by-value snapshot. Only hoisted forwarders are
            // marked — class-method (declined) and module-block/loop (#622) forwarders stay unmarked.
            IsLiftedForwarder = hoisted,
        };

        // Function-scope capture (#534): a `var` the caller hoists to the body top, matching
        // function-declaration hoisting. Module-block/loop capture (#622): a block-scoped `let` left
        // in place, re-bound per loop iteration.
        return new Stmt.Var(f.Name, TypeAnnotation: null, Initializer: arrow, IsVar: hoisted);
    }

    /// <summary>
    /// Rewrites a statement, carrying its source position onto whatever replaces it.
    /// </summary>
    /// <remarks>
    /// Relocating a nested function rebuilds every statement enclosing it. Those rebuilt statements
    /// are still the user's code, so provenance is copied at this single point rather than at each
    /// production below.
    /// </remarks>
    private Stmt ProcessStmt(Stmt stmt, bool enclosingIsStateMachine, bool enclosingIsAsyncFunction, bool enclosingIsGeneratorClassMethod)
    {
        Stmt rewritten = ProcessStmtCore(stmt, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
        _spans?.CopySpan(stmt, rewritten);
        return rewritten;
    }

    private Stmt ProcessStmtCore(Stmt stmt, bool enclosingIsStateMachine, bool enclosingIsAsyncFunction, bool enclosingIsGeneratorClassMethod)
    {
        switch (stmt)
        {
            case Stmt.Function f when f.Body != null:
            {
                // Not lifted (not a safe candidate), but its body may contain nested candidates.
                // A nested function declaration is never a class method, so the flag resets to false.
                var nb = ProcessBody(f.Body, f.IsGenerator || f.IsAsync, f.IsAsync && !f.IsGenerator, enclosingIsGeneratorClassMethod: false);
                return ReferenceEquals(nb, f.Body) ? f : f with { Body = nb };
            }
            case Stmt.Block b:
            {
                var nb = ProcessBody(b.Statements, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                return ReferenceEquals(nb, b.Statements) ? b : new Stmt.Block(nb);
            }
            case Stmt.Sequence s:
            {
                var nb = ProcessBody(s.Statements, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                return ReferenceEquals(nb, s.Statements) ? s : new Stmt.Sequence(nb);
            }
            case Stmt.If i:
            {
                var nt = ProcessStmt(i.ThenBranch, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                var ne = i.ElseBranch != null ? ProcessStmt(i.ElseBranch, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod) : null;
                if (ReferenceEquals(nt, i.ThenBranch) && (i.ElseBranch == null || ReferenceEquals(ne, i.ElseBranch)))
                    return i;
                return new Stmt.If(i.Condition, nt, ne);
            }
            case Stmt.While w:
            {
                var nb = ProcessStmt(w.Body, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                return ReferenceEquals(nb, w.Body) ? w : new Stmt.While(w.Condition, nb);
            }
            case Stmt.DoWhile d:
            {
                var nb = ProcessStmt(d.Body, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                return ReferenceEquals(nb, d.Body) ? d : new Stmt.DoWhile(nb, d.Condition);
            }
            case Stmt.For fo:
            {
                var ni = fo.Initializer != null ? ProcessStmt(fo.Initializer, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod) : null;
                var nb = ProcessStmt(fo.Body, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                if ((fo.Initializer == null || ReferenceEquals(ni, fo.Initializer)) && ReferenceEquals(nb, fo.Body))
                    return fo;
                return new Stmt.For(ni, fo.Condition, fo.Increment, nb);
            }
            case Stmt.ForOf fof:
            {
                var nb = ProcessStmt(fof.Body, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                return ReferenceEquals(nb, fof.Body) ? fof : fof with { Body = nb };
            }
            case Stmt.ForIn fin:
            {
                var nb = ProcessStmt(fin.Body, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                return ReferenceEquals(nb, fin.Body) ? fin : fin with { Body = nb };
            }
            case Stmt.LabeledStatement l:
            {
                var ni = ProcessStmt(l.Statement, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                return ReferenceEquals(ni, l.Statement) ? l : new Stmt.LabeledStatement(l.Label, ni);
            }
            case Stmt.TryCatch t:
            {
                var nt = ProcessBody(t.TryBlock, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                var nc = t.CatchBlock != null ? ProcessBody(t.CatchBlock, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod) : null;
                var nfb = t.FinallyBlock != null ? ProcessBody(t.FinallyBlock, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod) : null;
                if (ReferenceEquals(nt, t.TryBlock)
                    && (t.CatchBlock == null || ReferenceEquals(nc, t.CatchBlock))
                    && (t.FinallyBlock == null || ReferenceEquals(nfb, t.FinallyBlock)))
                    return t;
                return t with { TryBlock = nt, CatchBlock = nc, FinallyBlock = nfb };
            }
            case Stmt.Switch sw:
            {
                List<Stmt.SwitchCase>? newCases = null;
                for (int i = 0; i < sw.Cases.Count; i++)
                {
                    var c = sw.Cases[i];
                    var nb = ProcessBody(c.Body, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                    if (!ReferenceEquals(nb, c.Body))
                    {
                        newCases ??= new List<Stmt.SwitchCase>(sw.Cases);
                        newCases[i] = new Stmt.SwitchCase(c.Value, nb);
                    }
                }
                var newDefault = sw.DefaultBody != null ? ProcessBody(sw.DefaultBody, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod) : null;
                bool defaultChanged = sw.DefaultBody != null && !ReferenceEquals(newDefault, sw.DefaultBody);
                if (newCases == null && !defaultChanged) return sw;
                return new Stmt.Switch(sw.Subject, newCases ?? sw.Cases, defaultChanged ? newDefault : sw.DefaultBody);
            }
            case Stmt.Class cls:
                return ProcessClass(cls);
            case Stmt.Export ex when ex.Declaration != null:
            {
                var nd = ProcessStmt(ex.Declaration, enclosingIsStateMachine, enclosingIsAsyncFunction, enclosingIsGeneratorClassMethod);
                return ReferenceEquals(nd, ex.Declaration) ? ex : ex with { Declaration = nd };
            }
            // Stmt.Namespace is intentionally NOT traversed (#583 §3 lift barrier).
            default:
                return stmt;
        }
    }

    private Stmt ProcessClass(Stmt.Class cls)
    {
        List<Stmt.Function>? newMethods = null;
        for (int i = 0; i < cls.Methods.Count; i++)
        {
            var m = cls.Methods[i];
            if (m.Body == null) continue;
            // #945: a class generator method (sync or async, static or instance) does NOT reliably wire
            // a function display class for read-only captures, so a forwarding binding hoisted into its
            // body would read a stale by-value snapshot. Flag it so the gate declines to hoist there.
            var nb = ProcessBody(m.Body, m.IsGenerator || m.IsAsync, m.IsAsync && !m.IsGenerator, enclosingIsGeneratorClassMethod: m.IsGenerator);
            if (!ReferenceEquals(nb, m.Body))
            {
                newMethods ??= new List<Stmt.Function>(cls.Methods);
                newMethods[i] = m with { Body = nb };
            }
        }
        return newMethods == null ? cls : cls with { Methods = newMethods };
    }

    #endregion

    /// <summary>
    /// Collects the names of all module top-level bindings. A lifted declaration whose name matches
    /// one is left nested, because the injected <c>var</c> alias would otherwise be hijacked by the
    /// same-named top-level binding under the current name-resolution rules. Deliberately
    /// over-inclusive (type-only names too) — a false positive only declines a lift, which is safe.
    /// </summary>
    private static HashSet<string> CollectTopLevelBindingNames(List<Stmt> module)
    {
        var names = new HashSet<string>();
        foreach (var stmt in module)
            AddBindingName(stmt, names);
        return names;
    }

    private static void AddBindingName(Stmt stmt, HashSet<string> names)
    {
        switch (stmt)
        {
            case Stmt.Function f: names.Add(f.Name.Lexeme); break;
            case Stmt.Class c: names.Add(c.Name.Lexeme); break;
            case Stmt.Var v: names.Add(v.Name.Lexeme); break;
            case Stmt.Const co: names.Add(co.Name.Lexeme); break;
            case Stmt.Enum e: names.Add(e.Name.Lexeme); break;
            case Stmt.Namespace ns: names.Add(ns.Name.Lexeme); break;
            case Stmt.Interface itf: names.Add(itf.Name.Lexeme); break;
            case Stmt.TypeAlias ta: names.Add(ta.Name.Lexeme); break;
            case Stmt.Import imp:
                if (imp.DefaultImport != null) names.Add(imp.DefaultImport.Lexeme);
                if (imp.NamespaceImport != null) names.Add(imp.NamespaceImport.Lexeme);
                if (imp.NamedImports != null)
                    foreach (var spec in imp.NamedImports)
                        names.Add(spec.LocalName?.Lexeme ?? spec.Imported.Lexeme);
                break;
            case Stmt.Export ex when ex.Declaration != null:
                AddBindingName(ex.Declaration, names);
                break;
        }
    }
}
