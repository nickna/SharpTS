using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private RuntimeValue EvaluateSetSuper(Expr.SetSuper expression) =>
        EvaluateSetSuperCore(_syncContext, expression).GetAwaiter().GetResult();

    private ValueTask<RuntimeValue> EvaluateSetSuperAsync(Expr.SetSuper expression) =>
        EvaluateSetSuperCore(_asyncContext, expression);

    private async ValueTask<RuntimeValue> EvaluateSetSuperCore(
        IEvaluationContext context, Expr.SetSuper expression)
    {
        var reference = expression.Target;
        string name = reference.Method!.Lexeme;
        var superclass = context.Environment.Get(reference.Keyword).AsObject<SharpTSClass>()!;

        // Resolve the reference before the RHS runs. Arrows inherit the lexical
        // home and receiver, and a suspended RHS must retain this same base.
        context.Environment.TryGetThis(out var thisValue);
        object? receiver = thisValue.ToObject();
        var owner = context.Environment.PrivateClass;
        object? target = context.Environment.StaticSuper
            ? owner is null ? superclass : ObjectBuiltIns.PrototypeOf(this, owner)
            : owner is null ? superclass.Prototype : ObjectBuiltIns.PrototypeOf(this, owner.Prototype);

        RuntimeValue current = RuntimeValue.Undefined;
        if (expression.Operator is { } op)
        {
            if (target is null or SharpTSUndefined)
                throw new ThrowException(new SharpTSTypeError($"Cannot read super property '{name}'"));
            current = RuntimeValue.FromBoxed(GetPropertyValue(target, name, receiver));
            if (op.Type == TokenType.AND_AND_EQUAL && !current.IsTruthy() ||
                op.Type == TokenType.OR_OR_EQUAL && current.IsTruthy() ||
                op.Type == TokenType.QUESTION_QUESTION_EQUAL && !current.IsNullish)
                return current;
        }
        RuntimeValue value = await context.EvaluateExprAsync(expression.Value);
        if (expression.Operator is { Type: not (TokenType.AND_AND_EQUAL or TokenType.OR_OR_EQUAL or TokenType.QUESTION_QUESTION_EQUAL) } compound)
            value = ApplyCompoundOperatorRV(compound.Type, current, value);
        if (target is null or SharpTSUndefined ||
            !SharpTSProxy.OrdinarySet(this, target, name, value.ToObject(), receiver))
        {
            // Class code is strict: a refused [[Set]] is an abrupt completion.
            throw new ThrowException(new SharpTSTypeError(
                $"Cannot assign to super property '{name}'"));
        }
        return value;
    }
}
