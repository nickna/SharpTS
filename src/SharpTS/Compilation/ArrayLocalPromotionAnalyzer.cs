using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Whole-program analysis that flags <c>number[]</c>/<c>boolean[]</c> local declarations which can be
/// promoted from the default <c>object</c>/<c>$Array</c> slot to a concrete <c>List&lt;double&gt;</c>/
/// <c>List&lt;bool&gt;</c> CLR slot with unboxed element access (#857 / #860). Results are written into the
/// <see cref="TypeMap"/> via <see cref="TypeMap.MarkPromotableArrayLocal"/>; the IL emitter (EmitVar and
/// the index/length/push fast paths) consults them.
///
/// <para>This is the deliberately-conservative first cut: a local is promoted only when it is provably
/// non-escaping, so a bare <c>List&lt;T&gt;</c> (which is NOT a <c>$Array</c> and cannot represent holes,
/// sparseness, or <c>$Array</c> identity) is never observed anywhere that needs those semantics. A
/// candidate <c>x</c> qualifies iff ALL hold:</para>
/// <list type="number">
///   <item>declared <c>const</c>/<c>let</c> with an empty array-literal initializer (<c>[]</c>);</item>
///   <item>every use resolves the array element type to <c>number</c> or <c>boolean</c>;</item>
///   <item>the ONLY uses are <c>x[i]</c> read, <c>x[i] = v</c> write, <c>x.length</c>, and
///         <c>x.push(...)</c> — any other appearance of the bare variable (argument pass, return, store,
///         spread, <c>for…of</c>, <c>===</c>, reassignment, <c>delete x[i]</c>, <c>pop</c>/any other
///         method or property) disqualifies it;</item>
///   <item>the name is declared exactly once within its function scope (conservative guard against
///         shadowing without full scope resolution; candidacy is keyed per scope, so a same-named
///         array in a different function/module never interferes);</item>
///   <item>the name is not captured by any closure (a captured local is routed to an <c>object</c>
///         display-class field, never a typed slot).</item>
/// </list>
///
/// <para>The catch-all is <see cref="Visitor.VisitVariable"/>: any bare variable occurrence that was not
/// consumed by a permitted-use override disqualifies the name. The permitted-use overrides deliberately do
/// NOT recurse into the receiver variable, so only the safe shapes survive.</para>
/// </summary>
public static class ArrayLocalPromotionAnalyzer
{
    public static void Analyze(List<Stmt> program, TypeMap? typeMap, ClosureAnalyzer? closures)
    {
        if (typeMap == null) return;

        var visitor = new Visitor(typeMap, closures);
        foreach (var stmt in program)
            visitor.Visit(stmt);

        foreach (var (key, nameToken) in visitor.Candidates)
        {
            if (visitor.Disqualified.Contains(key)) continue;
            if (visitor.DeclCount.GetValueOrDefault(key) != 1) continue;
            if (!visitor.ElementToken.TryGetValue(key, out var token)) continue; // no Double/Bool use seen
            if (closures?.IsVariableCaptured(key.Name) == true) continue;
            typeMap.MarkPromotableArrayLocal(nameToken, token);
        }
    }

    private sealed class Visitor(TypeMap typeMap, ClosureAnalyzer? closures) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private readonly ClosureAnalyzer? _closures = closures;

        // Candidacy/disqualification is keyed by (function scope, lexeme), NOT whole-program lexeme: a
        // common array name like `arr`/`data` in one function must not be poisoned by an unrelated,
        // escaping same-name array in another (e.g. across bundled modules). Each function/arrow body is
        // its own scope; cross-scope references are captures, handled by the IsVariableCaptured guard. The
        // module top level is scope 0. Mirrors StringAccumulatorPromotionAnalyzer.
        private int _scope;
        private int _nextScope;

        /// <summary>(scope, name) → candidate declaration's name token (empty-array-literal local).</summary>
        public Dictionary<(int Scope, string Name), Token> Candidates { get; } = new();

        /// <summary>How many times each (scope, name) is declared.</summary>
        public Dictionary<(int Scope, string Name), int> DeclCount { get; } = new();

        /// <summary>(scope, name) → element primitive token (TYPE_NUMBER / TYPE_BOOLEAN), from its first typed use.</summary>
        public Dictionary<(int Scope, string Name), TokenType> ElementToken { get; } = new();

        /// <summary>(scope, name) pairs with at least one disqualifying occurrence.</summary>
        public HashSet<(int Scope, string Name)> Disqualified { get; } = new();

        protected override void VisitFunction(Stmt.Function stmt) => InScope(() => base.VisitFunction(stmt));
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) => InScope(() => base.VisitArrowFunction(expr));

        private void InScope(Action body)
        {
            var saved = _scope;
            _scope = ++_nextScope;
            body();
            _scope = saved;
        }

        protected override void VisitVar(Stmt.Var stmt) =>
            HandleDeclaration(stmt.Name, stmt.Initializer);

        protected override void VisitConst(Stmt.Const stmt) =>
            HandleDeclaration(stmt.Name, stmt.Initializer);

        private void HandleDeclaration(Token name, Expr? initializer)
        {
            var key = (_scope, name.Lexeme);
            DeclCount[key] = DeclCount.GetValueOrDefault(key) + 1;

            // A promotable-array local is one initialized with an empty array literal `[]` OR a
            // typed-double `src.map(cb)` (#861 typed-HOF pipeline). The map-result candidate becomes a
            // List<double> slot only if its source is itself promoted (checked at emit time); marking it
            // here is a hint, and a non-escaping result is the precondition for that to be sound.
            if (!Candidates.ContainsKey(key) && IsPromotableArrayInitializer(initializer))
            {
                Candidates[key] = name;
                // A typed number→number `map` result is statically number[]; record its element kind
                // directly rather than relying on the type checker resolving the .map() return type.
                // NotePermittedReceiver's ContainsKey guard then leaves this authoritative for the local's
                // own uses. (Empty-literal candidates get their element kind from their uses instead.)
                if (initializer is Expr.Call)
                    ElementToken[key] = TokenType.TYPE_NUMBER;
            }

            // Visit the initializer for completeness: `[]` has no sub-uses, but a `src.map(cb)` init
            // marks `src` as a permitted map-receiver (via VisitCall), and any non-candidate initializer
            // may reference other arrays.
            if (initializer != null)
                Visit(initializer);
        }

        private bool IsPromotableArrayInitializer(Expr? init) =>
            init is Expr.ArrayLiteral { Elements.Count: 0 }
            || (init is Expr.Call { Callee: Expr.Get { Object: Expr.Variable, Name.Lexeme: "map", Optional: false } } mc
                && IsTypedNonCapturingNumericMapper(mc.Arguments))
            || (init is Expr.Call { Callee: Expr.Get { Object: Expr.Variable, Name.Lexeme: "filter", Optional: false } } fc
                && IsTypedNonCapturingNumericPredicate(fc.Arguments));

        protected override void VisitGetIndex(Expr.GetIndex expr)
        {
            // `x[i]` read — permitted when receiver is a bare variable. Record the
            // element kind and visit only the index (NOT the receiver variable).
            if (expr.Object is Expr.Variable v && !expr.Optional)
                NotePermittedReceiver(v);
            else
                Visit(expr.Object);
            Visit(expr.Index);
        }

        protected override void VisitSetIndex(Expr.SetIndex expr)
        {
            // `x[i] = v` write — permitted receiver; visit index and value only.
            if (expr.Object is Expr.Variable v)
            {
                NotePermittedReceiver(v);
                // A written value whose static type admits the `undefined` sentinel
                // (any/unknown/…) would be coerced to NaN/false by the typed setter,
                // diverging from the general path that preserves it. Disqualify — the
                // array analogue of the #367/#372 numeric-slot taint guard.
                if (ValueAdmitsUndefinedSentinel(expr.Value))
                    Disqualified.Add((_scope, v.Name.Lexeme));
            }
            else
                Visit(expr.Object);
            Visit(expr.Index);
            Visit(expr.Value);
        }

        protected override void VisitGet(Expr.Get expr)
        {
            // `x.length` — permitted; skip the receiver variable. Any other
            // property access on a bare variable falls through to VisitVariable
            // (disqualifying), which is what we want.
            if (expr.Name.Lexeme == "length" && expr.Object is Expr.Variable v && !expr.Optional)
            {
                NotePermittedReceiver(v);
                return;
            }
            Visit(expr.Object);
        }

        protected override void VisitCall(Expr.Call expr)
        {
            // `x.push(args)` — permitted; visit the args but skip the receiver
            // variable. Every other call shape recurses normally, so a receiver
            // passed as an argument, or any non-push method, is caught. (pop is
            // intentionally excluded from this first cut: its empty→undefined
            // result can't sit in an unboxed typed slot.)
            if (expr.Callee is Expr.Get { Object: Expr.Variable v, Optional: false } get
                && get.Name.Lexeme == "push")
            {
                NotePermittedReceiver(v);
                // Same undefined-sentinel guard as index writes: a pushed value that
                // can be undefined would be coerced by the typed helper.
                foreach (var arg in expr.Arguments)
                {
                    if (ValueAdmitsUndefinedSentinel(arg))
                        Disqualified.Add((_scope, v.Name.Lexeme));
                    Visit(arg);
                }
                return;
            }

            // `x.reduce(reducer, init)` over a number[] with a typed, non-capturing 2-arg numeric
            // reducer — permitted receiver (#861 typed-HOF pipeline: the ArrayReduceDouble fast path
            // drives a Func<double,double,double> over the bare List<double>, no per-element boxing).
            // The emitter's typed-reduce hook gates on the SAME criteria (List<double> slot + a
            // non-capturing double(double,double) arrow), so promotion here always pairs with a typed
            // emit — never a broken List<double>-receiver fallback. Any other reduce shape falls through
            // to base (→ VisitVariable on the receiver → disqualify), keeping it on the $Array path.
            if (expr.Callee is Expr.Get { Object: Expr.Variable rv, Optional: false } rget
                && rget.Name.Lexeme == "reduce"
                && IsNumberArrayReceiver(rv)
                && IsTypedNonCapturingNumericReducer(expr.Arguments))
            {
                NotePermittedReceiver(rv);
                foreach (var arg in expr.Arguments) Visit(arg);
                return;
            }

            // `x.map(mapper)` over a number[] with a typed, non-capturing number→number mapper —
            // permitted receiver (#861 typed-HOF pipeline: ArrayMapDouble drives a Func<double,double>
            // over the bare List<double> into a fresh List<double>, no boxing). The result is only
            // promoted to a typed slot when the destination local is itself non-escaping and its source
            // is promoted (decided at emit time); here we just keep the source eligible.
            if (expr.Callee is Expr.Get { Object: Expr.Variable mv, Optional: false } mget
                && mget.Name.Lexeme == "map"
                && IsNumberArrayReceiver(mv)
                && IsTypedNonCapturingNumericMapper(expr.Arguments))
            {
                NotePermittedReceiver(mv);
                foreach (var arg in expr.Arguments) Visit(arg);
                return;
            }

            // `x.filter(predicate)` over a number[] with a typed, non-capturing number→bool predicate —
            // permitted receiver (ArrayFilterDouble drives a Func<double,bool> over the bare List<double>
            // into a fresh List<double>, no boxing). Same result-promotion rules as map.
            if (expr.Callee is Expr.Get { Object: Expr.Variable fv, Optional: false } fget
                && fget.Name.Lexeme == "filter"
                && IsNumberArrayReceiver(fv)
                && IsTypedNonCapturingNumericPredicate(expr.Arguments))
            {
                NotePermittedReceiver(fv);
                foreach (var arg in expr.Arguments) Visit(arg);
                return;
            }
            base.VisitCall(expr);
        }

        /// <summary>
        /// True if <paramref name="arguments"/> is a single inline, non-capturing arrow with one
        /// annotated <c>number</c> param and a <c>boolean</c> body — the shape the typed
        /// <c>ArrayFilterDouble</c> fast path binds to a <c>Func&lt;double,bool&gt;</c>.
        /// </summary>
        private bool IsTypedNonCapturingNumericPredicate(List<Expr> arguments)
        {
            if (arguments.Count != 1) return false;
            if (arguments[0] is not Expr.ArrowFunction af) return false;
            if (af.IsAsync || af.IsGenerator || af.HasOwnThis) return false;
            if (af.Parameters.Count != 1) return false;
            var p = af.Parameters[0];
            if (p.IsRest || p.IsOptional || p.DefaultValue != null) return false;
            if (p.Type != "number") return false;
            if (af.ReturnType != null && af.ReturnType != "boolean") return false;
            if (_closures?.GetCaptures(af).Count > 0) return false;
            return true;
        }

        /// <summary>
        /// True if <paramref name="arguments"/> is a single inline, non-capturing arrow with one
        /// annotated <c>number</c> param and a <c>number</c> body — the shape the typed
        /// <c>ArrayMapDouble</c> fast path binds to a <c>Func&lt;double,double&gt;</c>. Mirrors the
        /// emitter's <c>TryResolveTypedDoubleMapInit</c> gate.
        /// </summary>
        /// <summary>
        /// True if <paramref name="v"/> is a number array receiver eligible for typed map/reduce: either
        /// the type checker resolves it to <c>number[]</c>, or it is a recorded number map-result candidate
        /// in this scope (the checker often can't infer a chained <c>.map()</c> result type, but a typed
        /// number→number map yields number[] by construction — see HandleDeclaration).
        /// </summary>
        private bool IsNumberArrayReceiver(Expr.Variable v)
        {
            if (ArrayElements.Resolve(_typeMap.Get(v)) is { Kind: ArrayElementsKind.Double }) return true;
            return ElementToken.TryGetValue((_scope, v.Name.Lexeme), out var t) && t == TokenType.TYPE_NUMBER;
        }

        private bool IsTypedNonCapturingNumericMapper(List<Expr> arguments)
        {
            if (arguments.Count != 1) return false;
            if (arguments[0] is not Expr.ArrowFunction af) return false;
            if (af.IsAsync || af.IsGenerator || af.HasOwnThis) return false;
            if (af.Parameters.Count != 1) return false;
            var p = af.Parameters[0];
            if (p.IsRest || p.IsOptional || p.DefaultValue != null) return false;
            if (p.Type != "number") return false;
            if (af.ReturnType != null && af.ReturnType != "number") return false;
            if (_closures?.GetCaptures(af).Count > 0) return false;
            return true;
        }

        /// <summary>
        /// True if <paramref name="arguments"/> is <c>(reducer, init)</c> where reducer is an inline,
        /// non-capturing arrow with exactly two annotated <c>number</c> params and a <c>number</c> body —
        /// the shape the typed <c>ArrayReduceDouble</c> fast path can bind to a
        /// <c>Func&lt;double,double,double&gt;</c>. Mirrors the emitter's typed-reduce gate.
        /// </summary>
        private bool IsTypedNonCapturingNumericReducer(List<Expr> arguments)
        {
            if (arguments.Count != 2) return false;
            if (arguments[0] is not Expr.ArrowFunction af) return false;
            if (af.IsAsync || af.IsGenerator || af.HasOwnThis) return false;
            if (af.Parameters.Count != 2) return false;
            foreach (var p in af.Parameters)
            {
                if (p.IsRest || p.IsOptional || p.DefaultValue != null) return false;
                if (p.Type != "number") return false;
            }
            if (af.ReturnType != null && af.ReturnType != "number") return false;
            // Non-capturing: consistent with the emitter's DisplayClasses check (both derive from
            // ClosureAnalyzer), so a permitted reducer is always bindable as a direct static delegate.
            if (_closures?.GetCaptures(af).Count > 0) return false;
            return true;
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            // `x = ...` rebinds the slot — disqualify. (Only the empty-literal
            // initializer is supported; any later store could be a $Array or a
            // value the typed slot can't hold.)
            Disqualified.Add((_scope, expr.Name.Lexeme));
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Disqualified.Add((_scope, expr.Name.Lexeme));
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitDelete(Expr.Delete expr)
        {
            // `delete x[i]` creates a hole a List<T> cannot represent — disqualify
            // the base variable of the deleted operand.
            var target = expr.Operand;
            while (true)
            {
                if (target is Expr.GetIndex gi) { target = gi.Object; continue; }
                if (target is Expr.Get g) { target = g.Object; continue; }
                break;
            }
            if (target is Expr.Variable v)
                Disqualified.Add((_scope, v.Name.Lexeme));
            base.VisitDelete(expr);
        }

        protected override void VisitVariable(Expr.Variable expr)
        {
            // Catch-all: any bare variable occurrence not consumed by a permitted-use
            // override above is an escape (returned, passed, spread, compared, etc.).
            Disqualified.Add((_scope, expr.Name.Lexeme));
        }

        private void NotePermittedReceiver(Expr.Variable v)
        {
            // Resolve the element kind from the receiver's static array type (as
            // ArrayHoistAnalyzer does). Only Double/Bool arrays are promotable; an
            // Object-kind array (string[]/union[]) disqualifies the name.
            if (ElementToken.ContainsKey((_scope, v.Name.Lexeme))) return;
            var desc = ArrayElements.Resolve(_typeMap.Get(v));
            if (desc == null) return; // not statically an array here — leave undecided
            if (desc.Kind == ArrayElementsKind.Object || desc.ElementTokenType is not { } tok)
            {
                Disqualified.Add((_scope, v.Name.Lexeme));
                return;
            }
            ElementToken[(_scope, v.Name.Lexeme)] = tok;
        }

        /// <summary>
        /// True if <paramref name="value"/>'s static type can carry the runtime <c>undefined</c>
        /// sentinel (<c>any</c>/<c>unknown</c>/<c>undefined</c>, or a union containing one). Mirrors
        /// the type checker's <c>TypeAdmitsUndefinedSentinel</c> — a value the typed element setter
        /// would silently coerce to NaN/false instead of preserving as undefined.
        /// </summary>
        private bool ValueAdmitsUndefinedSentinel(Expr value) => Admits(_typeMap.Get(value));

        private static bool Admits(TypeInfo? type) => type switch
        {
            TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Undefined => true,
            TypeInfo.Union u => u.Types.Any(Admits),
            _ => false
        };
    }
}
