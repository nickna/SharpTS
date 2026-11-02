using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

internal sealed partial class RawCallStorageAnalysis
{
    private bool SeedDynamicStringArguments()
    {
        var parameters = _sourceParameters.Where(IsStringSourceParameter).ToArray();
        if (parameters.Length == 0) return false;
        while (PropagateCallableValues()) { }

        bool changed = false;
        foreach (var site in _argumentSites)
        {
            bool possible = site.Callee is null
                ? site.Arguments.Any(IsDynamicStringInput)
                : Producers(site.Callee).Values.Any(producer =>
                    producer.Parameters.Select((parameter, index) => (parameter, index)).Any(item =>
                        IsStringSourceParameter(item.parameter) &&
                        item.index < site.Arguments.Count && !IsProvenStringArgument(site.Arguments[item.index])))
                    || _map.Get(site.Callee) switch
                    {
                        TypeInfo.Function signature => signature.ParamTypes.Select((type, index) => (type, index)).Any(item =>
                            IsStringDestination(item.type) && item.index < site.Arguments.Count &&
                            !IsProvenStringArgument(site.Arguments[item.index])),
                        TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Inferred =>
                            site.Arguments.Any(argument => !IsProvenStringArgument(argument)),
                        _ => site.Arguments.Any(IsDynamicStringInput)
                    };
            if (!possible) continue;

            // These are possible source destinations, never native-call authority.
            // Unknown or mutable callees can reach any actual string parameter.
            foreach (var parameter in parameters)
                changed |= MarkRawFlow(parameter.Name, parameter, true, false, true);
            foreach (var argument in site.Arguments.Where(IsDynamicStringInput))
            {
                foreach (var expression in EquivalentExpressions(argument))
                    changed |= _map.MarkRawNumericResult(expression);
                if (ExpressionUnwrapper.Unwrap(argument) is Expr.Variable variable)
                    changed |= MarkRawFlow(variable.Name, null, true, false, true);
            }
        }
        return changed;
    }

    private bool IsStringSourceParameter(Stmt.Parameter parameter) =>
        IsStringDestination(_sourceParameterTypes.GetValueOrDefault(parameter)
            ?? _map.GetDeclaredValueType(parameter.Name));

    private static bool IsStringDestination(TypeInfo? type) => type is
        TypeInfo.Primitive { Type: TokenType.TYPE_STRING } or TypeInfo.String
        or TypeInfo.StringLiteral or TypeInfo.TemplateLiteralType;

    private bool IsProvenStringArgument(Expr expression)
    {
        var actual = ExpressionUnwrapper.Unwrap(expression);
        return IsStringDestination(_map.Get(actual)) && !_map.IsRawNumericResult(actual)
            && !_map.IsUndefinedReachableReturn(actual)
            && (actual is not Expr.Variable variable ||
                !IsRawBinding(variable.Name, Evidence.Any) && !_map.IsRawNumericStorage(variable.Name));
    }

    private bool IsDynamicStringInput(Expr expression)
    {
        var actual = ExpressionUnwrapper.Unwrap(expression);
        return _map.Get(expression) is TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Inferred
            || _map.IsRawNumericResult(actual) || _map.IsUndefinedReachableReturn(actual)
            || actual is Expr.Variable variable && IsRawBinding(variable.Name, Evidence.Any);
    }
}
