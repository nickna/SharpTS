using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Whole-program analysis that flags <c>string</c> local declarations which can be promoted from the
/// default <c>object</c> slot to a concrete <c>StringBuilder</c> slot (#857). Repeated
/// <c>s = s + str</c> / <c>s += str</c> on an <c>object</c>/<c>string</c> slot lowers to
/// <c>String.Concat</c>, which copies the whole accumulator every iteration — O(n²). A StringBuilder
/// slot turns that into amortized-O(1) <c>Append</c> (O(n) total). <c>StringBuilder.Length</c> and the
/// <c>this[int]</c> indexer are UTF-16 code units, identical to JS <c>.length</c> and
/// <c>charCodeAt(i)</c>, so those reads need no materialization.
///
/// <para>Deliberately-conservative first cut (mirrors <see cref="ArrayLocalPromotionAnalyzer"/>): a local
/// is promoted only when provably non-escaping AND every use is one of a tiny permitted set, so the bare
/// <c>StringBuilder</c> (which is NOT a <c>string</c>) is never observed anywhere that expects a string.
/// A candidate <c>s</c> qualifies iff ALL hold:</para>
/// <list type="number">
///   <item>declared <c>const</c>/<c>let</c> with a string-literal initializer (<c>""</c> or any <c>"…"</c>);</item>
///   <item>the name is declared exactly once in the whole program (conservative shadowing guard);</item>
///   <item>the name is not captured by any closure (a captured local is routed to an <c>object</c>
///         display-class field, never a typed slot);</item>
///   <item>every use is one of: an <b>append in statement position</b> (<c>s = s + E</c> or <c>s += E</c>
///         as an expression statement, where <c>E</c> is statically <c>string</c> and does not reference
///         <c>s</c>), <c>s.length</c>, or <c>s.charCodeAt(i)</c>. Any other appearance — return, argument
///         pass, <c>s[i]</c>, other method/property, comparison, template literal, reassignment to a
///         non-append value, a non-string append, or an append used as a value — disqualifies it.</item>
/// </list>
///
/// <para>Append must be in statement position because <c>s = s + E</c> evaluates to the new string; with a
/// StringBuilder slot that result cannot be produced without an O(n) <c>ToString()</c>. As an expression
/// statement the result is discarded (<c>Stmt.Expression</c> pops it), so the emitter leaves the
/// <c>Append</c>-returned builder on the stack as the one popped value. Materialize-on-escape
/// (<c>return s</c>, pass, index, other methods) is a deliberate Phase-2 follow-up.</para>
/// </summary>
public static class StringAccumulatorPromotionAnalyzer
{
    public static void Analyze(List<Stmt> program, TypeMap? typeMap, ClosureAnalyzer? closures)
    {
        if (typeMap == null) return;

        var visitor = new Visitor(typeMap);
        foreach (var stmt in program)
            visitor.Visit(stmt);

        foreach (var (key, nameToken) in visitor.Candidates)
        {
            if (visitor.Disqualified.Contains(key)) continue;
            if (visitor.DirectEvalScopes.Contains(key.Scope)) continue;
            if (visitor.DeclCount.GetValueOrDefault(key) != 1) continue;
            // IsVariableCaptured is lexeme-global (conservative): a captured local is routed to an
            // object display-class field, never a StringBuilder slot, so capture must disqualify.
            if (closures?.IsVariableCaptured(key.Name) == true) continue;
            typeMap.MarkPromotableStringAccumulator(nameToken);
        }
    }

    private sealed class Visitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;

        // Candidacy/disqualification is keyed by (function scope, lexeme), NOT whole-program lexeme:
        // a common accumulator name like `s` in one function must not be poisoned by an unrelated,
        // escaping `s` in another (e.g. perf_hooks's `const s = findMark(...)` in a bundle). Each
        // function/arrow body is its own scope; cross-scope references are captures, handled by the
        // IsVariableCaptured guard. The module top level is scope 0.
        private int _scope;
        private int _nextScope;

        /// <summary>(scope, name) → candidate declaration's name token (string-literal-initialized local).</summary>
        public Dictionary<(int Scope, string Name), Token> Candidates { get; } = new();

        /// <summary>How many times each (scope, name) is declared.</summary>
        public Dictionary<(int Scope, string Name), int> DeclCount { get; } = new();

        /// <summary>(scope, name) pairs with at least one disqualifying occurrence.</summary>
        public HashSet<(int Scope, string Name)> Disqualified { get; } = new();

        /// <summary>
        /// Scopes containing a direct eval call. Its source can observe any
        /// lexical binding even when that binding has no ordinary AST use, so
        /// representation-changing local promotion is unsound in that scope.
        /// </summary>
        public HashSet<int> DirectEvalScopes { get; } = new();

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

            if (initializer is Expr.Literal { Value: string } && !Candidates.ContainsKey(key))
                Candidates[key] = name;

            // A string-literal initializer has no sub-uses, but a non-candidate initializer may
            // reference other accumulators.
            if (initializer is not Expr.Literal { Value: string } && initializer != null)
                Visit(initializer);
        }

        protected override void VisitExpression(Stmt.Expression stmt)
        {
            // Permitted append in statement position (result discarded): `s = s + E` / `s += E`
            // with E statically string. Consume by visiting ONLY E — not the target, not the inner
            // `s` read. Visiting E still disqualifies if E references s (the VisitVariable catch-all)
            // or escapes any other accumulator.
            switch (stmt.Expr)
            {
                case Expr.Assign { Value: Expr.Binary { Operator.Type: TokenType.PLUS, Left: Expr.Variable lv } bin } asg
                    when lv.Name.Lexeme == asg.Name.Lexeme && IsStaticString(bin.Right):
                    Visit(bin.Right);
                    return;
                case Expr.CompoundAssign { Operator.Type: TokenType.PLUS_EQUAL } ca when IsStaticString(ca.Value):
                    Visit(ca.Value);
                    return;
            }
            base.VisitExpression(stmt);
        }

        protected override void VisitGet(Expr.Get expr)
        {
            // `s.length` — permitted; skip the receiver variable.
            if (expr.Name.Lexeme == "length" && expr.Object is Expr.Variable && !expr.Optional)
                return;
            Visit(expr.Object);
        }

        protected override void VisitCall(Expr.Call expr)
        {
            if (expr.Callee is Expr.Variable { Name.Lexeme: "eval" })
                DirectEvalScopes.Add(_scope);

            // `s.charCodeAt(i)` — permitted; visit the index args but skip the receiver variable.
            if (expr.Callee is Expr.Get { Object: Expr.Variable, Optional: false } get
                && get.Name.Lexeme == "charCodeAt")
            {
                foreach (var arg in expr.Arguments)
                    Visit(arg);
                return;
            }
            base.VisitCall(expr);
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            // Reached only when NOT consumed as a statement-position append above — i.e. a reassign,
            // a non-string/prepend append, or an append used as a value. Disqualify.
            Disqualified.Add((_scope, expr.Name.Lexeme));
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Disqualified.Add((_scope, expr.Name.Lexeme));
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitVariable(Expr.Variable expr)
        {
            // Catch-all: any bare variable occurrence not consumed by a permitted-use override above
            // is an escape (returned, passed, indexed, compared, concatenated as a value, etc.).
            Disqualified.Add((_scope, expr.Name.Lexeme));
        }

        private bool IsStaticString(Expr e) => IsStringTypeInfo(_typeMap.Get(e));

        private static bool IsStringTypeInfo(TypeInfo? type) => type switch
        {
            TypeInfo.String => true,
            TypeInfo.StringLiteral => true,
            TypeInfo.Union u => u.FlattenedTypes.All(IsStringTypeInfo),
            _ => false
        };
    }
}
