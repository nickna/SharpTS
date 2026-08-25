using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Recognizes a side-effect-free counted numeric Map fill:
/// <code>for (let i = 0; i &lt; n; i++) map.set(pureKey, pureValue);</code>
/// so promoted storage can reserve capacity once before the first iteration.
/// </summary>
internal static class CountedNumericMapSetLoopAnalyzer
{
    internal readonly record struct Reservation(
        Expr.Variable Map,
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
        {
            return false;
        }

        if (loop.Condition is not Expr.Binary
            {
                Left: Expr.Variable { Name.Lexeme: var conditionCounter },
                Operator.Type: TokenType.LESS,
                Right: Expr.Variable bound
            }
            || conditionCounter != counter)
        {
            return false;
        }

        if (loop.Increment is not Expr.PostfixIncrement
            {
                Operand: Expr.Variable { Name.Lexeme: var incrementCounter },
                Operator.Type: TokenType.PLUS_PLUS
            }
            || incrementCounter != counter)
        {
            return false;
        }

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
                        Object: Expr.Variable map,
                        Name.Lexeme: "set"
                    },
                    Arguments: [var key, var value]
                }
            }
            || !IsPure(key)
            || !IsPure(value))
        {
            return false;
        }

        reservation = new Reservation(map, bound);
        return true;
    }

    private static bool IsPure(Expr expression) => expression switch
    {
        Expr.Literal => true,
        Expr.Variable => true,
        Expr.Grouping grouping => IsPure(grouping.Expression),
        Expr.Unary unary => IsPure(unary.Right),
        Expr.Binary binary => IsPure(binary.Left) && IsPure(binary.Right),
        _ => false
    };
}
