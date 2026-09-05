using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Resolves exact call nodes to stable declarations. Local aliases are admitted
/// only in the caller's leading lexical scope, after initialization, and with a
/// unique declaration in that caller. Nested scopes with a conflicting name
/// conservatively disable that alias. No alias proof crosses a callable boundary.
/// </summary>
internal static class NumericRestCallBindingAnalyzer
{
    public static Dictionary<Stmt.Function, List<Expr.Call>> Analyze(
        IReadOnlyList<Stmt> statements, IReadOnlyList<Stmt.Function> functions,
        IReadOnlySet<Stmt.Function> stableFunctions)
    {
        var globals = functions.Where(stableFunctions.Contains)
            .GroupBy(f => f.Name.Lexeme).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var result = new Dictionary<Stmt.Function, List<Expr.Call>>(ReferenceEqualityComparer.Instance);
        var moduleNames = new Declarations();
        foreach (var statement in statements) moduleNames.Visit(statement);
        // The function declaration itself contributes one. Any other declaration
        // of the name in the module prevents direct binding resolution.
        foreach (string name in globals.Keys.ToArray())
            if (moduleNames.Counts.GetValueOrDefault(name) != 1) globals.Remove(name);

        new Calls(globals, moduleNames, result, allowAliases: false).VisitStatements(statements);
        foreach (var function in functions)
        {
            if (function.Body == null) continue;
            var names = new Declarations();
            foreach (var parameter in function.Parameters) names.Add(parameter.Name.Lexeme);
            foreach (var statement in function.Body) names.Visit(statement);
            new Calls(globals, names, result, allowAliases: true).VisitStatements(function.Body);
        }
        return result;
    }

    private sealed class Declarations : AstVisitorBase
    {
        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
        public void Add(string name) => Counts[name] = Counts.GetValueOrDefault(name) + 1;
        protected override void VisitVar(Stmt.Var stmt) { Add(stmt.Name.Lexeme); base.VisitVar(stmt); }
        protected override void VisitConst(Stmt.Const stmt) { Add(stmt.Name.Lexeme); base.VisitConst(stmt); }
        protected override void VisitFunction(Stmt.Function stmt) => Add(stmt.Name.Lexeme);
        protected override void VisitClass(Stmt.Class stmt) => Add(stmt.Name.Lexeme);
        protected override void VisitEnum(Stmt.Enum stmt) => Add(stmt.Name.Lexeme);
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
        protected override void VisitClassExpr(Expr.ClassExpr expr) { }
        protected override void VisitForOf(Stmt.ForOf stmt) { Add(stmt.Variable.Lexeme); base.VisitForOf(stmt); }
        protected override void VisitForIn(Stmt.ForIn stmt) { Add(stmt.Variable.Lexeme); base.VisitForIn(stmt); }
        protected override void VisitTryCatch(Stmt.TryCatch stmt)
        {
            if (stmt.CatchParam != null) Add(stmt.CatchParam.Lexeme);
            base.VisitTryCatch(stmt);
        }
    }

    private sealed class Calls(Dictionary<string, Stmt.Function> globals, Declarations names,
        Dictionary<Stmt.Function, List<Expr.Call>> result, bool allowAliases) : AstVisitorBase
    {
        // The declaration node is retained to make the binding identity explicit.
        private readonly Dictionary<string, (Stmt.Const Binding, Stmt.Function Target)> _aliases = [];

        public void VisitStatements(IEnumerable<Stmt> statements)
        {
            foreach (var statement in statements)
            {
                if (allowAliases && statement is Stmt.Const declaration
                    && names.Counts.GetValueOrDefault(declaration.Name.Lexeme) == 1
                    && declaration.Initializer is Expr.Variable source)
                {
                    Visit(declaration.Initializer);
                    if (Resolve(source.Name.Lexeme) is { } target)
                        _aliases[declaration.Name.Lexeme] = (declaration, target);
                }
                else Visit(statement);
            }
        }

        private Stmt.Function? Resolve(string name)
        {
            if (_aliases.TryGetValue(name, out var alias)) return alias.Target;
            // At module level, the sole function declaration is the global.
            if (names.Counts.ContainsKey(name) && allowAliases) return null;
            return globals.GetValueOrDefault(name);
        }

        protected override void VisitCall(Expr.Call call)
        {
            if (!call.Optional && call.Callee is Expr.Variable variable
                && Resolve(variable.Name.Lexeme) is { } target)
            {
                if (!result.TryGetValue(target, out var calls)) result[target] = calls = [];
                calls.Add(call);
            }
            base.VisitCall(call);
        }

        // Do not inherit proofs into closures, defaults, methods, or class fields.
        protected override void VisitFunction(Stmt.Function stmt) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
        protected override void VisitClass(Stmt.Class stmt) { }
        protected override void VisitClassExpr(Expr.ClassExpr expr) { }
    }
}
