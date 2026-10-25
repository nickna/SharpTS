using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private abstract record LoopValueOrigin;
    private sealed record LoopFixedValue(TypeInfo Type) : LoopValueOrigin;
    private sealed record LoopCopiedValue(BindingSymbol Source) : LoopValueOrigin;
    private sealed record LoopFilteredValue(LoopValueOrigin Value, TypeInfo Constraint) : LoopValueOrigin;
    private sealed record LoopUnaryValue(TokenType Operator, LoopValueOrigin Operand, TypeInfo OperandType) : LoopValueOrigin;
    private sealed record LoopBinaryValue(TokenType Operator, LoopValueOrigin Left, TypeInfo LeftType,
        LoopValueOrigin Right, TypeInfo RightType, Expr LeftSyntax, Expr RightSyntax) : LoopValueOrigin;
    private sealed record LoopCallValue(LoopValueOrigin Callee, TypeInfo CalleeType, LoopValueOrigin[] Arguments,
        TypeInfo[] ArgumentTypes, TypeInfo CheckedReturn, bool ExplicitTypes, bool Construct = false) : LoopValueOrigin;
    private sealed record LoopLogicalValue(TokenType Operator, LoopValueOrigin Left, TypeInfo LeftType,
        LoopValueOrigin Right, TypeInfo RightType) : LoopValueOrigin;
    private sealed record LoopChoiceValue(LoopValueOrigin Condition, TypeInfo ConditionType,
        LoopValueOrigin Then, TypeInfo ThenType, LoopValueOrigin Else, TypeInfo ElseType) : LoopValueOrigin;
    private sealed record LoopValueRead(LoopValueOrigin Value, TypeInfo Type);
    private sealed record LoopComputedValue(TypeInfo Type, LoopValueRead[] Reads, bool Unknown = false) : LoopValueOrigin;
    private sealed record LoopJoinedValues(LoopValueOrigin[] Values) : LoopValueOrigin;
    private sealed record LoopOriginSnapshot(LoopValueDomain Owner, Dictionary<BindingSymbol, LoopValueOrigin> Origins);
    private sealed record LoopWrittenOrigin(LoopValueDomain Owner, LoopValueOrigin Origin);
    private sealed record LoopMemberRead(LoopValueOrigin Receiver, TypeInfo Type, Token Name);

    private sealed class LoopExpressionReads(Expr? expression = null)
    {
        internal Expr? Expression { get; } = expression;
        internal List<LoopValueRead> Reads { get; } = [];
        internal bool Unknown { get; set; }
    }

    // These facts accompany the checked flow. No statements are replayed, and tentative
    // loop-domain iterations cannot change diagnostics, source identities or candidates.
    private sealed class LoopValueDomain(ValueFlowState incoming, Dictionary<BindingSymbol, bool>? assignments)
    {
        internal ValueFlowState Incoming { get; } = incoming;
        internal Dictionary<BindingSymbol, bool>? Assignments { get; } = assignments;
        internal Dictionary<BindingSymbol, LoopValueOrigin> Origins { get; } = incoming.Bindings.Keys
            .ToDictionary<BindingSymbol, BindingSymbol, LoopValueOrigin>(symbol => symbol,
                symbol => new LoopCopiedValue(symbol), ReferenceEqualityComparer.Instance);
        internal HashSet<BindingSymbol> VisibleBindings { get; } = new(ReferenceEqualityComparer.Instance);
        internal ConditionalWeakTable<Expr, LoopValueOrigin> Expressions { get; } = new();
        internal ConditionalWeakTable<ValueFlowState, LoopValueOrigin> ReturnValues { get; } = new();
        internal ConditionalWeakTable<Expr.ArrowFunction, LoopValueOrigin> ArrowReturns { get; } = new();
        internal Stack<LoopExpressionReads> Reads { get; } = new();
        internal LoopExpressionReads RiskReads { get; } = new();
        internal List<LoopMemberRead> MemberReads { get; } = [];
        internal ValueFlowState[] Backedges { get; set; } = [];
    }

    private readonly Stack<LoopValueDomain> _loopValueDomains = new();

    private LoopValueDomain BeginLoopValueDomain(ValueFlowState incoming)
    {
        _definiteAssignmentStack.TryPeek(out var assignments);
        var domain = new LoopValueDomain(incoming, assignments);
        for (TypeEnvironment? environment = _environment; environment is not null; environment = environment.Enclosing)
            foreach (string name in environment.Names)
                if (environment.GetLocalValueBinding(name) is { } symbol) domain.VisibleBindings.Add(symbol);
        _loopValueDomains.Push(domain);
        return domain;
    }

    private bool IsCurrentLoopValueDomain(LoopValueDomain domain)
    {
        _definiteAssignmentStack.TryPeek(out var assignments);
        int remaining = _immediateClassFlows.Count + 1;
        while (assignments is not null && remaining-- > 0)
        {
            if (ReferenceEquals(assignments, domain.Assignments)) return true;
            if (!_immediateClassFlows.TryGetValue(assignments, out assignments)) return false;
        }
        return domain.Assignments is null;
    }

    private LoopOriginSnapshot[] CaptureLoopValueOrigins() => _loopValueDomains.Where(IsCurrentLoopValueDomain).Select(domain =>
        new LoopOriginSnapshot(domain, new(domain.Origins, ReferenceEqualityComparer.Instance))).ToArray();

    private static void RestoreLoopValueOrigins(ValueFlowState state)
    {
        foreach (var snapshot in state.LoopOrigins)
        {
            snapshot.Owner.Origins.Clear();
            foreach (var (symbol, origin) in snapshot.Origins) snapshot.Owner.Origins[symbol] = origin;
        }
    }

    private static LoopValueOrigin JoinLoopOrigins(IEnumerable<LoopValueOrigin> origins)
    {
        var values = origins.Distinct<LoopValueOrigin>(ReferenceEqualityComparer.Instance).ToArray();
        return values.Length == 1 ? values[0] : new LoopJoinedValues(values);
    }

    private static void JoinLoopValueOrigins(ValueFlowState incoming, ValueFlowState[] exits)
    {
        foreach (var snapshot in incoming.LoopOrigins)
            foreach (var (symbol, origin) in snapshot.Origins)
                snapshot.Owner.Origins[symbol] = JoinLoopOrigins(exits.Select(exit =>
                    exit.LoopOrigins.FirstOrDefault(candidate => ReferenceEquals(candidate.Owner, snapshot.Owner))?
                        .Origins.GetValueOrDefault(symbol) ?? origin));
    }

    private sealed class LoopValueReadScope(TypeChecker checker, Expr expression,
        (LoopValueDomain Domain, LoopExpressionReads Reads)[] frames) : IDisposable
    {
        private TypeInfo? _result;
        private bool _finished;
        internal void Complete(TypeInfo result)
        {
            _result = result;
            Dispose();
        }
        public void Dispose()
        {
            if (_finished) return;
            _finished = true;
            foreach (var (domain, reads) in frames)
            {
                domain.Reads.Pop();
                if (_result is null)
                {
                    if (domain.Reads.TryPeek(out var parent)) parent.Unknown = true;
                    continue;
                }
                var origin = checker.CheckedLoopExpressionOrigin(domain, expression, _result, reads);
                domain.Expressions.AddOrUpdate(expression, origin);
                if (IsLoopRiskExpression(expression))
                {
                    if (expression is Expr.Assign assign && checker.GetDeclaredType(assign.Name.Lexeme) is { } declared)
                        domain.RiskReads.Reads.Add(new(origin, declared));
                    else if (origin is LoopUnaryValue or LoopBinaryValue) domain.RiskReads.Reads.Add(new(origin, _result));
                    else
                    {
                        domain.RiskReads.Reads.AddRange(reads.Reads);
                        domain.RiskReads.Unknown |= reads.Unknown;
                    }
                }
                if (expression is Expr.Get get && checker._typeMap.Get(get.Object) is { } receiverType &&
                    domain.Expressions.TryGetValue(get.Object, out var receiverOrigin))
                    domain.MemberReads.Add(new(receiverOrigin, receiverType, get.Name));
                if (domain.Reads.TryPeek(out var enclosing)) AddLoopOriginReads(origin, _result, enclosing);
                Token? written = expression switch
                {
                    Expr.Assign assign => assign.Name,
                    Expr.CompoundAssign assign => assign.Name,
                    Expr.LogicalAssign assign => assign.Name,
                    Expr.PrefixIncrement { Operand: Expr.Variable variable } => variable.Name,
                    Expr.PostfixIncrement { Operand: Expr.Variable variable } => variable.Name,
                    _ => null
                };
                if (written is not null && checker._environment.GetValueBinding(written.Lexeme) is { } symbol)
                    domain.Origins[symbol] = StoredLoopOrigin(origin, checker._environment.Get(written.Lexeme));
            }
        }
    }

    private LoopValueReadScope? BeginLoopValueReads(Expr expression)
    {
        if (_loopValueDomains.Count == 0) return null;
        var frames = _loopValueDomains.Where(IsCurrentLoopValueDomain).Select(domain =>
        {
            var reads = new LoopExpressionReads(expression);
            domain.Reads.Push(reads);
            return (domain, reads);
        }).ToArray();
        return frames.Length == 0 ? null : new(this, expression, frames);
    }

    private LoopValueOrigin CheckedLoopExpressionOrigin(LoopValueDomain domain, Expr expression,
        TypeInfo type, LoopExpressionReads reads)
    {
        if (expression is Expr.Variable variable && _environment.GetValueBinding(variable.Name.Lexeme) is { } symbol)
            return domain.Origins.GetValueOrDefault(symbol) ?? (domain.VisibleBindings.Contains(symbol)
                ? new LoopFixedValue(type) : new LoopComputedValue(type, [], Unknown: true));
        if (expression is Expr.Unary unary && domain.Expressions.TryGetValue(unary.Right, out var operand))
            return unary.Operator.Type == TokenType.TYPEOF ? new LoopFixedValue(TypeInfo.String.Shared)
                : new LoopUnaryValue(unary.Operator.Type, operand, _typeMap.Get(unary.Right) ?? TypeInfo.Any.Shared);
        if (expression is Expr.Call call)
        {
            if (UngroupGenericTupleExpression(call.Callee) is Expr.ArrowFunction arrow &&
                domain.ArrowReturns.TryGetValue(arrow, out var returned)) return returned;
            if (!call.Arguments.Any(argument => argument is Expr.Spread) &&
                domain.Expressions.TryGetValue(call.Callee, out var callee) &&
                call.Arguments.All(argument => domain.Expressions.TryGetValue(argument, out _)))
                return new LoopCallValue(callee, _typeMap.Get(call.Callee) ?? TypeInfo.Any.Shared,
                    call.Arguments.Select(argument => domain.Expressions.GetValue(argument, _ => throw new InvalidOperationException())).ToArray(),
                    call.Arguments.Select(argument => _typeMap.Get(argument) ?? TypeInfo.Any.Shared).ToArray(), type,
                    call.TypeArgs is { Count: > 0 });
        }
        if (expression is Expr.New constructed && !constructed.Arguments.Any(argument => argument is Expr.Spread) &&
            domain.Expressions.TryGetValue(constructed.Callee, out var constructor) &&
            constructed.Arguments.All(argument => domain.Expressions.TryGetValue(argument, out _)))
            return new LoopCallValue(constructor, _typeMap.Get(constructed.Callee) ?? TypeInfo.Any.Shared,
                constructed.Arguments.Select(argument => domain.Expressions.GetValue(argument, _ => throw new InvalidOperationException())).ToArray(),
                constructed.Arguments.Select(argument => _typeMap.Get(argument) ?? TypeInfo.Any.Shared).ToArray(), type,
                constructed.TypeArgs is { Count: > 0 }, Construct: true);
        if (expression is Expr.Binary binary &&
            domain.Expressions.TryGetValue(binary.Left, out var left) && domain.Expressions.TryGetValue(binary.Right, out var right))
            return new LoopBinaryValue(binary.Operator.Type, left, _typeMap.Get(binary.Left) ?? TypeInfo.Any.Shared,
                right, _typeMap.Get(binary.Right) ?? TypeInfo.Any.Shared, binary.Left, binary.Right);
        if (expression is Expr.Logical logical && domain.Expressions.TryGetValue(logical.Left, out var logicalLeft) &&
            domain.Expressions.TryGetValue(logical.Right, out var logicalRight))
            return new LoopLogicalValue(logical.Operator.Type, logicalLeft, _typeMap.Get(logical.Left) ?? TypeInfo.Any.Shared,
                logicalRight, _typeMap.Get(logical.Right) ?? TypeInfo.Any.Shared);
        if (expression is Expr.NullishCoalescing nullish && domain.Expressions.TryGetValue(nullish.Left, out var nullishLeft) &&
            domain.Expressions.TryGetValue(nullish.Right, out var nullishRight))
            return new LoopLogicalValue(TokenType.QUESTION_QUESTION, nullishLeft, _typeMap.Get(nullish.Left) ?? TypeInfo.Any.Shared,
                nullishRight, _typeMap.Get(nullish.Right) ?? TypeInfo.Any.Shared);
        if (expression is Expr.Ternary choice && domain.Expressions.TryGetValue(choice.Condition, out var condition) &&
            domain.Expressions.TryGetValue(choice.ThenBranch, out var then) && domain.Expressions.TryGetValue(choice.ElseBranch, out var otherwise))
            return new LoopChoiceValue(condition, _typeMap.Get(choice.Condition) ?? TypeInfo.Any.Shared,
                then, _typeMap.Get(choice.ThenBranch) ?? TypeInfo.Any.Shared, otherwise, _typeMap.Get(choice.ElseBranch) ?? TypeInfo.Any.Shared);
        Expr? copied = expression switch
        {
            Expr.Grouping grouping => grouping.Expression,
            Expr.Assign assignment => assignment.Value,
            Expr.Comma comma => comma.Right,
            _ => null
        };
        if (copied is not null && domain.Expressions.TryGetValue(copied, out var origin)) return origin;
        return new LoopComputedValue(type, reads.Reads.ToArray(), reads.Unknown);
    }

    private static void AddLoopOriginReads(LoopValueOrigin origin, TypeInfo observed, LoopExpressionReads reads)
    {
        switch (origin)
        {
            case LoopCopiedValue or LoopFilteredValue or LoopUnaryValue or LoopBinaryValue or LoopCallValue or LoopLogicalValue or LoopChoiceValue:
                reads.Reads.Add(new(origin, observed)); break;
            case LoopComputedValue computed:
                reads.Reads.AddRange(computed.Reads);
                reads.Unknown |= computed.Unknown;
                break;
            case LoopJoinedValues joined:
                foreach (var value in joined.Values) AddLoopOriginReads(value, observed, reads);
                break;
        }
    }

    private void RecordLoopValueDeclaration(Stmt statement)
    {
        if (_loopValueDomains.Count == 0) return;
        if (statement is Stmt.Return { Value: { } returned })
            foreach (var domain in _loopValueDomains.Where(IsCurrentLoopValueDomain))
                if (domain.Expressions.TryGetValue(returned, out var value))
                    AddLoopOriginReads(value, _currentFunctionReturnType is { } expected and not TypeInfo.Inferred
                        ? expected : _typeMap.Get(returned) ?? TypeInfo.Any.Shared, domain.RiskReads);
        (Token? name, Expr? initializer) = statement switch
        {
            Stmt.Var variable => (variable.Name, variable.Initializer),
            Stmt.Const constant => (constant.Name, constant.Initializer),
            _ => (null, null)
        };
        if (name is null || _environment.GetValueBinding(name.Lexeme) is not { } symbol) return;
        bool annotated = statement is Stmt.Var { TypeAnnotation: not null } or Stmt.Var { TypeAnnotationNode: not null } or
            Stmt.Const { TypeAnnotation: not null } or Stmt.Const { TypeAnnotationNode: not null };
        foreach (var domain in _loopValueDomains.Where(IsCurrentLoopValueDomain))
        {
            if (initializer is not null && domain.Expressions.TryGetValue(initializer, out var origin))
            {
                domain.Origins[symbol] = StoredLoopOrigin(origin, _environment.Get(name.Lexeme));
                if (annotated && _classFieldDeclaredBindingTypes.TryGetValue(symbol, out var declared))
                    AddLoopOriginReads(origin, declared.Type, domain.RiskReads);
            }
            else if (!domain.Origins.ContainsKey(symbol))
                domain.Origins[symbol] = new LoopComputedValue(_environment.Get(name.Lexeme) ?? TypeInfo.Any.Shared, [], Unknown: true);
        }
    }

    private static bool IsLoopRiskExpression(Expr expression) => expression switch
    {
        Expr.GetPrivate or Expr.GetIndex or Expr.Call or Expr.New or Expr.Assign or
            Expr.CompoundAssign or Expr.LogicalAssign or Expr.Set or Expr.SetIndex or Expr.SetPrivate or
            Expr.PrefixIncrement or Expr.PostfixIncrement => true,
        Expr.Unary unary => unary.Operator.Type != TokenType.TYPEOF && unary.Operator.Type != TokenType.BANG,
        Expr.Binary binary => binary.Operator.Type is not (TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL or
            TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL or TokenType.INSTANCEOF or TokenType.IN),
        _ => false
    };

    private static LoopValueOrigin StoredLoopOrigin(LoopValueOrigin origin, TypeInfo? type) => (origin, type) switch
    {
        (LoopFixedValue, { } stored) => new LoopFixedValue(stored),
        (LoopComputedValue computed, { } stored) => computed with { Type = stored },
        _ => origin
    };

    private void RecordLoopReturnValue(ValueFlowState state, Expr? expression)
    {
        foreach (var domain in _loopValueDomains.Where(IsCurrentLoopValueDomain))
            domain.ReturnValues.AddOrUpdate(state, expression is not null && domain.Expressions.TryGetValue(expression, out var origin)
                ? origin : new LoopFixedValue(TypeInfo.Undefined.Shared));
    }

    private void RecordLoopArrowReturnValues(Expr.ArrowFunction arrow, IReadOnlyList<ValueFlowState> exits)
    {
        if (!_immediateClassCallables.ContainsKey(arrow)) return;
        foreach (var domain in _loopValueDomains.Where(IsCurrentLoopValueDomain))
        {
            LoopValueOrigin origin = arrow.ExpressionBody is { } expression && domain.Expressions.TryGetValue(expression, out var value)
                ? value : JoinLoopOrigins(exits.Select(exit => domain.ReturnValues.TryGetValue(exit, out var returned)
                    ? returned : new LoopFixedValue(TypeInfo.Undefined.Shared)));
            domain.ArrowReturns.AddOrUpdate(arrow, origin);
        }
    }

    private void RecordLoopImmediateParameters(Expr.ArrowFunction arrow, TypeEnvironment environment, List<TypeInfo> types)
    {
        if (_loopValueDomains.Count == 0 || !_immediateClassCallables.ContainsKey(arrow)) return;
        foreach (var domain in _loopValueDomains.Where(IsCurrentLoopValueDomain))
        {
            var call = domain.Reads.Select(reads => reads.Expression).OfType<Expr.Call>()
                .FirstOrDefault(candidate => ReferenceEquals(UngroupGenericTupleExpression(candidate.Callee), arrow));
            for (int i = 0; i < arrow.Parameters.Count; i++)
            {
                if (environment.GetLocalValueBinding(arrow.Parameters[i].Name.Lexeme) is not { } symbol) continue;
                var reads = new LoopExpressionReads(arrow);
                if (call is not null && i < call.Arguments.Count && call.Arguments[i] is not Expr.Spread &&
                    domain.Expressions.TryGetValue(call.Arguments[i], out var argument))
                    AddLoopOriginReads(argument, _typeMap.Get(call.Arguments[i]) ?? types[i], reads);
                else reads.Unknown = true;
                domain.Origins[symbol] = new LoopComputedValue(types[i], reads.Reads.ToArray(), reads.Unknown);
            }
        }
    }

    private TypeInfo EvaluateLoopOrigin(LoopValueOrigin origin, TypeInfo fallback, IReadOnlyDictionary<BindingSymbol, TypeInfo> domains)
        => origin switch
        {
            LoopFixedValue fixedValue => fixedValue.Type,
            LoopCopiedValue copied => domains.GetValueOrDefault(copied.Source) ?? fallback,
            LoopFilteredValue filtered => IntersectLoopDomain(EvaluateLoopOrigin(filtered.Value, fallback, domains), filtered.Constraint),
            LoopUnaryValue unary => EvaluateLoopUnary(unary, fallback, domains),
            LoopBinaryValue binary => EvaluateLoopBinary(binary, fallback, domains),
            LoopCallValue call => EvaluateLoopCall(call, fallback, domains),
            LoopLogicalValue logical => EvaluateLoopLogical(logical, domains),
            LoopChoiceValue choice => EvaluateLoopChoice(choice, domains),
            LoopComputedValue computed when !computed.Unknown && computed.Reads.All(read =>
                LoopReadTypePreserved(read, domains)) => computed.Type,
            LoopJoinedValues joined => UnionLoopDomainTypes(joined.Values.Select(value => EvaluateLoopOrigin(value, fallback, domains))),
            _ => fallback
        };

    private ValueFlowState EvaluateLoopExit(LoopValueDomain domain, ValueFlowState state,
        IReadOnlyDictionary<BindingSymbol, TypeInfo> domains, bool projectParents = false)
    {
        var origins = state.LoopOrigins.FirstOrDefault(snapshot => ReferenceEquals(snapshot.Owner, domain));
        var bindings = new Dictionary<BindingSymbol, ValueFlowBinding>(state.Bindings, ReferenceEqualityComparer.Instance);
        foreach (var (symbol, binding) in bindings.ToArray())
            if (origins?.Origins.GetValueOrDefault(symbol) is { } origin &&
                _classFieldDeclaredBindingTypes.TryGetValue(symbol, out var declared))
                bindings[symbol] = binding with { Type = EvaluateLoopOrigin(origin, declared.Type, domains) };
        return state with { Bindings = bindings, LoopOrigins = projectParents
            ? ProjectLoopParentOrigins(domain, state, domains) : state.LoopOrigins };
    }

    private ValueFlowState[] LiteralTrueDoWhileExits(LoopValueDomain domain, ValueFlowLoopExit exit)
    {
        var initial = domain.Incoming.Bindings;
        Dictionary<BindingSymbol, TypeInfo> domains = initial.ToDictionary<KeyValuePair<BindingSymbol, ValueFlowBinding>, BindingSymbol, TypeInfo>(
            pair => pair.Key, pair => pair.Value.Type, ReferenceEqualityComparer.Instance);
        var backedges = exit.Continues.Concat(exit.HasNormalExit ? [exit.Body] : []).ToArray();
        domain.Backedges = backedges;
        // Copies form a finite graph. Each growth adds an already-checked type or a declared
        // fallback; a bounded worklist also handles chains and mutually dependent assignments.
        int remaining = initial.Count * 2 + 1;
        bool changed;
        do
        {
            changed = false;
            var evaluated = backedges.Select(state => EvaluateLoopExit(domain, state, domains)).ToArray();
            foreach (var (symbol, binding) in initial)
            {
                TypeInfo previous = domains[symbol];
                TypeInfo next = UnionLoopDomainTypes(new[] { previous }.Concat(evaluated.Select(state =>
                    state.Bindings.GetValueOrDefault(symbol)?.Type ?? binding.Type)));
                if (ReferenceEquals(previous, next) || TypeInfoEqualityComparer.Instance.Equals(previous, next)) continue;
                domains[symbol] = next;
                changed = true;
            }
        } while (changed && --remaining > 0);
        if (changed)
            foreach (var symbol in initial.Keys)
                if (_classFieldDeclaredBindingTypes.TryGetValue(symbol, out var declared)) domains[symbol] = declared.Type;

        IEnumerable<ValueFlowState> exits = exit.Breaks;
        bool membersPreserved = true;
        foreach (var read in domain.MemberReads) membersPreserved &= LoopMemberReadPreserved(read, domains);
        if (domain.RiskReads.Unknown || !domain.RiskReads.Reads.All(read => LoopReadTypePreserved(read, domains)) ||
            !membersPreserved)
            exits = exits.Concat(backedges);
        return exits.Select(state => EvaluateLoopExit(domain, state, domains, projectParents: true)).ToArray();
    }

    private bool LoopReadTypePreserved(LoopValueRead read, IReadOnlyDictionary<BindingSymbol, TypeInfo> domains)
    {
        var type = EvaluateLoopOrigin(read.Value, read.Type, domains);
        return ReferenceEquals(read.Type, type) || IsCompatible(read.Type, type);
    }

    private TypeInfo EvaluateLoopUnary(LoopUnaryValue unary, TypeInfo fallback, IReadOnlyDictionary<BindingSymbol, TypeInfo> domains)
    {
        var operand = EvaluateLoopOrigin(unary.Operand, unary.OperandType, domains);
        var types = operand is TypeInfo.Union union ? union.FlattenedTypes : [operand];
        try { return UnionLoopDomainTypes(types.Select(type => CheckUnaryOperandType(type, unary.Operator))); }
        catch (TypeCheckException) { return fallback; }
    }

    private TypeInfo EvaluateLoopBinary(LoopBinaryValue binary, TypeInfo fallback, IReadOnlyDictionary<BindingSymbol, TypeInfo> domains)
    {
        var left = EvaluateLoopOrigin(binary.Left, binary.LeftType, domains);
        var right = EvaluateLoopOrigin(binary.Right, binary.RightType, domains);
        var leftTypes = left is TypeInfo.Union leftUnion ? leftUnion.FlattenedTypes : [left];
        var rightTypes = right is TypeInfo.Union rightUnion ? rightUnion.FlattenedTypes : [right];
        List<TypeInfo> results = [];
        foreach (var first in leftTypes)
            foreach (var second in rightTypes)
            {
                if (DotNetTypeSynthesizer.TryResolveBinaryOperator(first, second, binary.Operator, out var clrResult))
                {
                    results.Add(clrResult);
                    continue;
                }
                var descriptor = SemanticOperatorResolver.Resolve(binary.Operator);
                if (descriptor is OperatorDescriptor.Plus or OperatorDescriptor.Equality or OperatorDescriptor.Comparison or
                    OperatorDescriptor.In or OperatorDescriptor.InstanceOf)
                {
                    try
                    {
                        results.Add(descriptor switch
                        {
                            OperatorDescriptor.Plus => CheckPlusOperator(first, second),
                            OperatorDescriptor.Equality => CheckEqualityOperator(first, second, binary.LeftSyntax, binary.RightSyntax, 0),
                            OperatorDescriptor.Comparison => CheckComparisonOperandTypes(first, second, binary.Operator.ToString()),
                            OperatorDescriptor.In => CheckInOperator(second, 0),
                            _ => CheckInstanceOfOperator(first, second, 0)
                        });
                    }
                    catch (TypeCheckException) { return fallback; }
                }
                else if (IsAnyPermissive(first) || IsAnyPermissive(second)) results.Add(TypeInfo.Any.Shared);
                else if (NumericBinaryResult(first, second) is { } numeric &&
                    (binary.Operator != TokenType.GREATER_GREATER_GREATER || numeric is not TypeInfo.BigInt)) results.Add(numeric);
                else return fallback;
            }
        return UnionLoopDomainTypes(results);
    }

    private TypeInfo EvaluateLoopCall(LoopCallValue call, TypeInfo fallback, IReadOnlyDictionary<BindingSymbol, TypeInfo> domains)
    {
        var callee = EvaluateLoopOrigin(call.Callee, call.CalleeType, domains);
        var arguments = call.Arguments.Select((argument, index) => EvaluateLoopOrigin(argument, call.ArgumentTypes[index], domains)).ToList();
        if (ReferenceEquals(callee, call.CalleeType) && arguments.Select((type, index) =>
            ReferenceEquals(type, call.ArgumentTypes[index])).All(unchanged => unchanged)) return call.CheckedReturn;
        if (call.ExplicitTypes) return call.CheckedReturn;
        if (call.Construct) return EvaluateLoopConstructor(callee, call, arguments, fallback);
        var functions = callee is TypeInfo.Union union ? union.FlattenedTypes : [callee];
        List<TypeInfo> results = [];
        foreach (var sourceFunction in functions)
        {
            var function = LoopCallableDomainSignature(sourceFunction);
            if (function is TypeInfo.Function ordinary) results.Add(ordinary.ReturnType);
            else if (function is TypeInfo.GenericFunction generic)
            {
                try
                {
                    var inferred = InferTypeArguments(generic, arguments);
                    var substitutions = generic.TypeParams.Select((parameter, index) => (parameter.Name, Type: inferred[index]))
                        .ToDictionary(pair => pair.Name, pair => pair.Type, StringComparer.Ordinal);
                    // Preserve proven source-equivalent signature metadata. This only
                    // transforms types; no invocation is selected or checked again.
                    results.Add(SubstitutePreservingSignatures(generic.ReturnType, substitutions));
                }
                catch (TypeCheckException) { return fallback; }
            }
            else return fallback;
        }
        return UnionLoopDomainTypes(results);
    }

    private TypeInfo LoopCallableDomainSignature(TypeInfo type)
    {
        var signatures = type switch
        {
            TypeInfo.Record record => record.CallSignatures,
            TypeInfo.Interface iface => CallableObjectSignatures(iface),
            TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface generic } => CallableObjectSignatures(generic),
            _ => null
        };
        if (signatures is not [{ } signature]) return type;
        if (type is TypeInfo.InstantiatedGeneric instance)
        {
            var substitutions = GenericInterfaceSubs((TypeInfo.GenericInterface)instance.GenericDefinition, instance.TypeArguments)
                .Where(pair => signature.TypeParams is null || signature.TypeParams.All(parameter => parameter.Name != pair.Key))
                .ToDictionary(StringComparer.Ordinal);
            signature = CopyEditorSignatureMetadata(signature, signature with
            {
                TypeParams = signature.TypeParams?.Select(parameter => parameter with
                {
                    Constraint = parameter.Constraint is null ? null : SubstitutePreservingSignatures(parameter.Constraint, substitutions),
                    Default = parameter.Default is null ? null : SubstitutePreservingSignatures(parameter.Default, substitutions)
                }).ToList(),
                ParamTypes = signature.ParamTypes.Select(parameter => SubstitutePreservingSignatures(parameter, substitutions)).ToList(),
                ReturnType = SubstitutePreservingSignatures(signature.ReturnType, substitutions)
            });
        }
        // This is the same declaration-owned call surface as the ordinary adapter,
        // projected only as types. Do not publish an invocation or choose a candidate.
        return CopyEditorSignatureMetadata(signature, signature.IsGeneric
            ? (TypeInfo)new TypeInfo.GenericFunction(signature.TypeParams!, signature.ParamTypes, signature.ReturnType,
                signature.RequiredParams, signature.HasRestParam, ParamNames: signature.ParamNames)
            : new TypeInfo.Function(signature.ParamTypes, signature.ReturnType, signature.RequiredParams,
                signature.HasRestParam, ParamNames: signature.ParamNames));
    }

    private TypeInfo EvaluateLoopConstructor(TypeInfo callee, LoopCallValue call, List<TypeInfo> arguments, TypeInfo fallback)
    {
        var constructors = callee is TypeInfo.Union union ? union.FlattenedTypes : [callee];
        List<TypeInfo> results = [];
        foreach (var constructor in constructors)
        {
            if (constructor is TypeInfo.Class)
                results.Add(ReferenceEquals(constructor, call.CalleeType) ? call.CheckedReturn : new TypeInfo.Instance(constructor));
            else if (constructor is TypeInfo.GenericClass generic)
            {
                var inferred = InferConstructorTypeArguments(generic, arguments);
                if (inferred is null) return fallback;
                results.Add(new TypeInfo.Instance(new TypeInfo.InstantiatedGeneric(generic, inferred)));
            }
            else if (constructor is TypeInfo.InstantiatedGeneric or TypeInfo.Interface or TypeInfo.GenericInterface)
                results.Add(call.CheckedReturn);
            else return fallback;
        }
        return UnionLoopDomainTypes(results);
    }

    private TypeInfo EvaluateLoopLogical(LoopLogicalValue logical, IReadOnlyDictionary<BindingSymbol, TypeInfo> domains)
    {
        var left = EvaluateLoopOrigin(logical.Left, logical.LeftType, domains);
        if (logical.Operator == TokenType.QUESTION_QUESTION)
        {
            var nonNullish = ExpandNonNullable(left);
            if (!LoopTypeMayBeNullish(left)) return nonNullish;
            var right = EvaluateLoopOrigin(logical.Right, logical.RightType, domains);
            return nonNullish is TypeInfo.Never ? right : UnionLoopDomainTypes([nonNullish, right]);
        }
        bool and = logical.Operator == TokenType.AND_AND;
        var selected = and ? NarrowLogicalFalsy(left) : NarrowLogicalTruthy(left);
        bool rightRuns = and ? !IsAlwaysFalsy(left) : !IsAlwaysTruthy(left);
        if (!rightRuns) return selected;
        var resultRight = EvaluateLoopOrigin(logical.Right, logical.RightType, domains);
        return selected is TypeInfo.Never ? resultRight : UnionLoopDomainTypes([selected, resultRight]);
    }

    private TypeInfo EvaluateLoopChoice(LoopChoiceValue choice, IReadOnlyDictionary<BindingSymbol, TypeInfo> domains)
    {
        var condition = EvaluateLoopOrigin(choice.Condition, choice.ConditionType, domains);
        if (IsAlwaysTruthy(condition)) return EvaluateLoopOrigin(choice.Then, choice.ThenType, domains);
        if (IsAlwaysFalsy(condition)) return EvaluateLoopOrigin(choice.Else, choice.ElseType, domains);
        return UnionLoopDomainTypes([EvaluateLoopOrigin(choice.Then, choice.ThenType, domains),
            EvaluateLoopOrigin(choice.Else, choice.ElseType, domains)]);
    }

    private static bool LoopTypeMayBeNullish(TypeInfo type) => type switch
    {
        TypeInfo.Null or TypeInfo.Undefined or TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Inferred => true,
        TypeInfo.TypeParameter parameter => parameter.Constraint is null || LoopTypeMayBeNullish(parameter.Constraint),
        TypeInfo.Union union => union.FlattenedTypes.Any(LoopTypeMayBeNullish),
        TypeInfo.Intersection intersection => intersection.FlattenedTypes.All(LoopTypeMayBeNullish),
        TypeInfo.IndexedAccess or TypeInfo.ConditionalType or TypeInfo.RecursiveTypeAlias => true,
        _ => false
    };

    private bool LoopMemberReadPreserved(LoopMemberRead read, IReadOnlyDictionary<BindingSymbol, TypeInfo> domains)
    {
        var type = EvaluateLoopOrigin(read.Receiver, read.Type, domains);
        if (ReferenceEquals(read.Type, type) || IsCompatible(read.Type, type)) return true;
        var members = type is TypeInfo.Union union ? union.FlattenedTypes : [type];
        return members.All(member => GetDiscriminantPropertyType(member, read.Name.Lexeme) is not null);
    }

    private LoopWrittenOrigin[] CaptureLoopWrittenOrigins(BindingSymbol symbol) => _loopValueDomains
        .Where(domain => domain.Origins.ContainsKey(symbol))
        .Select(domain => new LoopWrittenOrigin(domain, domain.Origins[symbol])).ToArray();

    private static LoopWrittenOrigin[] MergeLoopWrittenOrigins(LoopWrittenOrigin[]? first, LoopWrittenOrigin[]? second)
    {
        if (first is null || first.Length == 0) return second ?? [];
        if (second is null || second.Length == 0) return first;
        List<LoopWrittenOrigin> merged = [.. first];
        foreach (var value in second)
        {
            int existing = merged.FindIndex(candidate => ReferenceEquals(candidate.Owner, value.Owner));
            if (existing < 0) merged.Add(value);
            else merged[existing] = value with { Origin = JoinLoopOrigins([merged[existing].Origin, value.Origin]) };
        }
        return merged.ToArray();
    }

    private static LoopOriginSnapshot[] ExceptionLoopOrigins(ValueFlowTryWrites writes) => writes.Incoming.LoopOrigins.Select(snapshot =>
    {
        var origins = new Dictionary<BindingSymbol, LoopValueOrigin>(snapshot.Origins, ReferenceEqualityComparer.Instance);
        foreach (var (symbol, binding) in writes.Writes)
            if (origins.TryGetValue(symbol, out var incoming) && binding.LoopWrites?
                .FirstOrDefault(write => ReferenceEquals(write.Owner, snapshot.Owner)) is { } changed)
                origins[symbol] = JoinLoopOrigins([incoming, changed.Origin]);
        return new LoopOriginSnapshot(snapshot.Owner, origins);
    }).ToArray();

    private static TypeInfo UnionLoopDomainTypes(IEnumerable<TypeInfo> types)
    {
        List<TypeInfo> distinct = [];
        foreach (var type in types)
            if (!distinct.Any(previous => ReferenceEquals(previous, type) || TypeInfoEqualityComparer.Instance.Equals(previous, type)))
                distinct.Add(type);
        return distinct.Count == 1 ? distinct[0] : CollapseOrCreateUnion(distinct);
    }
}
