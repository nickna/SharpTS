using System.Numerics;

namespace SharpTS.Parsing;

/// <summary>
/// Lexical analyzer that tokenizes TypeScript source code.
/// </summary>
/// <remarks>
/// First stage of the compiler pipeline. Scans source text character by character,
/// producing a flat list of <see cref="Token"/> objects. Handles single/multi-character
/// operators, string/number literals, template literals with interpolation, identifiers,
/// keywords, and comments (line and block). Tracks line numbers for error reporting.
/// The token stream is consumed by <see cref="Parser"/> to build the AST.
/// </remarks>
/// <seealso cref="Token"/>
/// <seealso cref="TokenType"/>
/// <seealso cref="Parser"/>
public class Lexer(string source)
{
    private readonly string _source = source;
    private readonly List<Token> _tokens = [];
    private int _start = 0;
    private int _current = 0;
    private int _line = 1;
    // Stack to track brace depth when inside template interpolations
    private readonly Stack<int> _templateBraceDepth = new();
    // Tracks whether we're expecting an expression (true) or operator (false)
    // Used to disambiguate regex literals from division operator
    private bool _expectExpr = true;

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "abstract", TokenType.ABSTRACT },
        { "as", TokenType.AS },
        { "asserts", TokenType.ASSERTS },
        { "async", TokenType.ASYNC },
        { "await", TokenType.AWAIT },
        { "break", TokenType.BREAK },
        { "case", TokenType.CASE },
        { "class", TokenType.CLASS },
        { "const", TokenType.CONST },
        { "constructor", TokenType.CONSTRUCTOR },
        { "continue", TokenType.CONTINUE },
        { "declare", TokenType.DECLARE },
        { "default", TokenType.DEFAULT },
        { "do", TokenType.DO },
        { "else", TokenType.ELSE },
        { "enum", TokenType.ENUM },
        { "export", TokenType.EXPORT },
        { "extends", TokenType.EXTENDS },
        { "false", TokenType.FALSE },
        { "function", TokenType.FUNCTION },
        { "for", TokenType.FOR },
        { "from", TokenType.FROM },
        { "get", TokenType.GET },
        { "if", TokenType.IF },
        { "implements", TokenType.IMPLEMENTS },
        { "import", TokenType.IMPORT },
        { "in", TokenType.IN },
        { "infer", TokenType.INFER },
        { "instanceof", TokenType.INSTANCEOF },
        { "interface", TokenType.INTERFACE },
        { "is", TokenType.IS },
        { "keyof", TokenType.KEYOF },
        { "let", TokenType.LET },
        { "namespace", TokenType.NAMESPACE },
        { "never", TokenType.NEVER },
        { "new", TokenType.NEW },
        { "null", TokenType.NULL },
        { "of", TokenType.OF },
        { "override", TokenType.OVERRIDE },
        { "private", TokenType.PRIVATE },
        { "protected", TokenType.PROTECTED },
        { "public", TokenType.PUBLIC },
        { "readonly", TokenType.READONLY },
        { "return", TokenType.RETURN },
        { "set", TokenType.SET },
        { "static", TokenType.STATIC },
        { "super", TokenType.SUPER },
        { "switch", TokenType.SWITCH },
        { "this", TokenType.THIS },
        { "throw", TokenType.THROW },
        { "true", TokenType.TRUE },
        { "try", TokenType.TRY },
        { "catch", TokenType.CATCH },
        { "finally", TokenType.FINALLY },
        { "type", TokenType.TYPE },
        { "typeof", TokenType.TYPEOF },
        { "undefined", TokenType.UNDEFINED },
        { "unique", TokenType.UNIQUE },
        { "unknown", TokenType.UNKNOWN },
        { "while", TokenType.WHILE },
        { "yield", TokenType.YIELD },
        { "string", TokenType.TYPE_STRING },
        { "number", TokenType.TYPE_NUMBER },
        { "boolean", TokenType.TYPE_BOOLEAN },
        { "symbol", TokenType.TYPE_SYMBOL },
        { "Symbol", TokenType.SYMBOL },
        { "bigint", TokenType.TYPE_BIGINT },
        { "BigInt", TokenType.BIGINT }
    };

    public List<Token> ScanTokens()
    {
        while (!IsAtEnd())
        {
            _start = _current;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.EOF, "", null, _line));
        return _tokens;
    }

    private void ScanToken()
    {
        char c = Advance();
        switch (c)
        {
            case '(': AddToken(TokenType.LEFT_PAREN); break;
            case ')': AddToken(TokenType.RIGHT_PAREN); break;
            case '{':
                if (_templateBraceDepth.Count > 0)
                {
                    _templateBraceDepth.Push(_templateBraceDepth.Pop() + 1);
                }
                AddToken(TokenType.LEFT_BRACE);
                break;
            case '}':
                if (_templateBraceDepth.Count > 0)
                {
                    int depth = _templateBraceDepth.Pop();
                    if (depth > 0)
                    {
                        _templateBraceDepth.Push(depth - 1);
                        AddToken(TokenType.RIGHT_BRACE);
                    }
                    else
                    {
                        // End of interpolation, continue template
                        ContinueTemplateLiteral();
                    }
                }
                else
                {
                    AddToken(TokenType.RIGHT_BRACE);
                }
                break;
            case '[': AddToken(TokenType.LEFT_BRACKET); break;
            case ']': AddToken(TokenType.RIGHT_BRACKET); break;
            case ',': AddToken(TokenType.COMMA); break;
            case '.':
                if (Match('.') && Match('.'))
                    AddToken(TokenType.DOT_DOT_DOT);
                else
                    AddToken(TokenType.DOT);
                break;
            case '-':
                if (Match('-')) AddToken(TokenType.MINUS_MINUS);
                else if (Match('=')) AddToken(TokenType.MINUS_EQUAL);
                else AddToken(TokenType.MINUS);
                break;
            case '+':
                if (Match('+')) AddToken(TokenType.PLUS_PLUS);
                else if (Match('=')) AddToken(TokenType.PLUS_EQUAL);
                else AddToken(TokenType.PLUS);
                break;
            case ';': AddToken(TokenType.SEMICOLON); break;
            case '*':
                if (Match('*')) AddToken(TokenType.STAR_STAR);
                else if (Match('=')) AddToken(TokenType.STAR_EQUAL);
                else AddToken(TokenType.STAR);
                break;
            case '%': AddToken(Match('=') ? TokenType.PERCENT_EQUAL : TokenType.PERCENT); break;
            case ':': AddToken(TokenType.COLON); break;
            case '?':
                if (Match('?'))
                    AddToken(Match('=') ? TokenType.QUESTION_QUESTION_EQUAL : TokenType.QUESTION_QUESTION);
                else if (Match('.')) AddToken(TokenType.QUESTION_DOT);
                else AddToken(TokenType.QUESTION);
                break;
            case '&':
                if (Match('&'))
                    AddToken(Match('=') ? TokenType.AND_AND_EQUAL : TokenType.AND_AND);
                else if (Match('=')) AddToken(TokenType.AMPERSAND_EQUAL);
                else AddToken(TokenType.AMPERSAND);
                break;
            case '|':
                if (Match('|'))
                    AddToken(Match('=') ? TokenType.OR_OR_EQUAL : TokenType.OR_OR);
                else if (Match('=')) AddToken(TokenType.PIPE_EQUAL);
                else AddToken(TokenType.PIPE);
                break;
            case '^':
                AddToken(Match('=') ? TokenType.CARET_EQUAL : TokenType.CARET);
                break;
            case '~':
                AddToken(TokenType.TILDE);
                break;
            case '@':
                AddToken(TokenType.AT);
                break;
            case '!':
                if (Match('='))
                {
                    AddToken(Match('=') ? TokenType.BANG_EQUAL_EQUAL : TokenType.BANG_EQUAL);
                }
                else
                {
                    AddToken(TokenType.BANG);
                }
                break;
            case '=':
                if (Match('='))
                {
                    AddToken(Match('=') ? TokenType.EQUAL_EQUAL_EQUAL : TokenType.EQUAL_EQUAL);
                }
                else if (Match('>')) AddToken(TokenType.ARROW);
                else AddToken(TokenType.EQUAL);
                break;
            case '<':
                if (Match('<'))
                    AddToken(Match('=') ? TokenType.LESS_LESS_EQUAL : TokenType.LESS_LESS);
                else
                    AddToken(Match('=') ? TokenType.LESS_EQUAL : TokenType.LESS);
                break;
            case '>':
                if (Match('>'))
                {
                    if (Match('>'))
                        AddToken(Match('=') ? TokenType.GREATER_GREATER_GREATER_EQUAL : TokenType.GREATER_GREATER_GREATER);
                    else
                        AddToken(Match('=') ? TokenType.GREATER_GREATER_EQUAL : TokenType.GREATER_GREATER);
                }
                else
                    AddToken(Match('=') ? TokenType.GREATER_EQUAL : TokenType.GREATER);
                break;
            case '/':
                if (Match('/'))
                {
                    // Line comment
                    while (Peek() != '\n' && !IsAtEnd()) Advance();
                }
                else if (Match('*'))
                {
                    // Block comment
                    BlockComment();
                }
                else if (Match('='))
                {
                    AddToken(TokenType.SLASH_EQUAL);
                }
                else if (_expectExpr)
                {
                    // Regex literal
                    RegexLiteral();
                }
                else
                {
                    AddToken(TokenType.SLASH);
                }
                break;
            case ' ':
            case '\r':
            case '\t':
                break;
            case '\n':
                _line++;
                break;
            case '"': StringLiteral('"'); break;
            case '\'': StringLiteral('\''); break;
            case '`': TemplateLiteral(); break;
            default:
                if (char.IsDigit(c))
                {
                    NumberLiteral();
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    Identifier();
                }
                else
                {
                    // For brevity, we'll ignore unknown chars for now
                }
                break;
        }
    }

    private void Identifier()
    {
        while (char.IsLetterOrDigit(Peek()) || Peek() == '_') Advance();

        string text = _source[_start.._current];
        if (!Keywords.TryGetValue(text, out TokenType type))
        {
            type = TokenType.IDENTIFIER;
        }
        AddToken(type);
    }

    private void NumberLiteral()
    {
        // Consume digits and numeric separators (underscores)
        while (char.IsDigit(Peek()) || Peek() == '_')
        {
            if (Peek() == '_')
            {
                // Underscore must be between digits
                char prev = _source[_current - 1];
                char next = PeekNext();
                if (!char.IsDigit(prev) || !char.IsDigit(next))
                {
                    throw new Exception($"Numeric separator must be between digits at line {_line}");
                }
            }
            Advance();
        }

        // Check for bigint suffix BEFORE decimal point (123n is valid, 123.5n is not)
        if (Peek() == 'n')
        {
            string numStr = _source[_start.._current].Replace("_", "");
            Advance(); // consume 'n'
            AddToken(TokenType.BIGINT_LITERAL, BigInteger.Parse(numStr));
            return;
        }

        if (Peek() == '.' && (char.IsDigit(PeekNext()) || PeekNext() == '_'))
        {
            Advance(); // consume '.'

            // Check for underscore immediately after decimal point (invalid)
            if (Peek() == '_')
            {
                throw new Exception($"Numeric separator must be between digits at line {_line}");
            }

            // Consume fractional digits and numeric separators
            while (char.IsDigit(Peek()) || Peek() == '_')
            {
                if (Peek() == '_')
                {
                    // Underscore must be between digits
                    char prev = _source[_current - 1];
                    char next = PeekNext();
                    if (!char.IsDigit(prev) || !char.IsDigit(next))
                    {
                        throw new Exception($"Numeric separator must be between digits at line {_line}");
                    }
                }
                Advance();
            }
        }

        string numberStr = _source[_start.._current].Replace("_", "");
        AddToken(TokenType.NUMBER, double.Parse(numberStr));
    }

    private void StringLiteral(char delimiter)
    {
        var sb = new System.Text.StringBuilder();
        while (Peek() != delimiter && !IsAtEnd())
        {
            if (Peek() == '\n') _line++;
            if (Peek() == '\\' && !IsAtEnd())
            {
                Advance(); // consume backslash
                if (!IsAtEnd())
                {
                    char escaped = Advance();
                    sb.Append(escaped switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '\\' => '\\',
                        '"' => '"',
                        '\'' => '\'',
                        '0' => '\0',
                        'b' => '\b',
                        'f' => '\f',
                        'v' => '\v',
                        _ => escaped // unrecognized escape, keep as-is
                    });
                }
            }
            else
            {
                sb.Append(Advance());
            }
        }

        if (IsAtEnd()) return;

        Advance(); // The closing delimiter
        AddToken(TokenType.STRING, sb.ToString());
    }

    private void BlockComment()
    {
        while (!IsAtEnd())
        {
            if (Peek() == '*' && PeekNext() == '/')
            {
                Advance(); // consume *
                Advance(); // consume /
                return;
            }
            if (Peek() == '\n') _line++;
            Advance();
        }
        // Unterminated block comment - we'll just ignore for now
    }

    /// <summary>
    /// Scans a regex literal /pattern/flags starting after the opening /.
    /// </summary>
    private void RegexLiteral()
    {
        var pattern = new System.Text.StringBuilder();
        bool inCharClass = false;

        while (!IsAtEnd())
        {
            char c = Peek();

            // Handle escape sequences
            if (c == '\\' && !IsAtEnd())
            {
                pattern.Append(Advance()); // the backslash
                if (!IsAtEnd())
                {
                    pattern.Append(Advance()); // the escaped character
                }
                continue;
            }

            // Track character class brackets (regex inside [...] has different rules)
            if (c == '[') inCharClass = true;
            if (c == ']') inCharClass = false;

            // End of pattern (only if not inside character class)
            if (c == '/' && !inCharClass)
            {
                break;
            }

            // Newlines are not allowed in regex literals
            if (c == '\n')
            {
                throw new Exception($"Unterminated regex literal at line {_line}");
            }

            pattern.Append(Advance());
        }

        if (IsAtEnd())
        {
            throw new Exception($"Unterminated regex literal at line {_line}");
        }

        Advance(); // Consume closing /

        // Scan flags (g, i, m, s, u, y)
        var flags = new System.Text.StringBuilder();
        while (!IsAtEnd() && IsRegexFlag(Peek()))
        {
            flags.Append(Advance());
        }

        AddToken(TokenType.REGEX, new RegexLiteralValue(pattern.ToString(), flags.ToString()));
    }

    private static bool IsRegexFlag(char c) => c is 'g' or 'i' or 'm' or 's' or 'u' or 'y';

    private void TemplateLiteral()
    {
        int stringStart = _current;

        while (!IsAtEnd())
        {
            if (Peek() == '`')
            {
                // End of template
                string value = _source[stringStart.._current];
                Advance(); // consume closing `
                AddToken(TokenType.TEMPLATE_FULL, value);
                return;
            }
            else if (Peek() == '$' && PeekNext() == '{')
            {
                // Start of interpolation
                string value = _source[stringStart.._current];
                Advance(); // consume $
                Advance(); // consume {
                _templateBraceDepth.Push(0);
                AddToken(TokenType.TEMPLATE_HEAD, value);
                return;
            }
            else
            {
                if (Peek() == '\n') _line++;
                Advance();
            }
        }
    }

    private void ContinueTemplateLiteral()
    {
        _start = _current;
        int stringStart = _current;

        while (!IsAtEnd())
        {
            if (Peek() == '`')
            {
                // End of template
                string value = _source[stringStart.._current];
                Advance(); // consume closing `
                AddToken(TokenType.TEMPLATE_TAIL, value);
                return;
            }
            else if (Peek() == '$' && PeekNext() == '{')
            {
                // Another interpolation
                string value = _source[stringStart.._current];
                Advance(); // consume $
                Advance(); // consume {
                _templateBraceDepth.Push(0);
                AddToken(TokenType.TEMPLATE_MIDDLE, value);
                return;
            }
            else
            {
                if (Peek() == '\n') _line++;
                Advance();
            }
        }
    }

    private bool Match(char expected)
    {
        if (IsAtEnd()) return false;
        if (_source[_current] != expected) return false;

        _current++;
        return true;
    }

    private char Peek() => IsAtEnd() ? '\0' : _source[_current];

    private char PeekNext() => _current + 1 >= _source.Length ? '\0' : _source[_current + 1];

    private bool IsAtEnd() => _current >= _source.Length;

    private char Advance() => _source[_current++];

    private void AddToken(TokenType type) => AddToken(type, null);

    private void AddToken(TokenType type, object? literal)
    {
        string text = _source[_start.._current];
        _tokens.Add(new Token(type, text, literal, _line));
        // Update expression state for regex literal disambiguation
        _expectExpr = !IsExpressionEnd(type);
    }

    /// <summary>
    /// Determines if a token type ends an expression (meaning the next / should be division).
    /// Returns true for tokens after which an operator is expected, false otherwise.
    /// </summary>
    private static bool IsExpressionEnd(TokenType type)
    {
        return type switch
        {
            // Literals and identifiers - can be followed by operators
            TokenType.IDENTIFIER or
            TokenType.NUMBER or
            TokenType.STRING or
            TokenType.TRUE or
            TokenType.FALSE or
            TokenType.NULL or
            TokenType.THIS or
            TokenType.SUPER or
            TokenType.BIGINT_LITERAL or
            TokenType.REGEX or
            TokenType.TEMPLATE_FULL or
            TokenType.TEMPLATE_TAIL => true,

            // Closing brackets - expression ended
            TokenType.RIGHT_PAREN or
            TokenType.RIGHT_BRACKET or
            TokenType.RIGHT_BRACE => true,

            // Postfix operators - expression ended
            TokenType.PLUS_PLUS or
            TokenType.MINUS_MINUS => true,

            // Everything else (operators, keywords, opening brackets) expects an expression
            _ => false
        };
    }
}
