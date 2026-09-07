using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>Removes grouping and type-only wrappers without changing the underlying expression.</summary>
internal static class ExpressionUnwrapper
{
    public static Expr Unwrap(Expr expression)
    {
        while (true)
        {
            switch (expression)
            {
                case Expr.Grouping grouping:
                    expression = grouping.Expression;
                    continue;
                case Expr.TypeAssertion assertion:
                    expression = assertion.Expression;
                    continue;
                case Expr.Satisfies satisfies:
                    expression = satisfies.Expression;
                    continue;
                case Expr.NonNullAssertion nonNull:
                    expression = nonNull.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }
}
