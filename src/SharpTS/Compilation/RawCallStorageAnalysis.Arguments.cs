using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

internal sealed partial class RawCallStorageAnalysis
{
    private sealed record SourceCallable(object Owner, IReadOnlyList<Stmt.Parameter> Parameters);
    private sealed record ArgumentSite(Expr? Callee, IReadOnlyList<Expr> Arguments);
    private sealed class PossibleProducers
    {
        internal HashSet<SourceCallable> Values { get; } = new(ReferenceEqualityComparer.Instance);
        internal bool Unknown { get; set; }
        internal bool Union(PossibleProducers other)
        {
            bool changed = false;
            foreach (var value in other.Values) changed |= Values.Add(value);
            if (other.Unknown && !Unknown) { Unknown = true; changed = true; }
            return changed;
        }
    }

    private readonly Dictionary<object, SourceCallable> _sourceCallables = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Stmt.Parameter> _sourceParameters = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingSymbol, PossibleProducers> _possibleProducers = new(ReferenceEqualityComparer.Instance);
    private readonly List<ArgumentSite> _argumentSites = [];
    private readonly List<(Expr Result, Expr Operand)> _numericOperations = [];

    private bool _hasNumericProducer;
    internal bool HasNumericProducer => _hasNumericProducer;

    private void RegisterCallable(object owner, IReadOnlyList<Stmt.Parameter> parameters, Token? name)
    {
        var callable = new SourceCallable(owner, Array.AsReadOnly(parameters.ToArray()));
        _sourceCallables[owner] = callable;
        foreach (var parameter in parameters) _sourceParameters.Add(parameter);
        if (name is not null && _map.GetValueBinding(name) is { } binding)
            ProducerBinding(binding).Values.Add(callable);
    }

    private PossibleProducers ProducerBinding(BindingSymbol binding)
    {
        if (!_possibleProducers.TryGetValue(binding, out var producers))
            _possibleProducers[binding] = producers = new() { Unknown = !_map.IsConstBinding(binding) };
        return producers;
    }

    private bool PropagateCallableValues()
    {
        // These are possible source values, never a stable/native callee proof.
        // Predeclare actual destinations so forward const aliases converge by identity.
        foreach (var flow in _flows)
            if (_map.GetValueBinding(flow.Target) is { } binding) _ = ProducerBinding(binding);
        bool changed = false;
        foreach (var flow in _flows)
            if (_map.GetValueBinding(flow.Target) is { } binding)
                changed |= ProducerBinding(binding).Union(Producers(flow.Value));
        return changed;
    }

    private PossibleProducers Producers(Expr expression)
    {
        if (_sourceCallables.TryGetValue(expression, out var callable))
        {
            var result = new PossibleProducers(); result.Values.Add(callable); return result;
        }
        switch (expression)
        {
            case Expr.Variable variable when _map.GetValueBinding(variable.Name) is { } binding:
                return _possibleProducers.GetValueOrDefault(binding) ?? new() { Unknown = true };
            case Expr.Grouping grouping: return Producers(grouping.Expression);
            case Expr.TypeAssertion assertion: return Producers(assertion.Expression);
            case Expr.NonNullAssertion assertion: return Producers(assertion.Expression);
            case Expr.Satisfies satisfies: return Producers(satisfies.Expression);
            case Expr.Comma comma: return Producers(comma.Right);
            case Expr.Ternary conditional:
                var branches = new PossibleProducers();
                branches.Union(Producers(conditional.ThenBranch)); branches.Union(Producers(conditional.ElseBranch));
                return branches;
            default: return new() { Unknown = true };
        }
    }

    private bool SeedNumericOperations()
    {
        bool changed = false;
        foreach (var (result, operand) in _numericOperations)
        {
            bool possibleBigInt = result is Expr.Unary
                ? UnaryMayProduceBigInt(_map.Get(operand))
                    || UnaryMayProduceBigInt(_map.Get(ExpressionUnwrapper.Unwrap(operand)))
                : MayContainBigInt(_map.Get(operand));
            if (possibleBigInt || operand switch
                {
                    Expr.Variable variable => IsRawBinding(variable.Name, Evidence.Any)
                        || _map.IsAssertedAssignmentStorage(variable.Name),
                    Expr.Get member => PrimitiveFieldValueAnalysis.IsPrimitiveField(member, _map)
                        || PrimitiveFieldValueAnalysis.IsErasedGenericField(member, _map),
                    Expr.GetIndex member => PrimitiveFieldValueAnalysis.IsErasedGenericField(member, _map)
                        || member.Object is Expr.Variable array && IsRawBinding(array.Name, Evidence.Numeric),
                    _ => false
                })
                foreach (var expression in EquivalentExpressions(result))
                    changed |= _map.MarkRawNumericResult(expression);
        }
        if (!_hasNumericProducer && _numericOperations.Any(operation => _map.IsRawNumericResult(operation.Result)))
        {
            _hasNumericProducer = true;
            changed = true;
        }
        return changed;
    }

    private static bool MayContainBigInt(TypeInfo? type) => type switch
    {
        TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Inferred or TypeInfo.BigInt or TypeInfo.BigIntLiteral => true,
        TypeInfo.Union union => union.FlattenedTypes.Any(MayContainBigInt),
        TypeInfo.TypeParameter { Constraint: { } constraint } => MayContainBigInt(constraint),
        _ => false
    };

    // Object operands can produce BigInt through ToPrimitive, including behind
    // a numeric assertion. Only primitive kinds exclude that runtime result.
    private static bool UnaryMayProduceBigInt(TypeInfo? type) => type switch
    {
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER or TokenType.TYPE_STRING or TokenType.TYPE_BOOLEAN }
            or TypeInfo.NumberLiteral or TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.TemplateLiteralType
            or TypeInfo.BooleanLiteral or TypeInfo.Null or TypeInfo.Undefined or TypeInfo.Symbol
            or TypeInfo.UniqueSymbol or TypeInfo.Never => false,
        TypeInfo.Union union => union.FlattenedTypes.Any(UnaryMayProduceBigInt),
        TypeInfo.TypeParameter { Constraint: { } constraint } => UnaryMayProduceBigInt(constraint),
        _ => true
    };

    private bool PropagateArguments(Func<Expr, Evidence, bool> passesRawValue)
    {
        if (!HasNumericProducer) return false;
        bool changed = false;
        foreach (var site in _argumentSites)
        {
            if (!site.Arguments.Any(argument => passesRawValue(argument, Evidence.Numeric))) continue;
            var producers = site.Callee is Expr.Variable variable && _map.GetValueBinding(variable.Name) is { } binding
                && _map.IsConstBinding(binding) ? Producers(variable) : new() { Unknown = true };
            if (producers.Unknown || producers.Values.Count == 0 || site.Arguments.Any(argument => argument is Expr.Spread))
            {
                // No absence-of-escape assumption: every actual source parameter
                // remains possible. Foreign metadata signatures have no records here.
                bool callRaw = site.Arguments.Any(argument => passesRawValue(argument, Evidence.Call));
                foreach (var parameter in _sourceParameters)
                    changed |= MarkRawFlow(parameter.Name, parameter, true, callRaw, true);
                continue;
            }
            foreach (var producer in producers.Values)
                for (int index = 0; index < producer.Parameters.Count; index++)
                {
                    var parameter = producer.Parameters[index];
                    var values = parameter.IsRest ? site.Arguments.Skip(index)
                        : site.Arguments.Skip(index).Take(1);
                    var arguments = values.ToArray();
                    if (!arguments.Any(argument => passesRawValue(argument, Evidence.Numeric))) continue;
                    changed |= MarkRawFlow(parameter.Name, parameter, true,
                        arguments.Any(argument => passesRawValue(argument, Evidence.Call)), true);
                }
        }
        return changed;
    }
}
