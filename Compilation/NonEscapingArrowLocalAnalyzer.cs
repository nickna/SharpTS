using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Whole-program analysis that flags <c>const NAME = (…) =&gt; …</c> local bindings whose arrow value
/// provably never escapes — it is only ever invoked by name in direct-call position (<c>NAME(args)</c>).
/// Such an arrow does not need the per-call <c>$TSFunction</c> wrapper + reflective
/// <c>InvokeMethodValue</c> dispatch (boxed <c>object[]</c> args, <c>GetMethodFromHandle</c>, unbox of
/// the result); for a <b>capturing</b> arrow the emitter stores the bare display-class instance in a
/// typed local and the call site emits a direct <c>callvirt Invoke</c> with unboxed typed args (#858).
///
/// <para>Results are written into <paramref name="result"/> (binding name → its arrow). The IL emitter
/// (<c>EmitVarDeclaration</c> and the function-value call fast path) consults them; the optimization only
/// actually fires when the arrow turns out to be capturing (has a display class) and the in-scope local
/// slot's CLR type matches that display class — so a same-named parameter/local in another scope can never
/// accidentally hit the fast path (mirrors the slot-type keying of <see cref="ArrayLocalPromotionAnalyzer"/>).</para>
///
/// <para>Because there is no <c>$TSFunction</c> wrapper to fall back to once the local holds the bare
/// display instance, <b>every</b> use of a qualifying name must be a direct call this analysis can prove
/// the emitter handles. A candidate <c>f</c> qualifies iff ALL hold:</para>
/// <list type="number">
///   <item>declared <c>const</c>/<c>let</c> with an arrow-literal initializer that is not async, not a
///         generator, not a named function expression (self-reference needs the wrapper), not an
///         object-method arrow (<c>HasOwnThis</c>), and has no rest / optional / default-valued
///         parameter (so plain positional arity is exact and meaningful);</item>
///   <item>the ONLY uses are direct calls <c>f(args)</c> — non-optional, no type arguments, no spread
///         argument, and <c>args.Count</c> equal to the arrow's parameter count. Any other appearance of
///         the bare variable (argument pass, return, store, property/index access, spread, comparison,
///         reassignment, an indirect/optional/spread/wrong-arity call) disqualifies it;</item>
///   <item>the name is declared exactly once in the whole program (conservative guard against scope
///         ambiguity / shadowing without full scope resolution);</item>
///   <item>the name is not captured by any closure (a captured binding is routed to an <c>object</c>
///         display-class field, never a typed local slot the call site can key on).</item>
///   <item>the name is not exported (an exported binding escapes through its module's export field and
///         is invoked cross-module via the generic reflective dispatch, not the local-slot fast path — so
///         the bare display-class instance would surface as a non-callable object, #1229).</item>
/// </list>
///
/// <para>The catch-all is <see cref="Visitor.VisitVariable"/>: any bare variable occurrence not consumed
/// by the permitted direct-call override disqualifies the name. A recursive self-call inside the arrow
/// body reads the name from a nested scope, so the closure-capture check (4) filters it out.</para>
/// </summary>
public static class NonEscapingArrowLocalAnalyzer
{
    public static void Analyze(List<Stmt> program, IDictionary<string, Expr.ArrowFunction> result, ClosureAnalyzer? closures)
    {
        var visitor = new Visitor();
        foreach (var stmt in program)
            visitor.Visit(stmt);

        foreach (var (name, arrow) in visitor.Candidates)
        {
            if (visitor.Disqualified.Contains(name)) continue;
            if (visitor.DeclCount.GetValueOrDefault(name) != 1) continue;
            // Every observed direct call must pass exactly the declared number of positional args.
            if (visitor.CallArities.TryGetValue(name, out var arities) &&
                arities.Any(a => a != arrow.Parameters.Count))
                continue;
            if (closures?.IsVariableCaptured(name) == true) continue;
            result[name] = arrow;
        }
    }

    private sealed class Visitor : AstVisitorBase
    {
        /// <summary>name → eligible arrow-literal initializer.</summary>
        public Dictionary<string, Expr.ArrowFunction> Candidates { get; } = new();

        /// <summary>How many times each name is declared anywhere (var/const binding).</summary>
        public Dictionary<string, int> DeclCount { get; } = new();

        /// <summary>name → positional arg counts of every permitted direct call seen.</summary>
        public Dictionary<string, List<int>> CallArities { get; } = new();

        /// <summary>Names with at least one disqualifying occurrence.</summary>
        public HashSet<string> Disqualified { get; } = new();

        protected override void VisitVar(Stmt.Var stmt) =>
            HandleDeclaration(stmt.Name, stmt.Initializer);

        protected override void VisitConst(Stmt.Const stmt) =>
            HandleDeclaration(stmt.Name, stmt.Initializer);

        private void HandleDeclaration(Token name, Expr? initializer)
        {
            var lexeme = name.Lexeme;
            DeclCount[lexeme] = DeclCount.GetValueOrDefault(lexeme) + 1;

            if (initializer is Expr.ArrowFunction af && IsEligibleArrow(af) && !Candidates.ContainsKey(lexeme))
                Candidates[lexeme] = af;

            // Visit the initializer so its sub-uses are accounted for (a recursive call inside the
            // arrow body reads the name from a nested scope and is filtered by the capture check).
            if (initializer != null)
                Visit(initializer);
        }

        protected override void VisitCall(Expr.Call expr)
        {
            // `NAME(args)` — the one permitted shape: a non-optional, non-generic, spread-free direct
            // call on a bare variable. Record the arity and visit the arguments, but NOT the callee
            // variable (so it is not disqualified by the VisitVariable catch-all). Every other call
            // shape recurses normally, so an indirect call, a spread, type arguments, or the name used
            // as an argument is caught.
            if (expr.Callee is Expr.Variable v && !expr.Optional &&
                (expr.TypeArgs == null || expr.TypeArgs.Count == 0) &&
                !expr.Arguments.Any(a => a is Expr.Spread))
            {
                if (!CallArities.TryGetValue(v.Name.Lexeme, out var arities))
                    CallArities[v.Name.Lexeme] = arities = [];
                arities.Add(expr.Arguments.Count);
                foreach (var arg in expr.Arguments)
                    Visit(arg);
                return;
            }
            base.VisitCall(expr);
        }

        protected override void VisitVariable(Expr.Variable expr) =>
            Disqualified.Add(expr.Name.Lexeme);

        protected override void VisitExport(Stmt.Export stmt)
        {
            // An exported binding escapes this module: its value is copied into the module's
            // export field (read by `$GetNamespace` and by importing modules, which call it
            // through the generic reflective `InvokeMethodValue` dispatch — never the local-slot
            // direct-call fast path this optimization relies on). Storing the bare display-class
            // instance without a `$TSFunction` wrapper would then surface a plain object where a
            // callable is expected ("object is not a function", #1229). Disqualify the exported
            // name(s), then fall through to the base walk so uses INSIDE the declaration's
            // initializer still count toward other candidates.
            DisqualifyExportedNames(stmt.Declaration);
            if (stmt.NamedExports != null)
                foreach (var spec in stmt.NamedExports)
                    Disqualified.Add(spec.LocalName.Lexeme);

            base.VisitExport(stmt);
        }

        private void DisqualifyExportedNames(Stmt? decl)
        {
            switch (decl)
            {
                case Stmt.Const c: Disqualified.Add(c.Name.Lexeme); break;
                case Stmt.Var v: Disqualified.Add(v.Name.Lexeme); break;
                case Stmt.Sequence seq:
                    foreach (var inner in seq.Statements)
                        DisqualifyExportedNames(inner);
                    break;
            }
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            // `f = …` rebinds the name — the original arrow is no longer the only value, so it must NOT
            // be optimized. The assignment target is a Token (not an Expr.Variable), so the VisitVariable
            // catch-all never sees it; disqualify explicitly. (A `let f = arrow; f = arrow2;` case.)
            Disqualified.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Disqualified.Add(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        private static bool IsEligibleArrow(Expr.ArrowFunction af) =>
            !af.IsAsync && !af.IsGenerator && af.Name == null && !af.HasOwnThis &&
            af.Parameters.All(p => !p.IsRest && !p.IsOptional && p.DefaultValue == null);
    }
}
