using System.Runtime.CompilerServices;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // Live REPL bindings keep their declaration proof across Check calls, while
    // discarded signature graphs can be collected. Keys use reference identity.
    private readonly ConditionalWeakTable<TypeInfo, object> _specializedLiteralSignatures = new();

    private static bool IsSpecializedLiteralAnnotation(TypeNode? annotation) =>
        // The parser represents null by its actual keyword token, and an
        // uninterpolated template by a static template node. Both are TS LiteralType syntax.
        annotation is { IsParenthesizedLiteral: false } && (annotation is LiteralTypeNode or
            TemplateLiteralTypeNode { InterpolatedTypes.Count: 0 } or
            NamedTypeNode { NameToken.Type: TokenType.NULL });

    private T RegisterSpecializedOverload<T>(T signature, IEnumerable<TypeNode?> annotations) where T : TypeInfo
    {
        if (signature is TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.CallSignature &&
            annotations.Any(IsSpecializedLiteralAnnotation))
            _specializedLiteralSignatures.GetValue(signature, static _ => new object());
        return signature;
    }

    private void CopyOverloadPriority(TypeInfo original, TypeInfo replacement)
    {
        if (!ReferenceEquals(original, replacement) && _specializedLiteralSignatures.TryGetValue(original, out _))
            _specializedLiteralSignatures.GetValue(replacement, static _ => new object());
    }

    private IEnumerable<T> PrioritizeSpecializedOverloads<T>(IEnumerable<T> signatures) where T : TypeInfo =>
        signatures.OrderByDescending(signature => _specializedLiteralSignatures.TryGetValue(signature, out _));
}
