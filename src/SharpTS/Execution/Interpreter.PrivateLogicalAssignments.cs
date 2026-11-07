using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private async ValueTask<RuntimeValue> EvaluateLogicalSetPrivateCore(
        IEvaluationContext ctx, Expr.LogicalSetPrivate logical)
    {
        object? receiver = (await ctx.EvaluateExprAsync(logical.Object)).ToObject();
        string name = logical.Name.Lexeme;
        var owner = ctx.Environment.FindPrivateClass(name);
        if (owner is null)
            throw new ThrowException(new SharpTSTypeError($"Private member {name} is not declared in this scope"));

        // GetValue always checks this reference's brand, including when the
        // logical condition will skip the RHS and the eventual write.
        bool isStatic = owner.HasStaticPrivateField(name);
        bool isMethod = owner.GetPrivateMethod(name) is not null || owner.GetStaticPrivateMethod(name) is not null;
        bool isAccessor = owner.TryGetPrivateAccessor(name, out bool isStaticAccessor, out var getter, out var setter);
        RuntimeValue current;
        if (isAccessor)
        {
            current = InvokePrivateAccessor(owner, receiver, name, isStaticAccessor, getter);
        }
        else if (isMethod)
        {
            current = GetPrivateCore(receiver, name);
        }
        else if (isStatic)
        {
            if (!ReferenceEquals(receiver, owner) || !owner.HasInstalledStaticPrivateField(name))
                throw new ThrowException(new SharpTSTypeError($"Cannot access private member {name} from an object whose class did not declare it or before initialization"));
            current = owner.GetStaticPrivateFieldRV(name);
        }
        else
        {
            if (receiver is null || !owner.HasInstalledPrivateField(receiver, name))
                throw new ThrowException(new SharpTSTypeError($"Cannot access private member {name} from an object whose class did not declare it or before initialization"));
            current = owner.GetPrivateFieldRV(receiver, name);
        }

        bool assign = logical.Operator.Type switch
        {
            TokenType.AND_AND_EQUAL => current.IsTruthy(),
            TokenType.OR_OR_EQUAL => !current.IsTruthy(),
            TokenType.QUESTION_QUESTION_EQUAL => current.IsNullish,
            _ => throw new InvalidOperationException($"Unknown logical assignment operator: {logical.Operator.Type}")
        };
        if (!assign) return current;

        RuntimeValue value = await ctx.EvaluateExprAsync(logical.Value);
        if (isAccessor)
            return InvokePrivateAccessor(owner, receiver, name, isStaticAccessor, setter, value);
        if (isMethod)
            throw new ThrowException(new SharpTSTypeError($"Private method {name} is not writable"));

        // Keep the owner and receiver selected before the RHS, even when it
        // changes a receiver variable or suspends the current invocation.
        if (isStatic)
            owner.SetStaticPrivateField(name, value.ToObject());
        else
            owner.SetPrivateField(receiver!, name, value.ToObject());
        return value;
    }
}
