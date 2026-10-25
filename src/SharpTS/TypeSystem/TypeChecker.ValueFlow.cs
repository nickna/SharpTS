namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record ValueFlowBinding(string Name, BindingSymbol Symbol, TypeInfo Type);
    private sealed record ValueFlowEnvironment(TypeEnvironment Environment, Dictionary<string, ValueFlowBinding> Locals);
    private sealed record ValueFlowState(List<ValueFlowEnvironment> Environments,
        Dictionary<BindingSymbol, ValueFlowBinding> Bindings, ValueFlowReturns? ReturnOwner,
        ValueFlowState[] Returns);
    private sealed class ValueFlowReturns(int depth)
    {
        internal int Depth { get; } = depth;
        internal ValueFlowState Entry { get; set; } = null!;
        internal List<ValueFlowState> Exits { get; } = [];
    }
    private readonly Stack<ValueFlowReturns> _valueFlowReturns = new();
    private sealed class ValueFlowTryWrites(int depth, ValueFlowState incoming)
    {
        internal int Depth { get; } = depth;
        internal ValueFlowState Incoming { get; } = incoming;
        internal Dictionary<BindingSymbol, ValueFlowBinding> Writes { get; } = new(ReferenceEqualityComparer.Instance);
    }
    private readonly Stack<ValueFlowTryWrites> _valueFlowTryWrites = new();
    private sealed class ValueFlowTarget(string? label, bool loop, bool @switch, SharpTS.Parsing.Stmt? loopBody)
    {
        internal string? Label { get; } = label;
        internal bool IsLoop { get; } = loop;
        internal bool IsSwitch { get; } = @switch;
        internal SharpTS.Parsing.Stmt? LoopBody { get; } = loopBody;
        internal List<ValueFlowState> Breaks { get; } = [];
        internal List<ValueFlowState> Continues { get; } = [];
    }
    private readonly Stack<ValueFlowTarget> _valueFlowTargets = new();
    private sealed record ValueFlowLoopExit(ValueFlowState Body, ValueFlowState[] Breaks,
        ValueFlowState[] Continues, bool HasNormalExit);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SharpTS.Parsing.Expr.ArrowFunction, ValueFlowState>
        _arrowExitValueFlows = new();

    // Capture checked binding flow, rather than replaying expressions. Declaration identities
    // distinguish source shadows from the transient environments used for narrowing.
    private ValueFlowState CaptureValueFlow()
    {
        List<ValueFlowEnvironment> environments = [];
        Dictionary<BindingSymbol, ValueFlowBinding> bindings = new(ReferenceEqualityComparer.Instance);
        HashSet<BindingSymbol> tracked = new(ReferenceEqualityComparer.Instance);
        for (TypeEnvironment? environment = _environment; environment is not null; environment = environment.Enclosing)
        {
            Dictionary<string, ValueFlowBinding> locals = new(StringComparer.Ordinal);
            foreach (string name in environment.Names)
            {
                if (environment.GetLocalValueBinding(name) is not { } symbol ||
                    !_classFieldDeclaredBindingTypes.TryGetValue(symbol, out var declared) || environment.Get(name) is not { } type)
                    continue;
                var binding = new ValueFlowBinding(name, symbol, type);
                locals[name] = binding;
                bindings.TryAdd(symbol, binding);
                if (declared.Type is TypeInfo.Union or TypeInfo.TypeParameter || !ReferenceEquals(type, declared.Type))
                    tracked.Add(symbol);
            }
            environments.Add(new(environment, locals));
        }
        foreach (var environment in environments)
            foreach (string name in environment.Locals.Keys.ToArray())
                if (!tracked.Contains(environment.Locals[name].Symbol)) environment.Locals.Remove(name);
        foreach (var symbol in bindings.Keys.ToArray())
            if (!tracked.Contains(symbol)) bindings.Remove(symbol);
        _valueFlowReturns.TryPeek(out var returns);
        return new(environments, bindings, returns, returns?.Exits.ToArray() ?? []);
    }

    private void RestoreValueFlow(ValueFlowState state)
    {
        if (_valueFlowReturns.TryPeek(out var returns) && ReferenceEquals(returns, state.ReturnOwner))
        {
            returns.Exits.Clear();
            returns.Exits.AddRange(state.Returns);
        }
        foreach (var (environment, locals) in state.Environments)
        {
            Dictionary<string, TypeInfo>? values = null;
            foreach (string name in environment.Names)
            {
                if (!locals.ContainsKey(name) && environment.GetLocalValueBinding(name) is { } symbol &&
                    state.Bindings.ContainsKey(symbol))
                {
                    values ??= environment.SnapshotLocalValueTypes()!;
                    values.Remove(name);
                }
            }
            if (values is not null) environment.RestoreLocalValueTypes(values);
            foreach (var (name, binding) in locals)
                if (ReferenceEquals(environment.GetLocalValueBinding(name), binding.Symbol))
                    environment.Define(name, binding.Type);
        }
    }

    private void ApplyValueFlow(ValueFlowState state)
    {
        foreach (var binding in state.Bindings.Values)
            if (ReferenceEquals(_environment.GetValueBinding(binding.Name), binding.Symbol))
                _environment.Define(binding.Name, binding.Type);
    }

    private void JoinValueFlow(ValueFlowState incoming, params ValueFlowState[] exits)
    {
        RestoreValueFlow(incoming);
        if (exits.Length == 0) return;
        if (_valueFlowReturns.TryPeek(out var returns) && ReferenceEquals(returns, incoming.ReturnOwner))
        {
            HashSet<ValueFlowState> returned = new(returns.Exits, ReferenceEqualityComparer.Instance);
            foreach (var exit in exits)
                foreach (var returnedExit in exit.Returns)
                    if (returned.Add(returnedExit)) returns.Exits.Add(returnedExit);
        }
        foreach (var binding in incoming.Bindings.Values)
        {
            if (!ReferenceEquals(_environment.GetValueBinding(binding.Name), binding.Symbol)) continue;
            TypeInfo first = exits[0].Bindings.GetValueOrDefault(binding.Symbol)?.Type ?? binding.Type;
            List<TypeInfo>? distinct = null;
            foreach (var exit in exits.Skip(1))
            {
                TypeInfo type = exit.Bindings.GetValueOrDefault(binding.Symbol)?.Type ?? binding.Type;
                if (ReferenceEquals(first, type)) continue;
                distinct ??= [first];
                // Flow joins have few exits. Avoid structurally hashing a recursive generic
                // function/object graph just to deduplicate the same canonical binding type.
                if (!distinct.Any(existing => ReferenceEquals(existing, type) ||
                    TypeInfoEqualityComparer.Instance.Equals(existing, type))) distinct.Add(type);
            }
            _environment.Define(binding.Name, distinct is null ? first : CollapseOrCreateUnion(distinct));
        }
    }

    private void PublishInheritedValueFlow(TypeEnvironment child, TypeEnvironment parent)
    {
        foreach (string name in child.Names)
            if (child.GetLocalValueBinding(name) is { } symbol &&
                _classFieldDeclaredBindingTypes.TryGetValue(symbol, out _) &&
                ReferenceEquals(parent.GetValueBinding(name), symbol))
                parent.Define(name, child.Get(name)!);
    }

    private bool? CheckedConditionTruthiness(SharpTS.Parsing.Expr condition)
    {
        var type = _typeMap.Get(condition);
        return type is null ? null : IsAlwaysFalsy(type) ? false : IsAlwaysTruthy(type) ? true : null;
    }

    private sealed class DeferredValueFlowScope : IDisposable
    {
        private readonly TypeChecker _checker;
        private readonly ValueFlowState _incoming;
        private readonly bool _inStaticBlock;

        internal DeferredValueFlowScope(TypeChecker checker, bool preserveCapturedNarrowings = false)
        {
            _checker = checker;
            _incoming = checker.CaptureValueFlow();
            _inStaticBlock = checker._inStaticBlock;
            checker._inStaticBlock = false;
            if (!preserveCapturedNarrowings)
                foreach (var binding in _incoming.Bindings.Values)
                    if (!checker._typeMap.IsConstBinding(binding.Symbol) &&
                        checker._classFieldDeclaredBindingTypes.TryGetValue(binding.Symbol, out var declared) &&
                        ReferenceEquals(checker._environment.GetValueBinding(binding.Name), binding.Symbol))
                        checker._environment.Define(binding.Name, declared.Type);
        }

        public void Dispose()
        {
            _checker.RestoreValueFlow(_incoming);
            _checker._inStaticBlock = _inStaticBlock;
        }
    }

    private void RecordReturnedValueFlow()
    {
        if (_valueFlowReturns.TryPeek(out var returns) && returns.Depth == _definiteAssignmentStack.Count)
            returns.Exits.Add(CaptureValueFlow());
    }

    private ValueFlowReturns BeginArrowValueFlow()
    {
        var returns = new ValueFlowReturns(_definiteAssignmentStack.Count);
        _valueFlowReturns.Push(returns);
        returns.Entry = CaptureValueFlow();
        return returns;
    }

    private void CompleteArrowValueFlow(SharpTS.Parsing.Expr.ArrowFunction arrow, ValueFlowReturns returns)
    {
        var exits = returns.Exits.ToList();
        if (arrow.ExpressionBody is not null || arrow.BlockBody is null || !arrow.BlockBody.Any(AlwaysTerminates))
            exits.Add(CaptureValueFlow());
        JoinValueFlow(returns.Entry, exits.ToArray());
        _arrowExitValueFlows.AddOrUpdate(arrow, CaptureValueFlow());
        _valueFlowReturns.Pop();
    }

    private TypeInfo ApplyImmediateArrowValueFlow(SharpTS.Parsing.Expr.Call call, TypeInfo result)
    {
        SharpTS.Parsing.Expr callee = call.Callee;
        while (callee is SharpTS.Parsing.Expr.Grouping grouping) callee = grouping.Expression;
        if (callee is not SharpTS.Parsing.Expr.ArrowFunction { IsAsync: false, IsGenerator: false } arrow)
            return result;
        if (call.Arguments.Count == 0)
        {
            if (_arrowExitValueFlows.TryGetValue(arrow, out var immediateExit)) ApplyValueFlow(immediateExit);
            return result;
        }
        // Arguments execute before the body. Recheck this eager body against their checked
        // exit state; a summary taken while first resolving the callee would precede them.
        TypeInfo checkedCallee = CheckClassOrderCallTarget(call.Callee);
        if (_arrowExitValueFlows.TryGetValue(arrow, out var exit)) ApplyValueFlow(exit);
        return checkedCallee is TypeInfo.Function function ? function.ReturnType : result;
    }

    private (ValueFlowTryWrites Owner, Dictionary<BindingSymbol, ValueFlowBinding> Writes)? CaptureTryValueWrites() =>
        _valueFlowTryWrites.TryPeek(out var writes)
            ? (writes, new(writes.Writes, ReferenceEqualityComparer.Instance)) : null;

    private void RestoreTryValueWrites((ValueFlowTryWrites Owner, Dictionary<BindingSymbol, ValueFlowBinding> Writes)? snapshot)
    {
        if (snapshot is not { } saved || !_valueFlowTryWrites.TryPeek(out var current) || !ReferenceEquals(current, saved.Owner))
            return;
        current.Writes.Clear();
        foreach (var (symbol, binding) in saved.Writes) current.Writes[symbol] = binding;
    }

    private void RecordTryValueMutation(SharpTS.Parsing.Expr expression)
    {
        if (!_valueFlowTryWrites.TryPeek(out var writes) || writes.Depth != _definiteAssignmentStack.Count ||
            _classFieldBindingFlowBoundary is not null || _deferredInitializerFlowBoundary is not null)
            return;
        SharpTS.Parsing.Token? target = expression switch
        {
            SharpTS.Parsing.Expr.Assign assign => assign.Name,
            SharpTS.Parsing.Expr.CompoundAssign assign => assign.Name,
            SharpTS.Parsing.Expr.LogicalAssign assign => assign.Name,
            SharpTS.Parsing.Expr.PrefixIncrement { Operand: SharpTS.Parsing.Expr.Variable variable } => variable.Name,
            SharpTS.Parsing.Expr.PostfixIncrement { Operand: SharpTS.Parsing.Expr.Variable variable } => variable.Name,
            _ => null
        };
        if (target is not null && _environment.GetValueBinding(target.Lexeme) is { } symbol &&
            writes.Incoming.Bindings.ContainsKey(symbol) && _environment.Get(target.Lexeme) is { } type)
            AddTryValueWrite(writes, new(target.Lexeme, symbol, type));
        else if (expression is SharpTS.Parsing.Expr.Call or SharpTS.Parsing.Expr.ClassExpr)
            foreach (var binding in CaptureValueFlow().Bindings.Values)
                if (writes.Incoming.Bindings.TryGetValue(binding.Symbol, out var incoming) &&
                    !ReferenceEquals(incoming.Type, binding.Type) &&
                    !TypeInfoEqualityComparer.Instance.Equals(incoming.Type, binding.Type))
                    AddTryValueWrite(writes, binding);
    }

    private static void AddTryValueWrite(ValueFlowTryWrites writes, ValueFlowBinding binding)
    {
        if (writes.Writes.TryGetValue(binding.Symbol, out var previous))
            binding = binding with { Type = CreateUnion(previous.Type, binding.Type) };
        writes.Writes[binding.Symbol] = binding;
    }

    private static ValueFlowState ExceptionValueFlow(ValueFlowTryWrites writes)
    {
        var bindings = new Dictionary<BindingSymbol, ValueFlowBinding>(writes.Incoming.Bindings, ReferenceEqualityComparer.Instance);
        foreach (var (symbol, binding) in writes.Writes)
            bindings[symbol] = binding with { Type = CreateUnion(bindings[symbol].Type, binding.Type) };
        return writes.Incoming with { Bindings = bindings };
    }

    private (ValueFlowTarget Target, int Breaks, int Continues)[] CaptureValueFlowJumps() =>
        _valueFlowTargets.Select(target => (target, target.Breaks.Count, target.Continues.Count)).ToArray();

    private static void RestoreValueFlowJumps((ValueFlowTarget Target, int Breaks, int Continues)[] snapshot)
    {
        foreach (var (target, breaks, continues) in snapshot)
        {
            if (target.Breaks.Count > breaks) target.Breaks.RemoveRange(breaks, target.Breaks.Count - breaks);
            if (target.Continues.Count > continues) target.Continues.RemoveRange(continues, target.Continues.Count - continues);
        }
    }

    private void RecordValueFlowJump(SharpTS.Parsing.Token? label, bool isContinue)
    {
        var target = _valueFlowTargets.FirstOrDefault(candidate => label is not null
            ? candidate.Label == label.Lexeme
            : isContinue ? candidate.IsLoop : candidate.IsLoop || candidate.IsSwitch);
        if (target is not null)
            (isContinue ? target.Continues : target.Breaks).Add(CaptureValueFlow());
    }

    private bool ApplyLoopContinuingValueFlow(ValueFlowState incoming, ValueFlowLoopExit exit)
    {
        List<ValueFlowState> continuing = [.. exit.Continues];
        if (exit.HasNormalExit) continuing.Add(exit.Body);
        JoinValueFlow(incoming, continuing.ToArray());
        return continuing.Count > 0;
    }

    private void JoinLoopValueFlow(ValueFlowState incoming, ValueFlowLoopExit? exit, bool canSkip, bool alwaysTrue = false)
    {
        List<ValueFlowState> exits = [];
        if (canSkip) exits.Add(incoming);
        if (exit is not null)
        {
            exits.AddRange(exit.Breaks);
            if (!alwaysTrue || exit.Breaks.Length > 0)
            {
                exits.AddRange(exit.Continues);
                if (exit.HasNormalExit) exits.Add(exit.Body);
            }
        }
        JoinValueFlow(incoming, exits.ToArray());
    }

    private TypeInfo CheckValueFlowExpression(SharpTS.Parsing.Expr expression, TypeInfo? contextualType, out ValueFlowState exit)
    {
        using var branch = new EnvironmentScope(this, new TypeEnvironment(_environment));
        var result = CheckExprWithContext(expression, contextualType);
        exit = CaptureValueFlow();
        return result;
    }
}
