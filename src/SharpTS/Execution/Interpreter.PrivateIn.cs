using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    internal RuntimeValue VisitPrivateIn(Expr.PrivateIn expr)
        => PrivateInCore(Evaluate(expr.Object), expr.Name.Lexeme);

    internal async ValueTask<RuntimeValue> VisitPrivateInAsync(Expr.PrivateIn expr)
        => PrivateInCore((await EvaluateAsync(expr.Object)).ToObject(), expr.Name.Lexeme);

    private RuntimeValue PrivateInCore(object? receiver, string name)
    {
        if (receiver is null or SharpTSUndefined or string or double or bool or SharpTSBigInt or SharpTSSymbol)
            throw new ThrowException(new SharpTSTypeError("Right-hand side of private 'in' is not an object"));
        var owner = _environment.FindPrivateClass(name)
            ?? throw new InterpreterException("Private name has no lexical class owner");
        return RuntimeValue.FromBoolean(owner.HasStaticPrivateField(name)
            ? ReferenceEquals(receiver, owner) && owner.HasInstalledStaticPrivateField(name)
            : owner.GetStaticPrivateMethod(name) != null
                ? ReferenceEquals(receiver, owner)
                : owner.GetPrivateMethod(name) != null
                    ? owner.HasPrivateBrand(receiver)
                    : owner.HasInstalledPrivateField(receiver, name));
    }
}
