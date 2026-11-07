using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private static IEnumerable<Stmt.Function> ClassMethodsInCheckOrder(IEnumerable<Stmt.Function> methods)
        => methods.Where(method => method.Body is not null).OrderBy(method => method.PrivateAccessorName is null
            ? 2 : method.Parameters.Count);

    private static TypeInfo? InferPrivateAccessorSetterParameter(Stmt.Function method, TypeInfo.MutableClass owner)
    {
        if (method.PrivateAccessorName is not { } name ||
            method.Parameters is not [{ Type: null, DefaultValue: null, TypeAnnotationNode: null }])
            return null;
        var methods = method.IsStatic ? owner.StaticPrivateMethods : owner.PrivateMethods;
        return methods.GetValueOrDefault(PrivateAccessorNames.Getter(name.Lexeme)) is TypeInfo.Function getter
            ? PendingPrivateAccessorType(getter.ReturnType) : null;
    }

    private static TypeInfo PendingPrivateAccessorType(TypeInfo type)
        => type is TypeInfo.Inferred ? TypeInfo.Any.Shared : type;

    private static bool TryGetPrivateAccessorTypes(TypeInfo.Class owner, string name,
        out bool isStatic, out TypeInfo? readType, out TypeInfo? writeType)
    {
        bool found = PrivateAccessorNames.TryGet(owner.PrivateMethodTypes, owner.StaticPrivateMethodTypes,
            name, out isStatic, out var getter, out var setter);
        readType = (getter as TypeInfo.Function)?.ReturnType;
        writeType = (setter as TypeInfo.Function)?.ParamTypes[0];
        return found;
    }

    private static void ValidatePrivateSetterReturns(Stmt.Function method)
    {
        if (method.PrivateAccessorName is null || method.Parameters.Count == 0 || method.Body is null)
            return;

        var collector = new ClassMemberReturnCollector(statement => throw new TypeCheckException(
            "Setters cannot return a value.", statement.Keyword.Line, tsCode: "TS2408"));
        foreach (var statement in method.Body) collector.Visit(statement);
    }

    private TypeInfo CheckPrivateAccessorFacet(TypeInfo receiver, TypeInfo.Class owner, bool isStatic, Token name,
        TypeInfo? facet, bool isWrite = false)
    {
        ValidatePrivateReceiver(receiver, owner, isStatic, name);
        if (facet is null)
            throw isWrite
                ? new TypeCheckException($"Cannot assign to '{name.Lexeme}' because it is a read-only property.", name.Line, tsCode: "TS2540")
                : new TypeCheckException($"Private accessor '{name.Lexeme}' was defined without a getter.", name.Line, tsCode: "TS2806");
        if (!isWrite && facet is TypeInfo.Inferred)
            facet = PendingPrivateAccessorType(ResolveFieldStagePrivateGetter(owner, isStatic, name.Lexeme, facet));
        return InstantiatePrivateCompoundFieldType(facet, receiver, owner);
    }
}
