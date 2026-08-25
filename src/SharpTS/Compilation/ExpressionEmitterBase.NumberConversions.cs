using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    /// <summary>
    /// Emits stable, typed Number formatting calls without crossing the boxed
    /// object/object runtime ABI. All observable/dynamic cases return false.
    /// </summary>
    protected bool TryEmitStableNumberConversionCall(
        Expr.Get methodGet,
        IReadOnlyList<Expr> arguments)
    {
        if (methodGet.Optional
            || Ctx.RuntimeFeatures?.UsesNumberPrototypeMutation == true
            || !IsStaticallyNumber(Ctx.TypeMap?.Get(methodGet.Object)))
        {
            return false;
        }

        switch (methodGet.Name.Lexeme)
        {
            case "toString":
                if (arguments.Count == 1
                    && (!TryGetInt32Literal(arguments[0], out int radix) || radix != 10))
                {
                    return false;
                }
                if (arguments.Count > 1)
                    return false;

                if (TryEmitIntegerCounterDecimalString(methodGet.Object))
                    return true;

                EmitExpressionAsDouble(methodGet.Object);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.FormatNumber);
                SetStackType(StackType.String);
                return true;

            case "toFixed":
                int digits;
                if (arguments.Count == 0)
                {
                    digits = 0;
                }
                else if (arguments.Count == 1
                    && TryGetInt32Literal(arguments[0], out int literalDigits)
                    && literalDigits is >= 0 and <= 100)
                {
                    digits = literalDigits;
                }
                else
                {
                    return false;
                }

                EmitExpressionAsDouble(methodGet.Object);
                IL.Emit(OpCodes.Ldc_I4, digits);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.NumberToFixedDouble);
                SetStackType(StackType.String);
                return true;
        }

        return false;
    }

    /// <summary>
    /// Sync ILEmitter overrides this for its native Int64 for-loop counters.
    /// State machines and general doubles use FormatNumber(double).
    /// </summary>
    protected virtual bool TryEmitIntegerCounterDecimalString(Expr expression) => false;

    /// <summary>
    /// Folds <c>parseInt(counter.toString(), 10)</c> only when the receiver is a
    /// native Int64 loop-counter expression. Every Int64 is below JavaScript's
    /// 1e21 exponential-format threshold, so parsing its stable decimal spelling
    /// is exactly the original numeric value and neither intermediate operation
    /// is observable.
    /// </summary>
    internal bool TryEmitStableIntegerCounterParseInt(Expr expression)
    {
        if (!CanEmitStableIntegerCounterParseInt(expression))
            return false;

        var toStringCall = (Expr.Call)expression;
        var methodGet = (Expr.Get)toStringCall.Callee;
        return TryEmitIntegerCounterDecimalValue(methodGet.Object);
    }

    /// <summary>
    /// Non-emitting companion used while a call handler selects its stack ABI.
    /// </summary>
    internal bool CanEmitStableIntegerCounterParseInt(Expr expression)
    {
        if (Ctx.RuntimeFeatures?.UsesNumberPrototypeMutation == true
            || expression is not Expr.Call
            {
                Optional: false,
                Callee: Expr.Get
                {
                    Optional: false,
                    Name.Lexeme: "toString"
                } methodGet
            } toStringCall
            || toStringCall.Arguments.Count > 1
            || (toStringCall.Arguments.Count == 1
                && (!TryGetInt32Literal(toStringCall.Arguments[0], out int radix)
                    || radix != 10))
            || !IsStaticallyNumber(Ctx.TypeMap?.Get(methodGet.Object)))
        {
            return false;
        }

        return IsIntegerCounterDecimalValue(methodGet.Object);
    }

    /// <summary>
    /// Sync ILEmitter overrides this for its native Int64 for-loop counters.
    /// Other emitters conservatively retain the string-and-scan path.
    /// </summary>
    protected virtual bool TryEmitIntegerCounterDecimalValue(Expr expression) => false;

    /// <summary>Non-emitting probe paired with the value emitter.</summary>
    protected virtual bool IsIntegerCounterDecimalValue(Expr expression) => false;

    private static bool IsStaticallyNumber(TypeInfo? type) => type switch
    {
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => true,
        TypeInfo.NumberLiteral => true,
        TypeInfo.Union union => union.Types.Count > 0 && union.Types.All(IsStaticallyNumber),
        _ => false
    };

    internal static bool TryGetInt32Literal(Expr expression, out int value)
    {
        double number;
        switch (expression)
        {
            case Expr.Literal { Value: double literal }:
                number = literal;
                break;
            case Expr.Unary
            {
                Operator.Type: TokenType.MINUS,
                Right: Expr.Literal { Value: double literal }
            }:
                number = -literal;
                break;
            default:
                value = 0;
                return false;
        }

        if (!double.IsFinite(number)
            || number != Math.Truncate(number)
            || number < int.MinValue
            || number > int.MaxValue)
        {
            value = 0;
            return false;
        }

        value = (int)number;
        return true;
    }
}
