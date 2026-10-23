using SharpTS.Parsing;

namespace SharpTS.Compilation;

internal static class ParameterPatternBindings
{
    internal static IEnumerable<(Stmt Declaration, Token Name)> Declarations(List<Stmt>? body)
    {
        if (body is null) yield break;
        foreach (var initializer in body.OfType<Stmt.Sequence>().Where(sequence => sequence.ParameterName != null))
            foreach (var declaration in Collect(initializer.Statements))
                yield return declaration;
    }

    private static IEnumerable<(Stmt Declaration, Token Name)> Collect(IEnumerable<Stmt> statements)
    {
        foreach (var statement in statements)
            switch (statement)
            {
                case Stmt.Var variable: yield return (variable, variable.Name); break;
                case Stmt.Const constant: yield return (constant, constant.Name); break;
                case Stmt.Sequence sequence:
                    foreach (var declaration in Collect(sequence.Statements)) yield return declaration;
                    break;
            }
    }
}
