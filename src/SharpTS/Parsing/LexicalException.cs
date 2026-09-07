namespace SharpTS.Parsing;

/// <summary>An invalid source token, as distinct from an internal lexer failure.</summary>
public sealed class LexicalException(string message) : Exception(message);
