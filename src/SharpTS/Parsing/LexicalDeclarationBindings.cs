namespace SharpTS.Parsing;

/// <summary>Finds lexical bindings owned by one statement-list scope.</summary>
internal static class LexicalDeclarationBindings
{
    internal static IEnumerable<Token> Collect(IEnumerable<Stmt> statements)
    {
        foreach (var statement in statements)
            foreach (var name in Collect(statement))
                yield return name;
    }

    private static IEnumerable<Token> Collect(Stmt statement)
    {
        switch (statement)
        {
            case Stmt.Var { IsVar: false, IsDeclare: false } variable:
                yield return variable.Name;
                break;
            case Stmt.Const constant:
                yield return constant.Name;
                break;
            case Stmt.Using resource:
                foreach (var binding in resource.Bindings)
                    if (binding.Name is { } name) yield return name;
                break;
            case Stmt.Sequence sequence:
                foreach (var name in Collect(sequence.Statements))
                    yield return name;
                break;
            case Stmt.Export { Declaration: { } declaration }:
                foreach (var name in Collect(declaration))
                    yield return name;
                break;
        }
    }
}
