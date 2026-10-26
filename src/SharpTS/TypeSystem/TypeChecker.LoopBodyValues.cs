using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // An internal failed type query must never become a published any/declared-domain fact.
    private static readonly TypeInfo UnprovedLoopBodyType = new TypeInfo.Unknown();
    private sealed class LoopBodyEvaluation(LoopBodyGraph graph, LoopBodyScope scope,
        Dictionary<object, TypeInfo> values)
    {
        internal LoopBodyGraph Graph { get; } = graph;
        internal LoopBodyScope Scope { get; } = scope;
        internal Dictionary<object, TypeInfo> Values { get; } = values;
        internal List<Dictionary<object, TypeInfo>> Throws { get; } = [];
        internal HashSet<object> Writes { get; } = new(ReferenceEqualityComparer.Instance);
    }
    private static Dictionary<object, TypeInfo> CopyLoopBodyValues(Dictionary<object, TypeInfo> values) =>
        new(values, ReferenceEqualityComparer.Instance);

    private object? LoopBodySlot(LoopBodyEvaluation evaluation, string name)
    {
        var slot = evaluation.Scope.Resolve(name);
        if (slot is BindingSymbol symbol && !evaluation.Graph.Initial.ContainsKey(slot))
        {
            evaluation.Graph.Initial[slot] = evaluation.Graph.Environment.Get(name) ?? UnprovedLoopBodyType;
            if (_classFieldDeclaredBindingTypes.TryGetValue(symbol, out var contract))
                evaluation.Graph.Contracts[slot] = contract.Type;
        }
        return slot;
    }
    private TypeInfo LoopBodyValue(LoopBodyEvaluation evaluation, string name)
    {
        var slot = LoopBodySlot(evaluation, name);
        return slot is null ? UnprovedLoopBodyType : evaluation.Values.GetValueOrDefault(slot) ??
            evaluation.Graph.Initial.GetValueOrDefault(slot) ?? UnprovedLoopBodyType;
    }
    private void StoreLoopBodyValue(LoopBodyEvaluation evaluation, object slot, TypeInfo value, bool constant = false)
    {
        if (slot is BindingSymbol)
        {
            if (HasUnprovedLoopBodyType(value)) throw new UnprovedLoopBodyFlow();
            if (evaluation.Graph.Contracts.TryGetValue(slot, out var contract))
            {
                if (!IsCompatible(contract, value)) throw new UnprovedLoopBodyFlow();
                if (value is TypeInfo.Any && contract is not TypeInfo.Any) throw new UnprovedLoopBodyFlow();
                value = NarrowToDeclaredSlot(contract, value) ?? value;
            }
        }
        else if (!constant && value is TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigIntLiteral)
            value = WidenLiteralType(value);
        if (evaluation.Graph.MemberSlots.TryGetValue(slot, out var members))
            foreach (var member in members.Values) evaluation.Values.Remove(member);
        evaluation.Values[slot] = value;
        evaluation.Writes.Add(slot);
    }
    private TypeInfo UnionLoopBodyValues(IEnumerable<TypeInfo> types)
    {
        var values = types.ToArray();
        return values.Any(type => ReferenceEquals(type, UnprovedLoopBodyType))
            ? UnprovedLoopBodyType : UnionLoopDomainTypes(values);
    }

    private TypeInfo EvaluateLoopBodyValue(Expr expression, LoopBodyEvaluation evaluation)
    {
        TypeInfo Evaluate(Expr child) => EvaluateLoopBodyValue(child, evaluation);
        switch (expression)
        {
            case Expr.Literal literal: return GetLiteralType(literal.Value);
            case Expr.Variable variable: return LoopBodyValue(evaluation, variable.Name.Lexeme);
            case Expr.ArrowFunction: return UnprovedLoopBodyType; // Creating a deferred body has no eager writes.
            case Expr.This: return evaluation.Graph.ThisType ?? UnprovedLoopBodyType;
            case Expr.Grouping grouping: return Evaluate(grouping.Expression);
            case Expr.Comma comma: Evaluate(comma.Left); return Evaluate(comma.Right);
            case Expr.NonNullAssertion assertion:
            {
                var value = Evaluate(assertion.Expression);
                return ReferenceEquals(value, UnprovedLoopBodyType) ? value : ExpandNonNullable(value);
            }
            case Expr.Satisfies satisfies: return Evaluate(satisfies.Expression);
            case Expr.TypeAssertion assertion when assertion.TargetType == "const": return Evaluate(assertion.Expression);
            case Expr.TypeAssertion assertion:
            {
                Evaluate(assertion.Expression);
                return evaluation.Graph.Environment.GetTypeBinding(assertion.TargetType) ?? (assertion.TargetType switch
                {
                    "string" => TypeInfo.String.Shared,
                    "number" => TypeInfo.Primitive.Number,
                    "boolean" => TypeInfo.Primitive.Boolean,
                    "any" => TypeInfo.Any.Shared,
                    "unknown" => TypeInfo.Unknown.Shared,
                    _ => UnprovedLoopBodyType
                });
            }
            case Expr.AssertedAssignment assertion: return Evaluate(assertion.LoweredAssignment);
            case Expr.Assign assignment:
            {
                var value = Evaluate(assignment.Value);
                if (LoopBodySlot(evaluation, assignment.Name.Lexeme) is not { } slot) throw new UnprovedLoopBodyFlow();
                StoreLoopBodyValue(evaluation, slot, value);
                return value;
            }
            case Expr.CompoundAssign compound:
            {
                var current = LoopBodyValue(evaluation, compound.Name.Lexeme);
                Evaluate(compound.Value);
                if (LoopBodySlot(evaluation, compound.Name.Lexeme) is { } slot)
                    StoreLoopBodyValue(evaluation, slot, current);
                return current;
            }
            case Expr.LogicalAssign assignment:
                return EvaluateLoopBodyLogicalAssignment(assignment, evaluation);
            case Expr.Set assignment:
            {
                var receiver = Evaluate(assignment.Object);
                var value = Evaluate(assignment.Value);
                StoreLoopBodyMember(evaluation, assignment.Object, assignment.Name.Lexeme, receiver, value);
                evaluation.Throws.Add(CopyLoopBodyValues(evaluation.Values));
                return value;
            }
            case Expr.CompoundSet compound:
            {
                var receiver = Evaluate(compound.Object);
                Evaluate(compound.Value);
                var values = LoopBodyAtoms(receiver).Select(type => LoopBodyReadableMember(type, compound.Name.Lexeme)).ToArray();
                var result = values.Any(type => type is null) ? UnprovedLoopBodyType : UnionLoopBodyValues(values.OfType<TypeInfo>());
                StoreLoopBodyMember(evaluation, compound.Object, compound.Name.Lexeme, receiver, result);
                evaluation.Throws.Add(CopyLoopBodyValues(evaluation.Values));
                return result;
            }
            case Expr.GetIndex indexed:
            {
                var receiver = Evaluate(indexed.Object);
                var index = Evaluate(indexed.Index);
                return LoopBodyIndexedValue(receiver, index);
            }
            case Expr.SetIndex indexed:
            {
                var receiver = Evaluate(indexed.Object);
                var index = Evaluate(indexed.Index);
                var value = Evaluate(indexed.Value);
                if (index is TypeInfo.StringLiteral name) StoreLoopBodyMember(evaluation, indexed.Object, name.Value, receiver, value);
                evaluation.Throws.Add(CopyLoopBodyValues(evaluation.Values));
                return value;
            }
            case Expr.PrefixIncrement increment:
                return Increment(increment.Operand);
            case Expr.PostfixIncrement increment:
                return Increment(increment.Operand);
            case Expr.Unary unary:
            {
                var operand = Evaluate(unary.Right);
                if (unary.Operator.Type == TokenType.TYPEOF)
                {
                    if (ReferenceEquals(operand, UnprovedLoopBodyType)) return UnprovedLoopBodyType;
                    return TypeInfo.String.Shared;
                }
                if (ReferenceEquals(operand, UnprovedLoopBodyType)) return operand;
                try { return UnionLoopBodyValues(LoopBodyAtoms(operand).Select(type => CheckUnaryOperandType(type, unary.Operator.Type))); }
                catch (TypeCheckException) { return UnprovedLoopBodyType; }
            }
            case Expr.Binary binary:
            {
                var left = Evaluate(binary.Left);
                var right = Evaluate(binary.Right);
                if (ReferenceEquals(left, UnprovedLoopBodyType) || ReferenceEquals(right, UnprovedLoopBodyType)) return UnprovedLoopBodyType;
                if (binary.Operator.Type is TokenType.EQUAL_EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL &&
                    LoopLiteralEquality(left, right) is { } equal)
                    return new TypeInfo.BooleanLiteral(binary.Operator.Type == TokenType.EQUAL_EQUAL_EQUAL ? equal : !equal);
                try
                {
                    return EvaluateLoopBinary(new(binary.Operator.Type, new LoopFixedValue(left), left, new LoopFixedValue(right), right,
                        binary.Left, binary.Right), UnprovedLoopBodyType, new Dictionary<BindingSymbol, TypeInfo>());
                }
                catch (TypeCheckException) { return UnprovedLoopBodyType; }
            }
            case Expr.Logical logical:
                return EvaluateLoopBodyLogical(logical, evaluation);
            case Expr.NullishCoalescing nullish:
            {
                var left = Evaluate(nullish.Left);
                if (ReferenceEquals(left, UnprovedLoopBodyType)) throw new UnprovedLoopBodyFlow();
                if (!LoopTypeMayBeNullish(left)) return left;
                var skipped = CopyLoopBodyValues(evaluation.Values);
                var right = Evaluate(nullish.Right);
                if (ExpandNonNullable(left) is not TypeInfo.Never) MergeLoopBodyState(evaluation.Graph, evaluation.Values, skipped);
                return UnionLoopBodyValues([ExpandNonNullable(left), right]);
            }
            case Expr.Ternary choice:
            {
                var branches = EvaluateLoopBodyCondition(choice.Condition, evaluation);
                List<TypeInfo> types = [];
                Dictionary<object, TypeInfo>? result = null;
                foreach (var (state, arm) in new[] { (branches.Then, choice.ThenBranch), (branches.Else, choice.ElseBranch) })
                    if (state is not null)
                    {
                        var branch = new LoopBodyEvaluation(evaluation.Graph, evaluation.Scope, state);
                        types.Add(EvaluateLoopBodyValue(arm, branch));
                        evaluation.Throws.AddRange(branch.Throws);
                        evaluation.Writes.UnionWith(branch.Writes);
                        if (result is null) result = state; else MergeLoopBodyState(evaluation.Graph, result, state);
                    }
                evaluation.Values.Clear();
                foreach (var pair in result ?? []) evaluation.Values[pair.Key] = pair.Value;
                return UnionLoopBodyValues(types);
            }
            case Expr.ObjectLiteral literal:
            {
                Dictionary<string, TypeInfo> fields = new(StringComparer.Ordinal);
                HashSet<string> optional = new(StringComparer.Ordinal);
                bool proven = true;
                foreach (var property in literal.Properties)
                {
                    string? key = property.Key switch
                    {
                        Expr.IdentifierKey identifier => identifier.Name.Lexeme,
                        Expr.LiteralKey literalKey => literalKey.Literal.Literal?.ToString(),
                        Expr.ComputedKey computed => Evaluate(computed.Expression) switch
                        {
                            TypeInfo.StringLiteral text => text.Value,
                            TypeInfo.NumberLiteral number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            _ => null
                        },
                        _ => null
                    };
                    var value = Evaluate(property.Value);
                    if (property.IsSpread)
                    {
                        var spreadFields = value switch
                        {
                            TypeInfo.Record spread => spread.Fields.AsEnumerable(),
                            TypeInfo.Interface spread => spread.GetAllMembers(),
                            _ => null
                        };
                        if (spreadFields is null) { proven = false; continue; }
                        foreach (var pair in spreadFields)
                        {
                            fields[pair.Key] = pair.Value;
                            if (LoopBodyMemberOptional(value, pair.Key)) optional.Add(pair.Key); else optional.Remove(pair.Key);
                        }
                    }
                    else if (property.Kind == Expr.ObjectPropertyKind.Value && key is not null)
                    {
                        fields[key] = value;
                        optional.Remove(key);
                    }
                    else proven = false;
                }
                return !proven || fields.Values.Any(HasUnprovedLoopBodyType) ? UnprovedLoopBodyType
                    : new TypeInfo.Record(fields.ToFrozenDictionary(StringComparer.Ordinal), OptionalFields: optional.ToFrozenSet(StringComparer.Ordinal));
            }
            case Expr.ArrayLiteral literal:
            {
                List<TypeInfo> values = [];
                foreach (var element in literal.Elements)
                {
                    var value = Evaluate(element is Expr.Spread spread ? spread.Expression : element);
                    if (element is Expr.Spread)
                    {
                        if (!TryGetSpreadElementType(value, out value)) value = UnprovedLoopBodyType;
                    }
                    values.Add(value);
                }
                var elementType = UnionLoopBodyValues(values);
                return ReferenceEquals(elementType, UnprovedLoopBodyType) ? elementType : new TypeInfo.Array(WidenLiteralType(elementType));
            }
            case Expr.Get get:
            {
                var receiver = Evaluate(get.Object);
                evaluation.Throws.Add(CopyLoopBodyValues(evaluation.Values));
                if (LoopBodyMemberSlot(evaluation, get.Object, get.Name.Lexeme) is { } memberSlot &&
                    evaluation.Values.TryGetValue(memberSlot, out var narrowedMember)) return narrowedMember;
                if (ReferenceEquals(receiver, UnprovedLoopBodyType)) return receiver;
                var members = LoopBodyAtoms(receiver).Select(type => LoopBodyReadableMember(type, get.Name.Lexeme)).ToArray();
                return members.Any(type => type is null) ? UnprovedLoopBodyType : UnionLoopBodyValues(members.OfType<TypeInfo>());
            }
            case Expr.Call call:
            {
                if (UngroupGenericTupleExpression(call.Callee) is Expr.ArrowFunction immediate)
                    return EvaluateLoopBodyImmediateCall(call, immediate, evaluation);
                var callee = Evaluate(call.Callee);
                var arguments = EvaluateLoopBodyArguments(call.Arguments, evaluation);
                evaluation.Throws.Add(CopyLoopBodyValues(evaluation.Values));
                if (ReferenceEquals(callee, UnprovedLoopBodyType) || arguments.Any(type => ReferenceEquals(type, UnprovedLoopBodyType)))
                    return UnprovedLoopBodyType;
                return call.TypeArgs is { Count: > 0 }
                    ? EvaluateLoopBodyExplicitCall(callee, call, evaluation.Scope)
                    : EvaluateLoopCallReturn(callee, arguments, UnprovedLoopBodyType);
            }
            case Expr.New created:
            {
                var callee = Evaluate(created.Callee);
                var arguments = EvaluateLoopBodyArguments(created.Arguments, evaluation);
                evaluation.Throws.Add(CopyLoopBodyValues(evaluation.Values));
                if (arguments.Any(type => ReferenceEquals(type, UnprovedLoopBodyType))) return UnprovedLoopBodyType;
                if (created.TypeArgs is { Count: > 0 }) return UnprovedLoopBodyType;
                if (callee is TypeInfo.Class) return new TypeInfo.Instance(callee);
                if (callee is TypeInfo.GenericClass generic && InferConstructorTypeArguments(generic, arguments) is { } inferred)
                    return new TypeInfo.Instance(new TypeInfo.InstantiatedGeneric(generic, inferred));
                return UnprovedLoopBodyType;
            }
            default: throw new UnprovedLoopBodyFlow();
        }

        TypeInfo Increment(Expr operand)
        {
            var value = Evaluate(operand);
            if (ReferenceEquals(value, UnprovedLoopBodyType)) return value;
            TypeInfo result = value is TypeInfo.BigInt or TypeInfo.BigIntLiteral ? TypeInfo.BigInt.Shared : TypeInfo.Primitive.Number;
            if (operand is Expr.Variable variable && LoopBodySlot(evaluation, variable.Name.Lexeme) is { } slot)
                StoreLoopBodyValue(evaluation, slot, result);
            else if (operand is Expr.Get get) InvalidateLoopBodyMember(evaluation, get.Object, get.Name.Lexeme);
            else if (operand is Expr.GetIndex { Index: Expr.Literal { Value: string key } } indexed)
                InvalidateLoopBodyMember(evaluation, indexed.Object, key);
            return result;
        }
    }

    private static IEnumerable<TypeInfo> LoopBodyAtoms(TypeInfo type) => type is TypeInfo.Union union ? union.FlattenedTypes : [type];
    private static bool? LoopLiteralEquality(TypeInfo left, TypeInfo right)
    {
        if (left is TypeInfo.StringLiteral first && right is TypeInfo.StringLiteral second) return first.Value == second.Value;
        if (left is TypeInfo.NumberLiteral firstNumber && right is TypeInfo.NumberLiteral secondNumber) return firstNumber.Value == secondNumber.Value;
        if (left is TypeInfo.BooleanLiteral firstBoolean && right is TypeInfo.BooleanLiteral secondBoolean) return firstBoolean.Value == secondBoolean.Value;
        return null;
    }
}
