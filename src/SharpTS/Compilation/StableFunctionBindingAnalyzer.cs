using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Finds top-level function declarations whose module binding is never written after its
/// declaration. The result is deliberately conservative: a write to the same name anywhere in
/// the module disqualifies the function, even when a more precise scope analysis could prove that
/// the write targets a shadow. False negatives only retain value dispatch; false positives would
/// violate live ESM binding semantics.
/// </summary>
internal static class StableFunctionBindingAnalyzer
{
    public static void Analyze(
        IReadOnlyList<Stmt> statements,
        ISet<Stmt.Function> stableFunctions)
    {
        var functions = new List<Stmt.Function>();
        foreach (var statement in statements)
            CollectTopLevelFunctions(statement, functions);

        if (functions.Count == 0)
            return;

        var writes = new WrittenNameCollector();
        foreach (var statement in statements)
            writes.Visit(statement);

        var declarationCounts = functions
            .Where(function => function.Body != null)
            .GroupBy(function => function.Name.Lexeme, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var function in functions)
        {
            if (function.Body != null &&
                !writes.MayWriteUnknownBinding &&
                declarationCounts[function.Name.Lexeme] == 1 &&
                !writes.Names.Contains(function.Name.Lexeme))
            {
                stableFunctions.Add(function);
            }
        }
    }

    private static void CollectTopLevelFunctions(Stmt statement, List<Stmt.Function> functions)
    {
        switch (statement)
        {
            case Stmt.Function function:
                functions.Add(function);
                break;
            case Stmt.Export { Declaration: { } declaration }:
                CollectTopLevelFunctions(declaration, functions);
                break;
            case Stmt.Sequence sequence:
                foreach (var inner in sequence.Statements)
                    CollectTopLevelFunctions(inner, functions);
                break;
        }
    }

    private sealed class WrittenNameCollector : AstVisitorBase
    {
        public HashSet<string> Names { get; } = [];
        public bool MayWriteUnknownBinding { get; private set; }

        protected override void VisitCall(Expr.Call expr)
        {
            // A direct eval executes in the current lexical environment and can replace a
            // function binding using source that is not part of this AST. Conservatively retain
            // value dispatch for every function in a module containing one.
            if (expr.Callee is Expr.Variable { Name.Lexeme: "eval" } && !expr.Optional)
                MayWriteUnknownBinding = true;
            base.VisitCall(expr);
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            // These two forms are compiler-generated declaration initialization, not later
            // replacement of an established function binding.
            if (!expr.IsVarRedeclaration && !expr.IsLexicalInitialization)
                Names.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Names.Add(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
        {
            Names.Add(expr.Name.Lexeme);
            base.VisitLogicalAssign(expr);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr)
        {
            if (expr.Operand is Expr.Variable variable)
                Names.Add(variable.Name.Lexeme);
            base.VisitPrefixIncrement(expr);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr)
        {
            if (expr.Operand is Expr.Variable variable)
                Names.Add(variable.Name.Lexeme);
            base.VisitPostfixIncrement(expr);
        }

        protected override void VisitForOf(Stmt.ForOf stmt)
        {
            // ForOf does not retain whether its token came from a declaration or an assignment.
            // Treat either form as a possible write; this can only disable the optimization.
            Names.Add(stmt.Variable.Lexeme);
            base.VisitForOf(stmt);
        }

        protected override void VisitForIn(Stmt.ForIn stmt)
        {
            if (!stmt.IsDeclaration)
                Names.Add(stmt.Variable.Lexeme);
            base.VisitForIn(stmt);
        }
    }
}
