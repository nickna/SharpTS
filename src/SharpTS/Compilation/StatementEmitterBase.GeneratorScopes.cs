using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public abstract partial class StatementEmitterBase
{
    private readonly HashSet<Stmt> _activeLexicalLoopScopes = new(ReferenceEqualityComparer.Instance);

    private (Stmt Loop, List<LoopBodyCellScopeEntry> Cells)? EnterGeneratorLoopScope(Stmt statement)
    {
        while (statement is Stmt.LabeledStatement labeled) statement = labeled.Statement;
        if (statement is not (Stmt.For or Stmt.ForOf or Stmt.ForIn)) return null;
        if (_activeLexicalLoopScopes.Contains(statement)) return null;
        List<(Stmt Declaration, Token Token)> bindings = statement switch
        {
            Stmt.For { Initializer: { } initializer } => DirectLexicalDeclarations([initializer]).ToList(),
            Stmt.ForOf { IsDeclaration: true, IsVar: false } loop => [(Declaration: statement, Token: loop.Variable)],
            Stmt.ForIn { IsDeclaration: true, IsVar: false } loop => [(Declaration: statement, Token: loop.Variable)],
            _ => []
        };
        if (!bindings.Any(binding => Ctx.ClosureAnalyzer?.IsLoopBodyCellDeclaration(binding.Token) == true))
            return null;

        Ctx.Locals.EnterScope();
        List<LoopBodyCellScopeEntry> cells = [];
        try
        {
            foreach (var (declaration, token) in bindings) EnterLexicalCellBinding(declaration, token, cells);
            _activeLexicalLoopScopes.Add(statement);
            return (statement, cells);
        }
        catch
        {
            ExitLoopBodyCellScope(cells);
            Ctx.Locals.ExitScope();
            throw;
        }
    }

    private void ExitGeneratorLoopScope((Stmt Loop, List<LoopBodyCellScopeEntry> Cells)? scope)
    {
        if (scope is not { } active) return;
        ExitLoopBodyCellScope(active.Cells);
        Ctx.Locals.ExitScope();
        _activeLexicalLoopScopes.Remove(active.Loop);
    }

    protected void CopyGeneratorForBindings(Stmt.For loop)
    {
        if (loop.Initializer is null) return;
        foreach (var (_, token) in DirectLexicalDeclarations([loop.Initializer])) TryCopyLexicalCell(token);
    }

    protected void InitializeGeneratorLoopLocal(Token token, LocalBuilder local)
    {
        if (Ctx.ClosureAnalyzer?.IsLoopBodyCellDeclaration(token) != true) return;
        IL.Emit(OpCodes.Ldloc, local);
        if (local.LocalType.IsValueType) IL.Emit(OpCodes.Box, local.LocalType);
        if (!TryInitializeLexicalCell(token, fresh: true))
            throw new InvalidOperationException($"Loop binding '{token.Lexeme}' has no lexical cell.");
    }

    protected void WithGeneratorLexicalScope(IEnumerable<Stmt> statements, Action emit,
        object? declaration = null, Token? binding = null, bool ordinaryHandler = false)
    {
        var body = statements as IReadOnlyList<Stmt> ?? statements.ToList();
        bool selected = binding is not null && Ctx.ClosureAnalyzer?.IsLoopBodyCellDeclaration(binding) == true
            || DirectLexicalDeclarations(body).Any(item => Ctx.ClosureAnalyzer?.IsLoopBodyCellDeclaration(item.Token) == true);
        bool lexicalHandler = ordinaryHandler && (binding is not null || DirectLexicalDeclarations(body).Any()
            || DirectOrdinaryUsingBindings(body).Any());
        if (!selected && !lexicalHandler) { emit(); return; }
        var variables = new HandlerVarCollector();
        if (ordinaryHandler)
            foreach (var statement in body) variables.Visit(statement);
        Ctx.Locals.EnterScope();
        List<LoopBodyCellScopeEntry> cells = [];
        try
        {
            if (binding is not null) EnterLexicalCellBinding(declaration!, binding, cells, ordinaryHandler);
            EnterLoopBodyCellScope(body, cells, ordinaryHandler);
            if (ordinaryHandler) EnterOrdinaryUsingCellScope(body, cells);
            emit();
        }
        finally
        {
            // Keep the physical slots that flat ordinary handlers previously
            // left in their surrounding scope, including nested handler vars.
            var locals = variables.Names.Select(name =>
                (Name: name, Local: Ctx.Locals.GetCurrentScopeLocal(name))).ToList();
            ExitLoopBodyCellScope(cells);
            Ctx.Locals.ExitScope();
            foreach (var (name, local) in locals)
                if (local is not null && !ReferenceEquals(local, Ctx.Locals.GetLocal(name)))
                    Ctx.Locals.RegisterLocal(name, local);
        }
    }

    private sealed class HandlerVarCollector : AstVisitorBase
    {
        public HashSet<string> Names { get; } = [];
        protected override void VisitVar(Stmt.Var stmt) { if (stmt.IsVar && !stmt.IsDeclare) Names.Add(stmt.Name.Lexeme); }
        protected override void VisitForOf(Stmt.ForOf stmt) { if (stmt.IsDeclaration && stmt.IsVar) Names.Add(stmt.Variable.Lexeme); Visit(stmt.Body); }
        protected override void VisitForIn(Stmt.ForIn stmt) { if (stmt.IsDeclaration && stmt.IsVar) Names.Add(stmt.Variable.Lexeme); Visit(stmt.Body); }
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
        protected override void VisitFunction(Stmt.Function stmt) { }
        protected override void VisitClass(Stmt.Class stmt) { }
        protected override void VisitClassExpr(Expr.ClassExpr expr) { }
        protected override void VisitNamespace(Stmt.Namespace stmt) { }
        protected override void VisitDeclareModule(Stmt.DeclareModule stmt) { }
    }

    protected void EmitGeneratorScopedStatements(List<Stmt> statements, bool ordinaryHandler = false) =>
        WithGeneratorLexicalScope(statements, () =>
        {
            foreach (var statement in statements) EmitStatement(statement);
        }, ordinaryHandler: ordinaryHandler);

    protected bool TryInitializeGeneratorCatchBinding(Token binding, LocalBuilder value, bool ordinaryHandler = false)
    {
        if (Ctx.ClosureAnalyzer?.IsLoopBodyCellDeclaration(binding) != true)
            return ordinaryHandler && TryInitializeLexicalShadow(binding, value);
        IL.Emit(OpCodes.Ldloc, value);
        if (!TryInitializeLexicalCell(binding))
            throw new InvalidOperationException($"Catch binding '{binding.Lexeme}' has no lexical cell.");
        return true;
    }
}
