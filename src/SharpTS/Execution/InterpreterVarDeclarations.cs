using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Execution;

/// <summary>
/// Finds loop-head var declarations owned by the current execution unit without
/// changing the AST shared with the checker and compiler.
/// </summary>
internal static class InterpreterVarDeclarations
{
    internal static List<Token> CollectLoopHeads(IEnumerable<Stmt> statements)
    {
        var collector = new LoopHeadCollector();
        foreach (var statement in statements)
            collector.Visit(statement);
        return collector.Names;
    }

    private sealed class LoopHeadCollector : AstVisitorBase
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        internal List<Token> Names { get; } = [];

        // Expressions and nested declaration bodies have their own var owner.
        public override void Visit(Expr expr) { }
        protected override void VisitFunction(Stmt.Function stmt) { }
        protected override void VisitClass(Stmt.Class stmt) { }
        protected override void VisitNamespace(Stmt.Namespace stmt) { }
        protected override void VisitStaticBlock(Stmt.StaticBlock stmt) { }
        protected override void VisitDeclareModule(Stmt.DeclareModule stmt) { }
        protected override void VisitDeclareGlobal(Stmt.DeclareGlobal stmt) { }

        protected override void VisitForOf(Stmt.ForOf stmt)
        {
            if (stmt.IsDeclaration && stmt.IsVar) Add(stmt.Variable);
            Visit(stmt.Body);
        }

        protected override void VisitForIn(Stmt.ForIn stmt)
        {
            if (stmt.IsDeclaration && stmt.IsVar) Add(stmt.Variable);
            Visit(stmt.Body);
        }

        private void Add(Token name)
        {
            if (_seen.Add(name.Lexeme)) Names.Add(name);
        }
    }
}
