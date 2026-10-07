using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private readonly Stack<TypeInfo.Class?> _privateInEnclosingClasses = [];

    internal TypeInfo VisitPrivateIn(Expr.PrivateIn expr)
    {
        SourceDocument? sourceDocument = ShouldCaptureSourceMemberFacts ? CurrentSourceDocument : null;
        Token? writtenName = sourceDocument is null ? null : WrittenSourceMemberName(sourceDocument, expr, expr.Name);
        if (writtenName is not null)
            Members.RemoveOperation(sourceDocument, writtenName, MemberOperation.Presence, expr);
        if (_currentClass == null)
            throw new TypeCheckException("Private identifiers are not allowed outside class bodies.", tsCode: "TS18016");
        string name = expr.Name.Lexeme;
        var owner = new[] { _currentClass }.Concat(_privateInEnclosingClasses).FirstOrDefault(candidate => candidate is not null
            && (candidate.PrivateFieldTypes.ContainsKey(name) || candidate.StaticPrivateFieldTypes.ContainsKey(name)
                || candidate.PrivateMethodTypes.ContainsKey(name) || candidate.StaticPrivateMethodTypes.ContainsKey(name)));
        if (owner is null)
            throw new TypeCheckException($"Private name '{name}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");
        _typeMap.SetPrivateInOwner(expr.Name, owner);
        var type = CheckExpr(expr.Object);
        if (type is TypeInfo.Unknown)
            throw new TypeCheckException("Object is of type 'unknown'.", tsCode: "TS18046");
        if (!IsAnyPermissive(type) && IsInvalidPrivateInReceiver(type))
            throw new TypeCheckException($"Type '{type}' is not assignable to type 'object'.", tsCode: "TS2322");
        if (writtenName is not null)
        {
            // Presence checks name the lexical brand even when the probed object is not
            // nominally an instance of the declaring class. The checker recorded that owner.
            TypeInfo.Class? checkedOwner = _typeMap.GetPrivateInOwner(expr.Name);
            if (checkedOwner is not null)
            {
                MemberFacet facet = checkedOwner.PrivateFieldTypes.ContainsKey(name) ||
                    checkedOwner.PrivateMethodTypes.ContainsKey(name)
                    ? MemberFacet.PrivateInstance : MemberFacet.PrivateStatic;
                Members.Bind(sourceDocument, writtenName,
                    Members.ResolveSelected(checkedOwner.Core.DeclarationId, facet, name),
                    MemberOperation.Presence, owner: expr);
            }
        }
        return TypeInfo.Primitive.Boolean;
    }

    private bool IsInvalidPrivateInReceiver(TypeInfo type) => type switch
    {
        TypeInfo.Primitive or TypeInfo.String or TypeInfo.NumberLiteral or TypeInfo.StringLiteral
            or TypeInfo.BooleanLiteral or TypeInfo.Null or TypeInfo.Undefined
            or TypeInfo.BigInt or TypeInfo.Symbol or TypeInfo.UniqueSymbol or TypeInfo.Void or TypeInfo.Unknown => true,
        TypeInfo.TypeParameter parameter => ApparentTypeOf(parameter) is not { } constraint
            || constraint is TypeInfo.Any || IsInvalidPrivateInReceiver(constraint),
        TypeInfo.Union union => union.Types.Any(IsInvalidPrivateInReceiver),
        _ => false,
    };
}
