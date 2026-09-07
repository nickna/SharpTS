using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Proves a bounded numeric-storage slice: a const local list of fresh records,
/// const aliases of its elements, and scalar/indexed field reads only. No array
/// alias can cross a call, module, closure, iterator, interop or mutation boundary.
/// </summary>
internal static class LocalRecordArrayAnalyzer
{
    internal static void Analyze(IReadOnlyList<Stmt> statements, TypeMap? types, RuntimeFeatureSet features)
    {
        if (types is null || features.UsesDynamicPropertyDescriptors || features.UsesArrayPrototypeMutation)
            return;
        var functions = new Functions();
        foreach (var statement in statements) functions.Visit(statement);
        foreach (var function in functions.Items)
        {
            if (function.Body is null) continue;
            var declarations = new Declarations(function);
            foreach (var statement in function.Body) declarations.Visit(statement);
            if (declarations.Unsafe) continue;
            foreach (var list in declarations.Constants.Where(c => c.Initializer is Expr.ArrayLiteral { Elements.Count: 0 }))
            {
                string name = list.Name.Lexeme;
                if (declarations.Counts.GetValueOrDefault(name) != 1) continue;
                var pushes = declarations.Calls.Where(c => IsPush(c, name)).ToArray();
                if (pushes.Length == 0) continue;
                var literals = pushes.Select(c => (Expr.ObjectLiteral)c.Arguments[0]).ToArray();
                if (literals.Any(l => !features.CompactObjectRecordStablePushLiterals.Contains(l))) continue;
                var numeric = Fields(literals[0], declarations, numeric: true);
                var scalar = Fields(literals[0], declarations, numeric: false);
                foreach (var literal in literals.Skip(1))
                {
                    numeric.IntersectWith(Fields(literal, declarations, numeric: true));
                    scalar.IntersectWith(Fields(literal, declarations, numeric: false));
                }
                if (numeric.Count == 0) continue;
                var aliases = declarations.Constants.Where(c => c.Initializer is Expr.GetIndex
                    { Optional: false, Object: Expr.Variable v } && v.Name.Lexeme == name)
                    .Select(c => c.Name.Lexeme).ToHashSet(StringComparer.Ordinal);
                if (aliases.Any(a => declarations.Counts.GetValueOrDefault(a) != 1)) continue;
                var usage = new Usage(name, aliases, numeric, scalar);
                foreach (var statement in function.Body) usage.Visit(statement);
                if (usage.Unsafe) continue;
                foreach (var literal in literals)
                    foreach (var property in literal.Properties)
                        if (property.Key is Expr.IdentifierKey key && numeric.Contains(key.Name.Lexeme))
                            features.LocalRecordNumericArrays.Add((Expr.ArrayLiteral)property.Value);
            }
        }

        bool NumericElement(Expr expression, Declarations declarations)
        {
            if (!IsNumber(types.Get(expression))) return false;
            // A number annotation on an interface/property/call is insufficient:
            // an opaque alias may return a string at runtime. Restrict literals
            // to native numeric locals and arithmetic built from them.
            return expression switch
            {
                Expr.Literal { Value: double or int } => true,
                Expr.Variable v => declarations.Counts.ContainsKey(v.Name.Lexeme) ||
                    v.Name.Lexeme is "NaN" or "Infinity",
                Expr.Grouping g => NumericElement(g.Expression, declarations),
                Expr.Unary u => NumericElement(u.Right, declarations),
                Expr.Binary b => NumericElement(b.Left, declarations) && NumericElement(b.Right, declarations),
                _ => false
            };
        }

        HashSet<string> Fields(Expr.ObjectLiteral literal, Declarations declarations, bool numeric)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in literal.Properties)
            {
                if (p.Key is not Expr.IdentifierKey key || p.IsSpread || p.Kind != Expr.ObjectPropertyKind.Value) continue;
                if (numeric && p.Value is Expr.ArrayLiteral { Elements.Count: > 0 } a &&
                    !Enumerable.Range(0, a.Elements.Count).Any(a.IsHole) && a.Elements.All(e => NumericElement(e, declarations)))
                    fields.Add(key.Name.Lexeme);
                else if (!numeric && (IsNumber(types.Get(p.Value)) || types.Get(p.Value) is
                    TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.BooleanLiteral or TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN }))
                    fields.Add(key.Name.Lexeme);
            }
            return fields;
        }
    }

    private static bool IsNumber(TypeInfo? type) => type is
        TypeInfo.NumberLiteral or TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER };

    private static bool IsPush(Expr.Call call, string name) => call is
        { Optional: false, Callee: Expr.Get { Optional: false, Object: Expr.Variable v, Name.Lexeme: "push" },
            Arguments: [Expr.ObjectLiteral] } && v.Name.Lexeme == name;

    private sealed class Functions : AstVisitorBase
    {
        internal List<Stmt.Function> Items { get; } = [];
        protected override void VisitFunction(Stmt.Function statement) { Items.Add(statement); base.VisitFunction(statement); }
    }

    private sealed class Declarations(Stmt.Function function) : AstVisitorBase
    {
        internal Dictionary<string, int> Counts { get; } = function.Parameters.GroupBy(p => p.Name.Lexeme)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        internal List<Stmt.Const> Constants { get; } = [];
        internal List<Expr.Call> Calls { get; } = [];
        internal bool Unsafe { get; private set; }
        protected override void VisitConst(Stmt.Const s) { Counts[s.Name.Lexeme] = Counts.GetValueOrDefault(s.Name.Lexeme) + 1; Constants.Add(s); base.VisitConst(s); }
        protected override void VisitVar(Stmt.Var s) { Counts[s.Name.Lexeme] = Counts.GetValueOrDefault(s.Name.Lexeme) + 1; base.VisitVar(s); }
        protected override void VisitCall(Expr.Call e) { Calls.Add(e); base.VisitCall(e); }
        protected override void VisitVariable(Expr.Variable e) { if (e.Name.Lexeme == "eval") Unsafe = true; }
        protected override void VisitFunction(Stmt.Function s) => Unsafe = true;
        protected override void VisitArrowFunction(Expr.ArrowFunction e) => Unsafe = true;
        protected override void VisitClass(Stmt.Class s) => Unsafe = true;
        protected override void VisitClassExpr(Expr.ClassExpr e) => Unsafe = true;
        // These forms bind names outside Var/Const; reject rather than infer an
        // alias relationship across a shadowed binding.
        protected override void VisitForOf(Stmt.ForOf s) => Unsafe = true;
        protected override void VisitForIn(Stmt.ForIn s) => Unsafe = true;
        protected override void VisitTryCatch(Stmt.TryCatch s) => Unsafe = true;
        protected override void VisitDestructuringAssign(Expr.DestructuringAssign e) => Unsafe = true;
    }

    private sealed class Usage(string list, HashSet<string> aliases, HashSet<string> numeric, HashSet<string> scalar) : AstVisitorBase
    {
        internal bool Unsafe { get; private set; }
        private bool _mutationTarget;
        protected override void VisitConst(Stmt.Const s)
        {
            if (aliases.Contains(s.Name.Lexeme) && s.Initializer is Expr.GetIndex index) Visit(index.Index);
            else base.VisitConst(s);
        }
        protected override void VisitCall(Expr.Call e)
        {
            if (IsPush(e, list)) Visit(e.Arguments[0]);
            else base.VisitCall(e);
        }
        protected override void VisitGet(Expr.Get e)
        {
            if (!_mutationTarget && !e.Optional && e.Object is Expr.Variable v &&
                ((v.Name.Lexeme == list && e.Name.Lexeme == "length") ||
                 (aliases.Contains(v.Name.Lexeme) && scalar.Contains(e.Name.Lexeme)))) return;
            base.VisitGet(e);
        }
        protected override void VisitGetIndex(Expr.GetIndex e)
        {
            if (!_mutationTarget && !e.Optional && e.Object is Expr.Get { Optional: false, Object: Expr.Variable v } field &&
                aliases.Contains(v.Name.Lexeme) && numeric.Contains(field.Name.Lexeme))
            { Visit(e.Index); return; }
            base.VisitGetIndex(e);
        }
        protected override void VisitVariable(Expr.Variable e)
        { if (e.Name.Lexeme == list || aliases.Contains(e.Name.Lexeme)) Unsafe = true; }
        protected override void VisitAssign(Expr.Assign e)
        { if (e.Name.Lexeme == list || aliases.Contains(e.Name.Lexeme)) Unsafe = true; base.VisitAssign(e); }
        protected override void VisitDelete(Expr.Delete e) => VisitMutation(e.Operand);
        protected override void VisitPrefixIncrement(Expr.PrefixIncrement e) => VisitMutation(e.Operand);
        protected override void VisitPostfixIncrement(Expr.PostfixIncrement e) => VisitMutation(e.Operand);

        private void VisitMutation(Expr operand)
        {
            bool previous = _mutationTarget;
            _mutationTarget = true;
            try { Visit(operand); }
            finally { _mutationTarget = previous; }
        }
    }
}
