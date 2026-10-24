using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private TypeInfo CheckLogicalSetPrivate(Expr.LogicalSetPrivate logical)
    {
        using var lookup = BeginSourceMemberLookup(logical, logical.Name, MemberOperation.Read | MemberOperation.Write);
        var (receiver, owner, isStatic, fieldType) = CheckWritablePrivateField(
            logical, logical.Object, logical.Name, allowDynamicReceiver: true);
        TypeInfo right = CheckExpr(logical.Value);
        if (!IsCompatible(fieldType, right))
            throw new TypeCheckException($"Cannot assign '{right}' to private field '{logical.Name.Lexeme}' of type '{fieldType}'.",
                logical.Operator.Line, tsCode: "TS2322");
        ObservePrivateSourceMemberSelection(logical.Name, receiver, owner,
            isStatic ? MemberFacet.PrivateStatic : MemberFacet.PrivateInstance);
        InvalidatePrivateNarrowing(logical.Object, logical.Name);
        lookup?.Succeed();
        TypeInfo retained = logical.Operator.Type switch
        {
            TokenType.OR_OR_EQUAL => NarrowLogicalTruthy(fieldType),
            TokenType.AND_AND_EQUAL => NarrowLogicalFalsy(fieldType),
            _ => FilterUnionConstituents(fieldType, type => type is not (TypeInfo.Null or TypeInfo.Undefined)),
        };
        return CreateUnion(retained, right);
    }
}
