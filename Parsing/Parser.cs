namespace SharpTS.Parsing;

/// <summary>
/// Decorator specification mode - determines how decorators are parsed and validated.
/// </summary>
public enum DecoratorMode
{
    /// <summary>No decorator support (default for backwards compatibility)</summary>
    None,
    /// <summary>Legacy/Experimental TypeScript decorators (Stage 2)</summary>
    Legacy,
    /// <summary>TC39 Stage 3 decorators (ES2023+)</summary>
    Stage3
}

/// <summary>
/// Recursive descent parser that builds an AST from tokens.
/// </summary>
/// <remarks>
/// Second stage of the compiler pipeline. Consumes the token stream from <see cref="Lexer"/>
/// and produces an Abstract Syntax Tree of <see cref="Stmt"/> and <see cref="Expr"/> nodes
/// (defined in AST.cs). Performs syntax-directed desugaring (e.g., converting for loops to
/// while loops, expanding destructuring patterns). The resulting AST is validated by
/// <see cref="TypeChecker"/> and then executed by <see cref="Interpreter"/> or compiled
/// by <see cref="ILCompiler"/>.
/// </remarks>
/// <seealso cref="Lexer"/>
/// <seealso cref="Stmt"/>
/// <seealso cref="Expr"/>
public partial class Parser(List<Token> tokens, DecoratorMode decoratorMode = DecoratorMode.None)
{
    private readonly List<Token> _tokens = tokens;
    private readonly DecoratorMode _decoratorMode = decoratorMode;
    private int _current = 0;
    private int _tempVarCounter = 0;

    // Internal pattern representation for destructuring (not AST nodes)
    private abstract record DestructurePattern;
    private record ArrayPattern(List<ArrayPatternElement> Elements, int Line) : DestructurePattern;
    private record ObjectPattern(List<ObjectPatternProperty> Properties, int Line) : DestructurePattern;
    private record IdentifierPattern(Token Name, Expr? DefaultValue) : DestructurePattern;
    private record RestPattern(Token Name) : DestructurePattern;

    private record ArrayPatternElement(DestructurePattern? Pattern, bool IsHole);
    private record ObjectPatternProperty(Token Key, DestructurePattern Value, Expr? DefaultValue);

    private Token GenerateTempVar(int line) =>
        new Token(TokenType.IDENTIFIER, $"_dest{_tempVarCounter++}", null, line);

    public List<Stmt> Parse()
    {
        List<Stmt> statements = [];
        while (!IsAtEnd())
        {
            statements.Add(Declaration());
        }
        return statements;
    }

    // ============== TOKEN NAVIGATION ==============

    private bool Match(params TokenType[] types)
    {
        foreach (TokenType type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }
        return false;
    }

    private Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw new Exception(message);
    }

    /// <summary>
    /// Consumes a token that can be used as a property name after '.'.
    /// This includes identifiers and reserved keywords (JavaScript allows keywords as property names).
    /// </summary>
    private Token ConsumePropertyName(string message)
    {
        Token current = Peek();

        // Accept any identifier
        if (current.Type == TokenType.IDENTIFIER)
            return Advance();

        // Accept keywords that can be used as property names
        // In JavaScript/TypeScript, all keywords are valid property names
        if (IsKeyword(current.Type))
        {
            Advance();
            // Convert keyword token to identifier token for AST consistency
            return new Token(TokenType.IDENTIFIER, current.Lexeme, null, current.Line);
        }

        throw new Exception(message);
    }

    /// <summary>
    /// Checks if a token type is a keyword that can be used as a property name.
    /// </summary>
    private static bool IsKeyword(TokenType type)
    {
        return type switch
        {
            TokenType.ABSTRACT or TokenType.AS or TokenType.ASYNC or TokenType.AWAIT or
            TokenType.BREAK or TokenType.CASE or TokenType.CATCH or TokenType.CLASS or
            TokenType.CONST or TokenType.CONSTRUCTOR or TokenType.CONTINUE or
            TokenType.DEFAULT or TokenType.DO or TokenType.ELSE or
            TokenType.ENUM or TokenType.EXPORT or TokenType.EXTENDS or TokenType.FALSE or
            TokenType.FINALLY or TokenType.FOR or TokenType.FROM or TokenType.FUNCTION or
            TokenType.GET or TokenType.IF or TokenType.IMPLEMENTS or TokenType.IMPORT or
            TokenType.IN or TokenType.INSTANCEOF or TokenType.INTERFACE or TokenType.LET or
            TokenType.NEVER or TokenType.NEW or TokenType.NULL or TokenType.OF or TokenType.OVERRIDE or
            TokenType.PRIVATE or TokenType.PROTECTED or TokenType.PUBLIC or TokenType.READONLY or
            TokenType.RETURN or TokenType.SET or TokenType.STATIC or TokenType.SUPER or
            TokenType.SWITCH or TokenType.THIS or TokenType.THROW or TokenType.TRUE or
            TokenType.TRY or TokenType.TYPE or TokenType.TYPEOF or TokenType.UNKNOWN or
            TokenType.VAR or TokenType.WHILE => true,
            _ => false
        };
    }

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

    private bool CheckNext(TokenType type) => PeekNext().Type == type;

    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    private bool IsAtEnd() => Peek().Type == TokenType.EOF;

    private Token Peek() => _tokens[_current];

    private Token PeekNext()
    {
        if (_current + 1 >= _tokens.Count) return _tokens.Last();
        return _tokens[_current + 1];
    }

    private Token Previous() => _tokens[_current - 1];

    private Expr? TryParseAngleBracketAssertion()
    {
        int saved = _current;
        try
        {
            Advance(); // consume <
            if (!IsTypeStart()) { _current = saved; return null; }

            string typeName = ParseTypeAnnotation();
            if (!Check(TokenType.GREATER)) { _current = saved; return null; }
            Advance(); // consume >

            Expr expression = Unary();
            return new Expr.TypeAssertion(expression, typeName);
        }
        catch { _current = saved; return null; }
    }

    private bool IsTypeStart() =>
        Check(TokenType.IDENTIFIER) ||
        Check(TokenType.TYPE_STRING) ||
        Check(TokenType.TYPE_NUMBER) ||
        Check(TokenType.TYPE_BOOLEAN) ||
        Check(TokenType.UNKNOWN) ||
        Check(TokenType.NEVER) ||
        Check(TokenType.NULL) ||
        Check(TokenType.LEFT_PAREN) ||
        Check(TokenType.LEFT_BRACE) ||  // for inline object types: { x: number }
        Check(TokenType.LEFT_BRACKET) ||  // for tuple types: [string, number]
        Check(TokenType.INFER) ||  // for conditional type infer patterns
        Check(TokenType.STRING) ||  // for string literal types: "hello" | "world"
        Check(TokenType.NUMBER) ||  // for number literal types: 1 | 2 | 3
        Check(TokenType.TRUE) ||  // for boolean literal type true
        Check(TokenType.FALSE) ||  // for boolean literal type false
        Check(TokenType.TEMPLATE_FULL) ||  // for template literal types: `literal`
        Check(TokenType.TEMPLATE_HEAD);  // for template literal types: `prefix${

    // ============== GENERIC TYPE CLOSING BRACKET HANDLING ==============
    //
    // The lexer tokenizes >> as GREATER_GREATER (right-shift operator) and >>> as
    // GREATER_GREATER_GREATER (unsigned right-shift). This causes issues with nested
    // generic types like Partial<Readonly<T>> where the >> should be two separate > tokens.
    //
    // Solution: When parsing type contexts, we handle compound greater-than tokens specially.
    // If we need a single > but have >> or >>>, we "split" the token by replacing it with
    // the remainder (>> becomes >, >>> becomes >>). This follows the approach used by
    // C#, Java, and TypeScript compilers.

    /// <summary>
    /// Checks if the current token can provide a closing '>' for generic type syntax.
    /// Returns true for GREATER, GREATER_GREATER, or GREATER_GREATER_GREATER.
    /// </summary>
    private bool CheckGreaterInTypeContext()
    {
        if (IsAtEnd()) return false;
        return Peek().Type is TokenType.GREATER
            or TokenType.GREATER_GREATER
            or TokenType.GREATER_GREATER_GREATER;
    }

    /// <summary>
    /// Consumes a single '>' from the current token in a type context.
    /// For GREATER, advances normally. For GREATER_GREATER or GREATER_GREATER_GREATER,
    /// splits the token by replacing it with the remainder.
    /// </summary>
    /// <returns>True if a '>' was consumed, false otherwise.</returns>
    private bool MatchGreaterInTypeContext()
    {
        if (IsAtEnd()) return false;

        Token current = Peek();
        switch (current.Type)
        {
            case TokenType.GREATER:
                Advance();
                return true;

            case TokenType.GREATER_GREATER:
                // Split >> into > (consumed) and > (remaining)
                _tokens[_current] = new Token(TokenType.GREATER, ">", null, current.Line);
                return true;

            case TokenType.GREATER_GREATER_GREATER:
                // Split >>> into > (consumed) and >> (remaining)
                _tokens[_current] = new Token(TokenType.GREATER_GREATER, ">>", null, current.Line);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Consumes a '>' in type context or throws an exception with the given message.
    /// Handles compound tokens (>> and >>>) by splitting them.
    /// </summary>
    private void ConsumeGreaterInTypeContext(string message)
    {
        if (!MatchGreaterInTypeContext())
            throw new Exception(message);
    }
}
