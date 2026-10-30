namespace SharpTS.Parsing;

/// <summary>Original public declarations in a same-scope lowered export list.</summary>
internal static class ExportDeclarationSequence
{
    internal static IEnumerable<Stmt> Declarations(Stmt declaration)
    {
        if (declaration is Stmt.Sequence sequence)
        {
            foreach (var child in sequence.Statements)
                foreach (var item in Declarations(child))
                    yield return item;
            yield break;
        }

        if (declaration is Stmt.Var { Name.IsGeneratedTemporary: true }
            or Stmt.Const { Name.IsGeneratedTemporary: true })
            yield break;
        yield return declaration;
    }
}
