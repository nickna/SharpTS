using SharpTS.Parsing;
using SharpTS.TypeSystem.Narrowing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private NarrowingPath? ExtractNarrowingPath(Expr expression) =>
        NarrowingPathExtractor.TryExtract(expression, name => FindLexicalPrivateOwner(name.Lexeme)?.Core.DeclarationId);

    private void InvalidatePrivateNarrowing(Expr receiver, Token name)
    {
        if (ExtractNarrowingPath(new Expr.GetPrivate(receiver, name)) is { } path)
            InvalidateNarrowingsFor(path);
    }
}
