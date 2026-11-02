using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

internal sealed partial class RawCallStorageAnalysis
{
    internal bool HasUnsafeStringReceiverProducer { get; private set; }

    private bool SeedUnsafeStringReceivers(Func<Expr, Evidence, bool>? passesRawValue = null)
    {
        bool changed = false, next;
        do
        {
            next = false;
            foreach (var flow in _flows)
            {
                if (!IsCheckedStringReceiver(_map.GetDeclaredValueType(flow.Target))) continue;
                var value = ExpressionUnwrapper.Unwrap(flow.Value);
                if (IsCheckedStringReceiver(_map.Get(value)) && value is not Expr.New
                    && !(value is Expr.Variable variable && (IsRawBinding(variable.Name, Evidence.Any)
                        || _map.IsRawNumericStorage(variable.Name)))
                    && passesRawValue?.Invoke(flow.Value, Evidence.Any) != true) continue;
                next |= MarkRawFlow(flow.Target, flow.Declaration, true, false, true);
                _map.MarkRawNumericStorage(flow.Target);
            }
            changed |= next;
        } while (next);

        foreach (var call in Calls)
        {
            if (call.Callee is not Expr.Get method || !IsCheckedStringReceiver(_map.Get(method.Object))) continue;
            var receiver = ExpressionUnwrapper.Unwrap(method.Object);
            if (!StringEmitter.RequiresRuntimeStringReceiver(_map, method.Object)
                && !(receiver is Expr.Variable variable && IsRawBinding(variable.Name, Evidence.Any))
                && passesRawValue?.Invoke(method.Object, Evidence.Any) != true) continue;
            foreach (var expression in EquivalentExpressions(method.Object))
                changed |= _map.MarkRawNumericResult(expression);
            foreach (var expression in EquivalentExpressions(call))
                changed |= _map.MarkRawNumericResult(expression);
            if (!HasUnsafeStringReceiverProducer)
            {
                HasUnsafeStringReceiverProducer = true;
                _hasNumericProducer = true;
                changed = true;
            }
        }
        return changed;
    }

    private static bool IsCheckedStringReceiver(TypeInfo? type) => type is
        TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.Primitive { Type: TokenType.TYPE_STRING };
}
