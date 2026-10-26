using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record LoopGuardFact(string Name, BindingSymbol Symbol, TypeInfo Type);
    private sealed record LoopConditionalGuards(LoopGuardFact[] Then, LoopGuardFact[] Else);

    private LoopGuardFact[]? LoopNullishGuard(Expr expression, bool nullish)
    {
        if (_loopValueDomains.Count == 0 || UngroupGenericTupleExpression(expression) is not Expr.Variable variable ||
            _environment.GetValueBinding(variable.Name.Lexeme) is not { } symbol ||
            GetDeclaredType(variable.Name.Lexeme) is not { } type) return null;
        if (!nullish) return [new(variable.Name.Lexeme, symbol, ExpandNonNullable(type))];
        var values = type is TypeInfo.Union union ? union.FlattenedTypes : [type];
        var selected = values.Where(value => value is TypeInfo.Null or TypeInfo.Undefined).ToArray();
        return [new(variable.Name.Lexeme, symbol, selected.Length > 0 ? UnionLoopDomainTypes(selected)
            : type is TypeInfo.Any or TypeInfo.Unknown or TypeInfo.TypeParameter
                ? new TypeInfo.Union([TypeInfo.Null.Shared, TypeInfo.Undefined.Shared]) : TypeInfo.Never.Shared)];
    }

    private LoopConditionalGuards? AnalyzeLoopExitGuards(Expr condition)
    {
        if (_loopValueDomains.Count == 0) return null;
        var fullEnvironment = new TypeEnvironment(_environment);
        HashSet<string> visible = new(StringComparer.Ordinal);
        for (TypeEnvironment? environment = _environment; environment is not null; environment = environment.Enclosing)
            foreach (string name in environment.Names)
                if (visible.Add(name) && environment.GetLocalValueBinding(name) is { } symbol &&
                    _classFieldDeclaredBindingTypes.TryGetValue(symbol, out var declared))
                    fullEnvironment.Define(name, declared.Type);
        using var scope = new EnvironmentScope(this, fullEnvironment);
        return Collect(condition);

        LoopConditionalGuards Collect(Expr expression)
        {
            expression = FinalConditionOperand(expression);
            if (expression is Expr.Grouping grouping) return Collect(grouping.Expression);
            if (expression is Expr.Unary { Operator.Type: TokenType.BANG } negation)
            {
                var inner = Collect(negation.Right);
                return new(inner.Else, inner.Then);
            }
            if (expression is Expr.Logical logical)
            {
                var left = Collect(logical.Left);
                var right = Collect(logical.Right);
                if (logical.Operator.Type == TokenType.AND_AND && _checkedAndGuards.TryGetValue(logical, out var checkedGuards))
                {
                    bool Survives(LoopGuardFact fact) => !checkedGuards.Writes.Any(write => ReferenceEquals(write.Symbol, fact.Symbol));
                    left = new(left.Then.Where(Survives).ToArray(), left.Else.Where(Survives).ToArray());
                }
                if (logical.Operator.Type == TokenType.OR_OR && _checkedOrGuards.TryGetValue(logical, out var checkedOrGuards))
                {
                    bool Survives(LoopGuardFact fact) => !checkedOrGuards.Writes.Any(write => ReferenceEquals(write.Symbol, fact.Symbol));
                    left = new(left.Then.Where(Survives).ToArray(), left.Else.Where(Survives).ToArray());
                }
                return logical.Operator.Type == TokenType.AND_AND
                    ? new(Combine(left.Then, right.Then, all: true), Combine(left.Else, right.Else, all: false))
                    : logical.Operator.Type == TokenType.OR_OR
                        ? new(Combine(left.Then, right.Then, all: false), Combine(left.Else, right.Else, all: true))
                        : new([], []);
            }
            if (expression is Expr.Variable variable && fullEnvironment.Get(variable.Name.Lexeme) is { } type &&
                fullEnvironment.GetValueBinding(variable.Name.Lexeme) is { } variableSymbol)
            {
                var (truthy, falsy) = ComputeTruthinessNarrowing(type);
                return new(truthy is not null ? [new(variable.Name.Lexeme, variableSymbol, truthy)] : [],
                    falsy is not null ? [new(variable.Name.Lexeme, variableSymbol, falsy)] : []);
            }
            // AnalyzeTypeGuard's root forms are pure except predicate calls. Never replay
            // an expression, getter, call or candidate check to obtain a loop constraint.
            if (expression is Expr.Call) return new([], []);
            var guard = AnalyzeTypeGuard(expression);
            if (guard.VarName is not { } name || fullEnvironment.GetValueBinding(name) is not { } symbol)
                return new([], []);
            return new(guard.NarrowedType is { } narrowed ? [new(name, symbol, narrowed)] : [],
                guard.ExcludedType is { } excluded ? [new(name, symbol, excluded)] : []);
        }

        LoopGuardFact[] Combine(LoopGuardFact[] first, LoopGuardFact[] second, bool all)
        {
            List<LoopGuardFact> combined = [];
            foreach (var fact in first)
            {
                var other = second.FirstOrDefault(candidate => ReferenceEquals(candidate.Symbol, fact.Symbol));
                if (other is not null)
                    combined.Add(fact with { Type = all ? IntersectLoopDomain(fact.Type, other.Type)
                        : UnionLoopDomainTypes([fact.Type, other.Type]) });
                else if (all) combined.Add(fact);
            }
            if (all)
                combined.AddRange(second.Where(fact => !combined.Any(candidate => ReferenceEquals(candidate.Symbol, fact.Symbol))));
            return combined.ToArray();
        }
    }

    private void ApplyLoopExitGuards(LoopGuardFact[]? guards)
    {
        if (guards is null) return;
        foreach (var fact in guards)
            foreach (var domain in _loopValueDomains.Where(IsCurrentLoopValueDomain))
                if (ReferenceEquals(_environment.GetValueBinding(fact.Name), fact.Symbol) &&
                    domain.Origins.TryGetValue(fact.Symbol, out var origin))
                {
                    domain.Origins[fact.Symbol] = new LoopFilteredValue(origin, fact.Type);
                }
    }

    private static ValueFlowState ApplyLoopExitGuards(ValueFlowState state, LoopGuardFact[]? guards)
    {
        if (guards is null || guards.Length == 0 || state.LoopOrigins.Length == 0) return state;
        var snapshots = state.LoopOrigins.Select(snapshot =>
        {
            var origins = new Dictionary<BindingSymbol, LoopValueOrigin>(snapshot.Origins, ReferenceEqualityComparer.Instance);
            foreach (var fact in guards)
                if (origins.TryGetValue(fact.Symbol, out var origin))
                {
                    origins[fact.Symbol] = new LoopFilteredValue(origin, fact.Type);
                }
            return new LoopOriginSnapshot(snapshot.Owner, origins);
        }).ToArray();
        return state with { LoopOrigins = snapshots };
    }

    private TypeInfo IntersectLoopDomain(TypeInfo type, TypeInfo constraint)
    {
        if (ReferenceEquals(type, constraint)) return type;
        if (type is TypeInfo.Any or TypeInfo.Unknown or TypeInfo.TypeParameter) return constraint;
        if (type is TypeInfo.Never || constraint is TypeInfo.Never) return TypeInfo.Never.Shared;
        var types = type is TypeInfo.Union union ? union.FlattenedTypes : [type];
        var constraints = constraint is TypeInfo.Union constrained ? constrained.FlattenedTypes : [constraint];
        List<TypeInfo> matching = [];
        foreach (var value in types)
            foreach (var candidate in constraints)
                if (ReferenceEquals(value, candidate) || IsCompatible(candidate, value)) matching.Add(value);
                else if (IsCompatible(value, candidate)) matching.Add(candidate);
        return matching.Count == 0 ? TypeInfo.Never.Shared : UnionLoopDomainTypes(matching);
    }
}
