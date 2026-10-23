using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    internal TypeInfo VisitInstantiation(Expr.Instantiation expression)
    {
        TypeInfo operand = CheckExpr(expression.Expression);
        var arguments = expression.TypeArgs
            .Select((_, index) => ResolveTypeArg(expression.TypeArgs, expression.TypeArgNodes, index)).ToList();
        if (operand is TypeInfo.GenericFunction function)
            return InstantiateGenericFunction(function, arguments, preserveCallableMetadata: true);

        // Other callable representations do not retain enough per-signature generic origin
        // information for this specialization path. Never erase their arguments into Any.
        throw new TypeCheckException(
            $"Type '{operand}' has no supported generic signature for this type argument list.",
            expression.LessThan.Line, tsCode: "TS2635");
    }
}
