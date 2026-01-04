namespace SharpTS.Parsing;

/// <summary>
/// Enumeration of all token types recognized by the lexer.
/// </summary>
/// <remarks>
/// Categorized into single-character tokens, multi-character operators, literals
/// (identifiers, strings, numbers, template strings), keywords (TypeScript and JavaScript),
/// and TypeScript-specific type keywords. Used by <see cref="Lexer"/> to classify tokens
/// and by <see cref="Parser"/> to match expected syntax.
/// </remarks>
public enum TokenType
{
    // Single-character tokens
    LEFT_PAREN, RIGHT_PAREN, LEFT_BRACE, RIGHT_BRACE,
    LEFT_BRACKET, RIGHT_BRACKET,
    COMMA, DOT, MINUS, PLUS, SEMICOLON, SLASH, STAR, PERCENT,
    COLON, QUESTION,

    // One or two character tokens
    BANG, BANG_EQUAL, BANG_EQUAL_EQUAL,
    EQUAL, EQUAL_EQUAL, EQUAL_EQUAL_EQUAL, ARROW,
    GREATER, GREATER_EQUAL,
    LESS, LESS_EQUAL,
    AND_AND, OR_OR,
    QUESTION_QUESTION, QUESTION_DOT,
    PLUS_PLUS, MINUS_MINUS,
    PLUS_EQUAL, MINUS_EQUAL, STAR_EQUAL, SLASH_EQUAL, PERCENT_EQUAL,
    STAR_STAR,
    DOT_DOT_DOT,
    // Bitwise operators
    AMPERSAND, PIPE, CARET, TILDE,
    LESS_LESS, GREATER_GREATER, GREATER_GREATER_GREATER,
    AMPERSAND_EQUAL, PIPE_EQUAL, CARET_EQUAL,
    LESS_LESS_EQUAL, GREATER_GREATER_EQUAL, GREATER_GREATER_GREATER_EQUAL,

    // Literals
    IDENTIFIER, STRING, NUMBER,
    TEMPLATE_HEAD, TEMPLATE_MIDDLE, TEMPLATE_TAIL, TEMPLATE_FULL,

    // Keywords
    AS, BREAK, CASE, CONST, CLASS, CONSTRUCTOR, CONTINUE, DEFAULT, DO, ELSE, ENUM, EXTENDS, FALSE, FUNCTION, FOR, GET, IF,
    IMPLEMENTS, IN, INSTANCEOF, INTERFACE, LET, NEVER,
    NEW, NULL, OF, PRIVATE, PROTECTED, PUBLIC, READONLY, RETURN, SET, STATIC, SUPER, SWITCH, THIS, THROW, TRUE, TRY, CATCH, FINALLY, TYPE, TYPEOF, UNKNOWN, VAR, WHILE,

    // TypeScript Specific
    TYPE_STRING, TYPE_NUMBER, TYPE_BOOLEAN, TYPE_SYMBOL,
    SYMBOL,  // Symbol() constructor

    EOF
}

/// <summary>
/// Represents a single token from the source code.
/// </summary>
/// <remarks>
/// Produced by <see cref="Lexer"/> during lexical analysis. Contains the token type,
/// the original source text (lexeme), any literal value (for strings/numbers), and
/// the source line number for error reporting. Consumed by <see cref="Parser"/> to
/// build the AST and preserved in AST nodes for runtime error messages.
/// </remarks>
/// <seealso cref="TokenType"/>
/// <seealso cref="Lexer"/>
public class Token(TokenType type, string lexeme, object? literal, int line)
{
    public TokenType Type { get; } = type;
    public string Lexeme { get; } = lexeme;
    public object? Literal { get; } = literal;
    public int Line { get; } = line;

    public override string ToString() => $"{Type} {Lexeme} {Literal}";
}
