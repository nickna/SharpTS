using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Recognizes a side-effect-free sequential index-write loop:
/// <code>for (let i = 0; i &lt; n; i++) items[i] = pureValue;</code>
/// The emitter may reserve packed numeric storage without changing the array's
/// length or evaluating guest expressions early.
/// </summary>
internal static class CountedIndexWriteLoopAnalyzer
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
            || initial != 0
            || loop.Condition is not Expr.Binary
            {
                Left: Expr.Variable { Name.Lexeme: var conditionCounter },
                Operator.Type: TokenType.LESS,
                Right: Expr.Variable bound
            }
            || conditionCounter != counter
            || loop.Increment is not Expr.PostfixIncrement
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
                Expr: Expr.SetIndex
                {
                    Object: Expr.Variable array,
                    Index: Expr.Variable { Name.Lexeme: var indexCounter },
                    Value: var value
                }
            }
            || indexCounter != counter
            || !IsPure(value))
        {
            return false;
        }

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
        _ => false
    };
}
