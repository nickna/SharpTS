using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private TypeInfo EvaluateLoopBodyLogicalAssignment(Expr.LogicalAssign assignment, LoopBodyEvaluation evaluation)
    {
        var current = LoopBodyValue(evaluation, assignment.Name.Lexeme);
        if (ReferenceEquals(current, UnprovedLoopBodyType)) throw new UnprovedLoopBodyFlow();
        var skipped = assignment.Operator.Type switch
        {
            TokenType.OR_OR_EQUAL => NarrowLogicalTruthy(current),
            TokenType.AND_AND_EQUAL => NarrowLogicalFalsy(current),
            _ => ExpandNonNullable(current)
        };
        bool runs = assignment.Operator.Type switch
        {
            TokenType.OR_OR_EQUAL => !IsAlwaysTruthy(current),
            TokenType.AND_AND_EQUAL => !IsAlwaysFalsy(current),
            _ => LoopTypeMayBeNullish(current)
        };
        if (!runs) return skipped;
        var previous = CopyLoopBodyValues(evaluation.Values);
        var value = EvaluateLoopBodyValue(assignment.Value, evaluation);
        if (LoopBodySlot(evaluation, assignment.Name.Lexeme) is not { } slot) throw new UnprovedLoopBodyFlow();
        StoreLoopBodyValue(evaluation, slot, value);
        if (skipped is not TypeInfo.Never)
        {
            previous[slot] = skipped;
            MergeLoopBodyState(evaluation.Graph, evaluation.Values, previous);
        }
        return skipped is TypeInfo.Never ? value : UnionLoopBodyValues([skipped, value]);
    }

    private TypeInfo LoopBodyIndexedValue(TypeInfo receiver, TypeInfo index)
    {
        if (ReferenceEquals(receiver, UnprovedLoopBodyType) || ReferenceEquals(index, UnprovedLoopBodyType)) return UnprovedLoopBodyType;
        List<TypeInfo> values = [];
        foreach (var type in LoopBodyAtoms(receiver))
        {
            TypeInfo? value = type switch
            {
                TypeInfo.Array array when index is TypeInfo.NumberLiteral or TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => WithUncheckedUndefined(array.ElementType),
                TypeInfo.Tuple tuple when index is TypeInfo.NumberLiteral number && number.Value == Math.Truncate(number.Value) &&
                    number.Value >= 0 && number.Value < tuple.Elements.Count =>
                    tuple.Elements[(int)number.Value].IsOptional ? CreateUnion(tuple.Elements[(int)number.Value].Type, TypeInfo.Undefined.Shared)
                        : tuple.Elements[(int)number.Value].Type,
                TypeInfo.Record record when index is TypeInfo.StringLiteral name => LoopBodyReadableMember(record, name.Value)
                    ?? (record.StringIndexType is { } stringIndex ? WithUncheckedUndefined(stringIndex) : null),
                TypeInfo.Interface iface when index is TypeInfo.StringLiteral name => LoopBodyReadableMember(iface, name.Value)
                    ?? (iface.StringIndexType is { } stringIndex ? WithUncheckedUndefined(stringIndex) : null),
                _ => null
            };
            if (value is null) return UnprovedLoopBodyType;
            values.Add(value);
        }
        return UnionLoopBodyValues(values);
    }
}
