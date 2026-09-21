using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>Expressions evaluated in the enclosing scope when defining a class.</summary>
internal static class ClassDefinitionExpressions
{
    internal static IEnumerable<Expr> Enumerate(Stmt.Class declaration) =>
        Enumerate(declaration.SuperclassExpr, declaration.Fields, declaration.Methods, declaration.Accessors);

    internal static IEnumerable<Expr> Enumerate(Expr.ClassExpr expression) =>
        Enumerate(expression.SuperclassExpr, expression.Fields, expression.Methods, expression.Accessors);

    private static IEnumerable<Expr> Enumerate(Expr? superclass, List<Stmt.Field> fields,
        List<Stmt.Function> methods, List<Stmt.Accessor>? accessors)
    {
        if (superclass != null)
            yield return superclass;
        foreach (var key in MemberKeys(fields, methods, accessors))
            yield return key;
    }

    internal static IEnumerable<Expr> MemberKeys(IEnumerable<Stmt.Field> fields,
        IEnumerable<Stmt.Function> methods, IEnumerable<Stmt.Accessor>? accessors) =>
        fields.Select(field => (field.Name.Start, field.ComputedKey))
            .Concat(methods.Select(method => (method.Name.Start, method.ComputedKey)))
            .Concat((accessors ?? []).Select(accessor => (accessor.Name.Start, accessor.ComputedKey)))
            .Where(member => member.ComputedKey != null)
            .OrderBy(member => member.Start)
            .Select(member => member.ComputedKey!);
}
