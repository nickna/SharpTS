using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Narrowing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record CheckedConditionWrite(string Name, BindingSymbol Symbol, TypeInfo Type);
    private sealed record CheckedAndGuards(
        (NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)[] Left,
        CheckedConditionWrite[] Writes, ValueFlowState Right);
    private readonly ConditionalWeakTable<Expr.Logical, CheckedAndGuards> _checkedAndGuards = new();
    private sealed record CheckedOrGuards(
        (NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)[] Left,
        (NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)[] TrueLeft,
        CheckedConditionWrite[] Writes, ValueFlowState Incoming, ValueFlowState Right);
    private readonly ConditionalWeakTable<Expr.Logical, CheckedOrGuards> _checkedOrGuards = new();
    private readonly Stack<ConditionWriteCapture> _conditionWriteCaptures = new();

    private sealed class ConditionWriteCapture(TypeChecker checker,
        Dictionary<BindingSymbol, bool>? assignments) : IDisposable
    {
        internal Dictionary<BindingSymbol, bool>? Assignments { get; } = assignments;
        internal Dictionary<BindingSymbol, string> Writes { get; } = new(ReferenceEqualityComparer.Instance);
        public void Dispose() => checker._conditionWriteCaptures.Pop();
    }

    private ConditionWriteCapture BeginConditionWriteCapture()
    {
        _definiteAssignmentStack.TryPeek(out var assignments);
        var capture = new ConditionWriteCapture(this, assignments);
        _conditionWriteCaptures.Push(capture);
        return capture;
    }

    private void RecordConditionValueMutation(Expr expression)
    {
        if (_conditionWriteCaptures.Count == 0 || _classFieldBindingFlowBoundary is not null ||
            _deferredInitializerFlowBoundary is not null) return;
        Token? target = expression switch
        {
            Expr.Assign assign => assign.Name,
            Expr.CompoundAssign assign => assign.Name,
            Expr.LogicalAssign assign => assign.Name,
            Expr.PrefixIncrement { Operand: Expr.Variable variable } => variable.Name,
            Expr.PostfixIncrement { Operand: Expr.Variable variable } => variable.Name,
            _ => null
        };
        if (target is null || _environment.GetValueBinding(target.Lexeme) is not { } symbol) return;
        foreach (var capture in _conditionWriteCaptures)
        {
            _definiteAssignmentStack.TryPeek(out var assignments);
            int remaining = _immediateClassFlows.Count + 1;
            bool active = ReferenceEquals(assignments, capture.Assignments);
            while (!active && assignments is not null && remaining-- > 0)
            {
                if (!_immediateClassFlows.TryGetValue(assignments, out assignments)) break;
                active = ReferenceEquals(assignments, capture.Assignments);
            }
            if (active) capture.Writes[symbol] = target.Lexeme;
        }
    }

    private void CompleteCheckedAndGuards(Expr.Logical logical, ConditionWriteCapture capture,
        List<(NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> left, ValueFlowState right)
    {
        var writes = capture.Writes.Select(pair => right.Bindings.TryGetValue(pair.Key, out var binding)
            ? new CheckedConditionWrite(pair.Value, pair.Key, binding.Type) : null).OfType<CheckedConditionWrite>().ToArray();
        _checkedAndGuards.AddOrUpdate(logical, new(left.ToArray(), writes, right));
    }

    private void CompleteCheckedOrGuards(Expr.Logical logical, ConditionWriteCapture capture,
        List<(NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> left,
        List<(NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> trueLeft,
        ValueFlowState incoming, ValueFlowState right)
    {
        var writes = (_typeMap.Get(logical.Left) is { } leftType && IsAlwaysTruthy(leftType)
            ? Enumerable.Empty<KeyValuePair<BindingSymbol, string>>() : capture.Writes)
            .Select(pair => right.Bindings.TryGetValue(pair.Key, out var binding)
            ? new CheckedConditionWrite(pair.Value, pair.Key, binding.Type) : null).OfType<CheckedConditionWrite>().ToArray();
        _checkedOrGuards.AddOrUpdate(logical, new(left.ToArray(), trueLeft.ToArray(), writes, incoming, right));
    }

    private void ApplyCheckedConditionLoopOrigins(Expr condition, bool truthy)
    {
        condition = FinalConditionOperand(condition);
        if (condition is not Expr.Logical logical) return;
        CheckedConditionWrite[] writes;
        ValueFlowState right;
        if (truthy && logical.Operator.Type == TokenType.AND_AND &&
            _checkedAndGuards.TryGetValue(logical, out var andGuards))
            (writes, right) = (andGuards.Writes, andGuards.Right);
        else if (!truthy && logical.Operator.Type == TokenType.OR_OR &&
            _checkedOrGuards.TryGetValue(logical, out var orGuards))
            (writes, right) = (orGuards.Writes, orGuards.Right);
        else return;
        ApplyCheckedConditionLoopOrigins(logical.Left, truthy);
        ApplyCheckedConditionLoopOrigins(logical.Right, truthy);
        foreach (var snapshot in right.LoopOrigins)
            foreach (var write in writes)
                if (ReferenceEquals(_environment.GetValueBinding(write.Name), write.Symbol) &&
                    snapshot.Origins.TryGetValue(write.Symbol, out var origin))
                    snapshot.Owner.Origins[write.Symbol] = origin;
    }

    private List<(NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)>
        AnalyzeCheckedAndGuards(Expr.Logical logical, CheckedAndGuards checkedGuards)
    {
        var writes = checkedGuards.Writes.Where(write =>
            ReferenceEquals(_environment.GetValueBinding(write.Name), write.Symbol)).ToArray();
        bool Invalidated(NarrowingPath path) => writes.Any(write => path.RootVariable == write.Name);
        var result = checkedGuards.Left.Where(guard => !Invalidated(guard.Path)).ToList();
        var rightEnvironment = new TypeEnvironment(_environment);
        var context = NarrowingContext.Empty;
        foreach (var (path, narrowed, _) in result)
            if (path is NarrowingPath.Variable variable) rightEnvironment.Define(variable.Name, narrowed);
            else context = context.WithNarrowing(path, narrowed);
        foreach (var write in writes) rightEnvironment.Define(write.Name, write.Type);
        List<(NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> right;
        using (new EnvironmentScope(this, rightEnvironment))
        {
            if (!context.IsEmpty) PushNarrowingContext(context);
            try { right = AnalyzeCompoundTypeGuards(logical.Right); }
            finally { if (!context.IsEmpty) PopNarrowingContext(); }
        }
        result.AddRange(right);
        foreach (var write in writes)
        {
            var path = new NarrowingPath.Variable(write.Name);
            if (right.Any(guard => guard.Path.Equals(path))) continue;
            var left = checkedGuards.Left.FirstOrDefault(guard => guard.Path.Equals(path));
            var skipped = left.Path is not null ? left.ExcludedType : _environment.Get(write.Name) ?? write.Type;
            result.Add((path, write.Type, UnionLoopDomainTypes([skipped, write.Type])));
        }
        return result;
    }

    private List<(NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)>
        AnalyzeCheckedOrGuards(Expr.Logical logical, CheckedOrGuards checkedGuards, bool exclusionsOnly = false)
    {
        var writes = checkedGuards.Writes.Where(write =>
            ReferenceEquals(_environment.GetValueBinding(write.Name), write.Symbol)).ToArray();
        bool leftIsFalse = _typeMap.Get(logical.Left) is { } leftType && IsAlwaysFalsy(leftType);
        bool Invalidated(NarrowingPath path) => writes.Any(write => path.RootVariable == write.Name);
        var left = checkedGuards.Left.Where(guard => !Invalidated(guard.Path)).ToList();
        var rightEnvironment = new TypeEnvironment(_environment);
        var context = NarrowingContext.Empty;
        foreach (var (path, _, excluded) in left)
            if (path is NarrowingPath.Variable variable) rightEnvironment.Define(variable.Name, excluded);
            else context = context.WithNarrowing(path, excluded);
        foreach (var write in writes) rightEnvironment.Define(write.Name, write.Type);
        List<(NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> rightFalse;
        List<(NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> rightTrue = [];
        using (new EnvironmentScope(this, rightEnvironment))
        {
            if (!context.IsEmpty) PushNarrowingContext(context);
            try
            {
                rightFalse = CollectDisjunctGuards(logical.Right);
                if (!exclusionsOnly) rightTrue = AnalyzeCompoundTypeGuards(logical.Right);
            }
            finally { if (!context.IsEmpty) PopNarrowingContext(); }
        }

        var result = exclusionsOnly ? left : new List<(NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)>();
        if (exclusionsOnly) result.AddRange(rightFalse);
        else
            foreach (var guard in checkedGuards.TrueLeft.Where(guard => !Invalidated(guard.Path)))
            {
                var right = rightTrue.FirstOrDefault(candidate => candidate.Path.Equals(guard.Path));
                if (right.Path is not null)
                    result.Add((guard.Path, UnionOfNarrowedTypes(guard.NarrowedType, right.NarrowedType), right.ExcludedType));
            }
        foreach (var write in writes)
        {
            var path = new NarrowingPath.Variable(write.Name);
            if (exclusionsOnly && rightFalse.Any(guard => guard.Path.Equals(path))) continue;
            var falseGuard = rightFalse.FirstOrDefault(guard => guard.Path.Equals(path));
            var falseType = falseGuard.Path is not null ? falseGuard.ExcludedType : write.Type;
            var skippedGuard = checkedGuards.TrueLeft.FirstOrDefault(guard => guard.Path.Equals(path));
            var skipped = skippedGuard.Path is not null ? skippedGuard.NarrowedType :
                checkedGuards.Incoming.Bindings.TryGetValue(write.Symbol, out var binding) ? binding.Type : write.Type;
            var trueGuard = rightTrue.FirstOrDefault(guard => guard.Path.Equals(path));
            var written = trueGuard.Path is not null ? trueGuard.NarrowedType : write.Type;
            result.Add((path, UnionOfNarrowedTypes(leftIsFalse ? TypeInfo.Never.Shared : skipped, written), falseType));
        }
        return result;
    }
}
