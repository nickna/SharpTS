using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private bool MergeLoopBodyState(LoopBodyGraph graph, Dictionary<object, TypeInfo> target, Dictionary<object, TypeInfo> source)
    {
        bool changed = false;
        foreach (var slot in target.Keys.Concat(source.Keys).Distinct(ReferenceEqualityComparer.Instance).ToArray())
        {
            var previous = target.GetValueOrDefault(slot) ?? DefaultLoopBodyValue(graph, target, slot);
            var added = source.GetValueOrDefault(slot) ?? DefaultLoopBodyValue(graph, source, slot);
            if (ReferenceEquals(previous, added)) continue;
            var joined = UnionLoopBodyValues([previous, added]);
            if (ReferenceEquals(previous, joined) || !ReferenceEquals(previous, UnprovedLoopBodyType) &&
                !ReferenceEquals(joined, UnprovedLoopBodyType) && TypeInfoEqualityComparer.Instance.Equals(previous, joined)) continue;
            target[slot] = joined;
            changed = true;
        }
        return changed;
    }

    private Dictionary<BindingSymbol, TypeInfo>? ReachableLoopBodyInputs(Stmt.DoWhile loop)
    {
        try
        {
            var graph = BuildLoopBodyGraph(loop);
            var flow = EvaluateLoopBodyGraph(graph, new(ReferenceEqualityComparer.Instance));
            if (flow.Entry.Any(pair => pair.Key is BindingSymbol && HasUnprovedLoopBodyType(pair.Value)))
                throw new UnprovedLoopBodyFlow();
            Dictionary<BindingSymbol, TypeInfo> result = new(ReferenceEqualityComparer.Instance);
            foreach (var (slot, type) in flow.Entry)
                if (slot is BindingSymbol symbol) result[symbol] = type;
            return result;
        }
        catch (UnprovedLoopBodyFlow) { return null; }
        // Pure type queries can fail while expanding an alias or comparing a
        // contract. Only the authoritative body check owns source diagnostics.
        catch (TypeCheckException) { return null; }
    }

    private sealed record LoopBodyGraphExit(Dictionary<object, TypeInfo> Entry, Dictionary<object, TypeInfo>? Exit,
        Dictionary<object, TypeInfo>? Thrown, HashSet<object> Writes);

    private LoopBodyGraphExit EvaluateLoopBodyGraph(LoopBodyGraph graph, Dictionary<object, TypeInfo> initial)
    {
            HashSet<object> writes = new(ReferenceEqualityComparer.Instance);
            var incoming = new Dictionary<object, TypeInfo>?[graph.Nodes.Count];
            incoming[graph.Entry] = CopyLoopBodyValues(initial);
            Queue<int> pending = new([graph.Entry]);
            HashSet<int> queued = [graph.Entry];
            int remaining = graph.Nodes.Count * 64 + 1;
            while (pending.TryDequeue(out int index))
            {
                if (--remaining == 0) throw new UnprovedLoopBodyFlow();
                queued.Remove(index);
                var node = graph.Nodes[index];
                if (node.Kind == LoopBodyNodeKind.End) continue;
                var state = CopyLoopBodyValues(incoming[index]!);
                var evaluation = new LoopBodyEvaluation(graph, node.Scope, state);
                switch (node.Kind)
                {
                    case LoopBodyNodeKind.Scope:
                        foreach (var local in node.Scope.Locals.Values) state.Remove(local);
                        Send(node.Next, state);
                        break;
                    case LoopBodyNodeKind.Condition:
                    {
                        var branches = node.Expression is null ? (Then: state, Else: (Dictionary<object, TypeInfo>?)null)
                            : EvaluateLoopBodyCondition(node.Expression, evaluation, node.LoopTest);
                        Send(node.Next, branches.Then);
                        Send(node.Otherwise, branches.Else);
                        break;
                    }
                    case LoopBodyNodeKind.Case:
                    {
                        var tested = EvaluateLoopBodyValue(node.Expression!, evaluation);
                        var subject = state.GetValueOrDefault(node.Slot!) ?? UnprovedLoopBodyType;
                        if (ReferenceEquals(tested, UnprovedLoopBodyType) || ReferenceEquals(subject, UnprovedLoopBodyType))
                            throw new UnprovedLoopBodyFlow();
                        var equal = LoopLiteralEquality(subject, tested);
                        string? dependency = node.Subject switch
                        {
                            Expr.Variable variable => variable.Name.Lexeme,
                            Expr.Get { Object: Expr.Variable variable } => variable.Name.Lexeme,
                            Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable variable } => variable.Name.Lexeme,
                            _ => null
                        };
                        bool stable = dependency is null || LoopBodySlot(evaluation, dependency) is not { } root || !evaluation.Writes.Contains(root);
                        if (equal != false) Send(node.Next, stable ? FilterLoopBodyEquality(node.Subject!, tested, true, evaluation) : state);
                        if (equal != true) Send(node.Otherwise, stable ? FilterLoopBodyEquality(node.Subject!, tested, false, evaluation) : state);
                        break;
                    }
                    case LoopBodyNodeKind.Each:
                    {
                        var iterable = state.GetValueOrDefault(node.Slot!) ?? UnprovedLoopBodyType;
                        if (HasUnprovedLoopBodyType(iterable)) throw new UnprovedLoopBodyFlow();
                        TypeInfo element;
                        if (node.AssignedType is { } keyType) element = keyType;
                        else if (!TryGetSpreadElementType(iterable, out element)) throw new UnprovedLoopBodyFlow();
                        var entered = new LoopBodyEvaluation(graph, node.Scope, CopyLoopBodyValues(state));
                        if (node.AssignedSlot is BindingSymbol symbol && !graph.Initial.ContainsKey(symbol))
                        {
                            graph.Initial[symbol] = graph.Environment.Get(symbol.Name) ?? UnprovedLoopBodyType;
                            if (_classFieldDeclaredBindingTypes.TryGetValue(symbol, out var contract)) graph.Contracts[symbol] = contract.Type;
                        }
                        StoreLoopBodyValue(entered, node.AssignedSlot!, element);
                        Send(node.Next, entered.Values);
                        Send(node.Otherwise, state);
                        writes.UnionWith(entered.Writes);
                        break;
                    }
                    case LoopBodyNodeKind.Declaration:
                    {
                        var value = node.AssignedType ?? (node.Expression is null ? TypeInfo.Undefined.Shared : EvaluateLoopBodyValue(node.Expression, evaluation));
                        if (node.Slot is BindingSymbol symbol && !graph.Initial.ContainsKey(symbol))
                        {
                            graph.Initial[symbol] = graph.Environment.Get(symbol.Name) ?? UnprovedLoopBodyType;
                            if (_classFieldDeclaredBindingTypes.TryGetValue(symbol, out var contract)) graph.Contracts[symbol] = contract.Type;
                        }
                        StoreLoopBodyValue(evaluation, node.Slot!, value, node.IsConst);
                        Send(node.Next, state);
                        break;
                    }
                    case LoopBodyNodeKind.Subject:
                        state[node.Slot!] = EvaluateLoopBodyValue(node.Expression!, evaluation);
                        Send(node.Next, state);
                        break;
                    case LoopBodyNodeKind.Return:
                        state[node.Slot!] = node.Expression is null ? TypeInfo.Undefined.Shared : EvaluateLoopBodyValue(node.Expression, evaluation);
                        Send(node.Next, state);
                        break;
                    case LoopBodyNodeKind.Expression:
                        EvaluateLoopBodyValue(node.Expression!, evaluation);
                        Send(node.Next, state);
                        break;
                    default: Send(node.Next, state); break;
                }
                if (node.Exception >= 0)
                    foreach (var exceptional in evaluation.Throws) Send(node.Exception, exceptional);
                writes.UnionWith(evaluation.Writes);
            }
            return new(incoming[graph.Entry]!, incoming[graph.Exit], incoming[graph.ThrowExit], writes);

            void Send(int destination, Dictionary<object, TypeInfo>? state)
            {
                if (destination < 0 || state is null) return;
                if (incoming[destination] is null) incoming[destination] = CopyLoopBodyValues(state);
                else if (!MergeLoopBodyState(graph, incoming[destination]!, state)) return;
                if (queued.Add(destination)) pending.Enqueue(destination);
            }
    }

    private (Dictionary<object, TypeInfo>? Then, Dictionary<object, TypeInfo>? Else) EvaluateLoopBodyCondition(
        Expr condition, LoopBodyEvaluation evaluation, bool loopTest = false)
    {
        if (condition is Expr.Grouping grouping) return EvaluateLoopBodyCondition(grouping.Expression, evaluation, loopTest);
        if (condition is Expr.Comma comma)
        {
            EvaluateLoopBodyValue(comma.Left, evaluation);
            return EvaluateLoopBodyCondition(comma.Right, evaluation, loopTest);
        }
        if (condition is Expr.Unary { Operator.Type: TokenType.BANG } negated)
        {
            var inner = EvaluateLoopBodyCondition(negated.Right, evaluation);
            return (inner.Else, inner.Then);
        }
        if (condition is Expr.Logical logical)
        {
            var left = EvaluateLoopBodyCondition(logical.Left, evaluation);
            bool and = logical.Operator.Type == TokenType.AND_AND;
            var rightInput = and ? left.Then : left.Else;
            (Dictionary<object, TypeInfo>? Then, Dictionary<object, TypeInfo>? Else) right = (null, null);
            if (rightInput is not null)
            {
                var branch = new LoopBodyEvaluation(evaluation.Graph, evaluation.Scope, rightInput);
                right = EvaluateLoopBodyCondition(logical.Right, branch);
                evaluation.Throws.AddRange(branch.Throws);
                evaluation.Writes.UnionWith(branch.Writes);
            }
            return and ? (right.Then, Join(left.Else, right.Else)) : (Join(left.Then, right.Then), right.Else);
        }
        var type = EvaluateLoopBodyValue(condition, evaluation);
        if (ReferenceEquals(type, UnprovedLoopBodyType)) throw new UnprovedLoopBodyFlow();
        bool? truth = loopTest ? LiteralLoopConditionTruthiness(condition)
            : IsAlwaysTruthy(type) ? true : IsAlwaysFalsy(type) ? false : null;
        Dictionary<object, TypeInfo>? whenTrue = truth == false ? null : CopyLoopBodyValues(evaluation.Values);
        Dictionary<object, TypeInfo>? whenFalse = truth == true ? null : CopyLoopBodyValues(evaluation.Values);
        if (condition is Expr.Binary binary && binary.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL or
            TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL)
        {
            bool equality = binary.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL;
            if (binary.Right is Expr.Literal literal)
            {
                var literalType = GetLiteralType(literal.Value);
                if (whenTrue is not null) whenTrue = FilterLoopBodyEquality(binary.Left, literalType, equality,
                    new(evaluation.Graph, evaluation.Scope, whenTrue));
                if (whenFalse is not null) whenFalse = FilterLoopBodyEquality(binary.Left, literalType, !equality,
                    new(evaluation.Graph, evaluation.Scope, whenFalse));
            }
        }
        else if (condition is Expr.Variable variable && LoopBodySlot(evaluation, variable.Name.Lexeme) is { } slot)
        {
            var (narrowed, excluded) = ComputeTruthinessNarrowing(type);
            if (whenTrue is not null && narrowed is not null) whenTrue[slot] = narrowed;
            if (whenFalse is not null && excluded is not null) whenFalse[slot] = excluded;
        }
        return (whenTrue, whenFalse);

        Dictionary<object, TypeInfo>? Join(Dictionary<object, TypeInfo>? first, Dictionary<object, TypeInfo>? second)
        {
            if (first is null) return second;
            if (second is null) return first;
            MergeLoopBodyState(evaluation.Graph, first, second);
            return first;
        }
    }

    private Dictionary<object, TypeInfo>? FilterLoopBodyEquality(Expr reference, TypeInfo compared, bool equal,
        LoopBodyEvaluation evaluation)
    {
        reference = FinalConditionOperand(reference);
        string? name = reference switch
        {
            Expr.Variable variable => variable.Name.Lexeme,
            Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable variable } => variable.Name.Lexeme,
            Expr.Get { Object: Expr.Variable variable } => variable.Name.Lexeme,
            _ => null
        };
        if (name is null || LoopBodySlot(evaluation, name) is not { } slot) return evaluation.Values;
        var type = LoopBodyValue(evaluation, name);
        if (ReferenceEquals(type, UnprovedLoopBodyType)) throw new UnprovedLoopBodyFlow();
        if (reference is Expr.Unary { Operator.Type: TokenType.TYPEOF } && compared is TypeInfo.StringLiteral expected)
        {
            var (narrowed, excluded) = AnalyzeTypeofGuard(type, expected.Value, negated: false);
            var projected = equal ? narrowed : excluded;
            if (projected is null or TypeInfo.Never) return null;
            evaluation.Values[slot] = projected;
            return evaluation.Values;
        }
        List<TypeInfo> selected = [];
        foreach (var member in LoopBodyAtoms(type))
        {
            TypeInfo? value = member;
            if (reference is Expr.Get get) value = GetMemberType(member, get.Name.Lexeme);
            bool? match = value is null ? null : LoopLiteralEquality(value, compared);
            if (match is null || match == equal) selected.Add(member);
        }
        if (selected.Count == 0) return null;
        evaluation.Values[slot] = UnionLoopBodyValues(selected);
        return evaluation.Values;
    }

    private TypeInfo EvaluateLoopBodyLogical(Expr.Logical logical, LoopBodyEvaluation evaluation)
    {
        var leftType = EvaluateLoopBodyValue(logical.Left, evaluation);
        if (ReferenceEquals(leftType, UnprovedLoopBodyType)) throw new UnprovedLoopBodyFlow();
        // The LHS has already supplied its effects. Its final operand is a pure guard query here.
        var left = FilterLoopBodyTruth(logical.Left, leftType, evaluation);
        bool and = logical.Operator.Type == TokenType.AND_AND;
        var skipped = and ? left.Else : left.Then;
        var runs = and ? left.Then : left.Else;
        var selected = and ? NarrowLogicalFalsy(leftType) : NarrowLogicalTruthy(leftType);
        if (runs is null) return selected;
        var branch = new LoopBodyEvaluation(evaluation.Graph, evaluation.Scope, runs);
        var right = EvaluateLoopBodyValue(logical.Right, branch);
        evaluation.Throws.AddRange(branch.Throws);
        evaluation.Writes.UnionWith(branch.Writes);
        if (skipped is not null) MergeLoopBodyState(evaluation.Graph, runs, skipped);
        evaluation.Values.Clear();
        foreach (var pair in runs) evaluation.Values[pair.Key] = pair.Value;
        return selected is TypeInfo.Never ? right : UnionLoopBodyValues([selected, right]);
    }
    private (Dictionary<object, TypeInfo>? Then, Dictionary<object, TypeInfo>? Else) FilterLoopBodyTruth(
        Expr expression, TypeInfo type, LoopBodyEvaluation evaluation)
    {
        Dictionary<object, TypeInfo>? then = IsAlwaysFalsy(type) ? null : CopyLoopBodyValues(evaluation.Values);
        Dictionary<object, TypeInfo>? otherwise = IsAlwaysTruthy(type) ? null : CopyLoopBodyValues(evaluation.Values);
        expression = FinalConditionOperand(expression);
        if (expression is Expr.Binary binary && binary.Right is Expr.Literal literal &&
            binary.Operator.Type is TokenType.EQUAL_EQUAL_EQUAL or TokenType.EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL or TokenType.BANG_EQUAL)
        {
            bool equal = binary.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL;
            if (then is not null) then = FilterLoopBodyEquality(binary.Left, GetLiteralType(literal.Value), equal,
                new(evaluation.Graph, evaluation.Scope, then));
            if (otherwise is not null) otherwise = FilterLoopBodyEquality(binary.Left, GetLiteralType(literal.Value), !equal,
                new(evaluation.Graph, evaluation.Scope, otherwise));
        }
        return (then, otherwise);
    }
}
