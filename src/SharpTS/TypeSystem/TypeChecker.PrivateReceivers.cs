using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private void ValidatePrivateReceiver(TypeInfo receiver, TypeInfo.Class owner, bool isStatic, Token token)
    {
        string name = token.Lexeme;
        if (FindInvalidPrivateReceiver(receiver, owner, isStatic) is { } invalidReceiver)
        {
            if (invalidReceiver is TypeInfo.Unknown)
                throw new TypeCheckException("Object is of type 'unknown'.", token.Line, tsCode: "TS18046");
            var receiverCores = EnumerateClassCores(invalidReceiver);
            // Static private names are not inherited. The lexical owner's own
            // ancestor slot also cannot be a conflicting foreign declaration.
            bool conflictingPrivateName = (isStatic ? receiverCores.Take(1) : receiverCores)
                .Any(core => core.DeclarationId != owner.Core.DeclarationId && (isStatic
                    ? core.StaticPrivateFieldTypes.ContainsKey(name) || core.StaticPrivateMethodTypes.ContainsKey(name)
                    : core.PrivateFieldTypes.ContainsKey(name) || core.PrivateMethodTypes.ContainsKey(name)));
            throw new TypeCheckException(conflictingPrivateName
                ? $"Property '{name}' is not accessible on type '{invalidReceiver}' because it has a different private declaration."
                : $"Property '{name}' does not exist on type '{invalidReceiver}'.",
                token.Line, tsCode: conflictingPrivateName ? "TS18013" : "TS2339");
        }
    }

    private static TypeInfo? FindInvalidPrivateReceiver(TypeInfo receiver, TypeInfo.Class owner, bool isStatic, int depth = 0)
    {
        if (depth > 32) return receiver;
        if (receiver is TypeInfo.Any or TypeInfo.Inferred) return null;
        if (receiver is TypeInfo.TypeParameter parameter)
            return ApparentTypeOf(parameter) is { } constraint && constraint is not TypeInfo.Any
                ? FindInvalidPrivateReceiver(constraint, owner, isStatic, depth + 1)
                : receiver;
        if (receiver is TypeInfo.Union union)
        {
            foreach (var part in union.FlattenedTypes)
                if (FindInvalidPrivateReceiver(part, owner, isStatic, depth + 1) is { } invalid) return invalid;
            return null;
        }
        if (receiver is TypeInfo.Intersection intersection)
            return intersection.FlattenedTypes.Any(part => FindInvalidPrivateReceiver(part, owner, isStatic, depth + 1) is null)
                ? null : receiver;
        bool valid = isStatic
            ? receiver is TypeInfo.Class or TypeInfo.MutableClass or TypeInfo.GenericClass or TypeInfo.InstantiatedGeneric &&
                SelectedSourceClassId(receiver) == owner.Core.DeclarationId
            : receiver is TypeInfo.Instance instance && SourceDerivesFromDeclaration(instance.ResolvedClassType, owner.Core.DeclarationId);
        return valid ? null : receiver;
    }

}
