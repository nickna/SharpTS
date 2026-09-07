using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// A conservative optimization identity, local to one visitor traversal. Scope zero is the
/// program; every function/arrow (including its parameter defaults) gets a fresh scope.
/// This is not lexical name resolution: blocks share their function's identity and duplicate
/// var/let/const declarations disable candidates. References in nested functions are keyed in
/// that nested scope; consumers must still apply their separate closure/capture proof.
/// </summary>
internal readonly record struct FunctionScopedBinding(int Scope, string Name);

/// <summary>
/// Bookkeeping for optimization passes with the same function-scope model. Counts declarations
/// during each consumer's existing semantic traversal, without introducing a separate program
/// walk. Candidate selection, allowed uses, and intrinsic mutation policies remain in consumers.
/// </summary>
internal abstract class FunctionScopedBindingVisitor : VariableWriteVisitor
{
    private int _scope;
    private int _nextScope;

    public Dictionary<FunctionScopedBinding, int> DeclarationCounts { get; } = [];
    public HashSet<FunctionScopedBinding> Disqualified { get; } = [];

    protected FunctionScopedBinding Binding(Token name) => new(_scope, name.Lexeme);

    protected override void VisitFunction(Stmt.Function statement)
    {
        int saved = _scope;
        _scope = ++_nextScope;
        try
        {
            base.VisitFunction(statement);
        }
        finally
        {
            _scope = saved;
        }
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expression)
    {
        int saved = _scope;
        _scope = ++_nextScope;
        try
        {
            base.VisitArrowFunction(expression);
        }
        finally
        {
            _scope = saved;
        }
    }

    protected sealed override void VisitVar(Stmt.Var statement) =>
        VisitDeclaration(statement.Name, statement.Initializer);

    protected sealed override void VisitConst(Stmt.Const statement) =>
        VisitDeclaration(statement.Name, statement.Initializer);

    private void VisitDeclaration(Token name, Expr? initializer)
    {
        var binding = Binding(name);
        DeclarationCounts[binding] = DeclarationCounts.GetValueOrDefault(binding) + 1;
        OnDeclaration(binding, name, initializer);
        if (initializer != null)
            Visit(initializer);
    }

    protected abstract void OnDeclaration(FunctionScopedBinding binding, Token name, Expr? initializer);

    protected override void OnVariableWrite(Token name) => Disqualified.Add(Binding(name));
}
