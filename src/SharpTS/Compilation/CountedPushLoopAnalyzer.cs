using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Recognizes the deliberately narrow, side-effect-free counted append loop:
/// <code>for (let i = 0; i &lt; n; i++) items.push(pureValue);</code>
/// The emitter can reserve <c>items</c> once without changing observable
/// evaluation order. Broader loops intentionally fall back to normal growth.
/// </summary>
internal static class CountedPushLoopAnalyzer
{
    internal readonly record struct Reservation(
        Expr.Variable Array,
        Expr.Variable Bound);

    internal static bool TryAnalyze(Stmt.For loop, out Reservation reservation)
    {
        reservation = default;
        if (loop.Initializer is not Stmt.Var
            {
                Name.Lexeme: var counter,
                Initializer: Expr.Literal { Value: double initial }
            }
            || initial != 0)
            return false;

        if (loop.Condition is not Expr.Binary
            {
                Left: Expr.Variable { Name.Lexeme: var conditionCounter },
                Operator.Type: TokenType.LESS,
                Right: Expr.Variable bound
            }
            || conditionCounter != counter)
            return false;

        if (loop.Increment is not Expr.PostfixIncrement
            {
                Operand: Expr.Variable { Name.Lexeme: var incrementCounter },
                Operator.Type: TokenType.PLUS_PLUS
            }
            || incrementCounter != counter)
            return false;

        Stmt body = loop.Body is Stmt.Block { Statements.Count: 1 } block
            ? block.Statements[0]
            : loop.Body;
        if (body is not Stmt.Expression
            {
                Expr: Expr.Call
                {
                    Optional: false,
                    Callee: Expr.Get
                    {
                        Optional: false,
                        Object: Expr.Variable array,
                        Name.Lexeme: "push"
                    },
                    Arguments: { Count: 1 } arguments
                }
            }
            || !IsPure(arguments[0]))
            return false;

        reservation = new Reservation(array, bound);
        return true;
    }

    private static bool IsPure(Expr expression) => expression switch
    {
        Expr.Literal => true,
        Expr.Variable => true,
        Expr.Grouping grouping => IsPure(grouping.Expression),
        Expr.Unary unary => IsPure(unary.Right),
        Expr.Binary binary => IsPure(binary.Left) && IsPure(binary.Right),
        Expr.ObjectLiteral literal => literal.Properties.All(property =>
            !property.IsSpread
            && property.Kind == Expr.ObjectPropertyKind.Value
            && property.Key is not Expr.ComputedKey
            && IsPure(property.Value)),
        Expr.ArrayLiteral literal => literal.Elements.All(IsPure),
        _ => false
    };
}
