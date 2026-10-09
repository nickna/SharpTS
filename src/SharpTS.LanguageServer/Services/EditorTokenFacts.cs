using SharpTS.Parsing;

namespace SharpTS.LanguageServer.Services;

internal static class EditorTokenFacts
{
    // Mirrors the parser's contextual identifier grammar for syntax admission only.
    // The fresh parse/check remains the authority for receiver and invocation facts.
    internal static bool IsContextualIdentifier(TokenType type) => type is
        TokenType.TYPE or TokenType.MODULE or TokenType.NAMESPACE or TokenType.ASYNC or TokenType.DECLARE or
        TokenType.ABSTRACT or TokenType.READONLY or TokenType.OVERRIDE or TokenType.GLOBAL or TokenType.OF or
        TokenType.FROM or TokenType.SATISFIES or TokenType.ACCESSOR or TokenType.OUT or TokenType.UNIQUE or
        TokenType.UNKNOWN or TokenType.NEVER or TokenType.INFER or TokenType.KEYOF or TokenType.ASSERTS or
        TokenType.IS or TokenType.TYPE_STRING or TokenType.TYPE_NUMBER or TokenType.TYPE_BOOLEAN or
        TokenType.TYPE_SYMBOL or TokenType.TYPE_BIGINT or TokenType.GET or TokenType.SET or TokenType.UNDEFINED or
        TokenType.CONSTRUCTOR or TokenType.SYMBOL or TokenType.BIGINT;
}
