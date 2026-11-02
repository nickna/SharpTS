using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

internal sealed partial class RawCallStorageAnalysis
{
    internal bool HasDynamicNumericArgumentProducer { get; private set; }

    private bool SeedDynamicNumericArguments()
    {
        bool changed = false;
        // These existing binding sets are possible source values, never native
        // callable authority. Finish const-alias propagation before examining
        // the actual source parameter records reached by an argument site.
        while (PropagateCallableValues()) { }
        foreach (var flow in _flows)
            if (IsNumericDestination(_map.GetDeclaredValueType(flow.Target))
                && !HasUnclassifiedNumericOperation(flow.Value)
                && !IsProvenNumericArgument(flow.Value))
            {
                changed |= MarkRawFlow(flow.Target, flow.Declaration, true, false, true);
                changed |= MarkDynamicNumericArgument(flow.Value);
            }
        foreach (var site in _argumentSites)
        {
            if (site.Callee is null) continue;
            foreach (var producer in Producers(site.Callee).Values)
            {
                if (site.Arguments.Any(argument => argument is Expr.Spread))
                {
                    if (!producer.Parameters.Any(parameter => IsNumericDestination(_map.GetDeclaredValueType(parameter.Name))))
                        continue;
                    // A shared tuple/array can contain values written through
                    // any. Unknown spread positions use the existing conservative
                    // source-parameter fallback, rather than assuming checked slots.
                    foreach (var argument in site.Arguments)
                        if (!(argument is Expr.Spread spread
                            ? IsFreshNumericSpread(spread.Expression)
                            : IsProvenNumericArgument(argument)))
                            changed |= MarkDynamicNumericArgument(argument);
                    continue;
                }
                for (int index = 0; index < Math.Min(producer.Parameters.Count, site.Arguments.Count); index++)
                {
                    var parameter = producer.Parameters[index];
                    var argument = site.Arguments[index];
                    if (IsNumericDestination(_map.GetDeclaredValueType(parameter.Name))
                        && !IsProvenNumericArgument(argument))
                        changed |= MarkDynamicNumericArgument(argument);
                }
            }
        }
        return changed;
    }

    private static bool IsNumericDestination(TypeInfo? type)
        => type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral;

    private bool HasUnclassifiedNumericOperation(Expr expression)
    {
        var actual = ExpressionUnwrapper.Unwrap(expression);
        // Existing operation/iterator analyses own an operator's result kind.
        // Missing checked detail alone must not erase their numeric proof;
        // actual raw-operation evidence still takes precedence.
        return actual is Expr.Binary or Expr.Unary
            && _map.Get(actual) is TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Inferred
            && !_map.IsRawNumericResult(actual) && !_map.IsUndefinedReachableReturn(actual);
    }

    private bool IsProvenNumericArgument(Expr expression)
    {
        var actual = ExpressionUnwrapper.Unwrap(expression);
        return IsNumericDestination(_map.Get(actual))
            && !_map.IsRawNumericResult(actual) && !_map.IsUndefinedReachableReturn(actual)
            && (actual is not Expr.Variable variable
                || !IsRawBinding(variable.Name, Evidence.Any) && !_map.IsRawNumericStorage(variable.Name));
    }

    private bool IsFreshNumericSpread(Expr expression)
        => ExpressionUnwrapper.Unwrap(expression) is Expr.ArrayLiteral array
            && array.Elements.All(element => element is not Expr.Spread && IsProvenNumericArgument(element));

    private bool MarkDynamicNumericArgument(Expr argument)
    {
        // Negative representation evidence only; this never grants native CALL proof.
        bool changed = !HasDynamicNumericArgumentProducer;
        _hasNumericProducer = true;
        HasDynamicNumericArgumentProducer = true;
        foreach (var expression in EquivalentExpressions(argument))
            changed |= _map.MarkRawNumericResult(expression);
        return changed;
    }
}
