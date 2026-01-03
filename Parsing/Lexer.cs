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

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "as", TokenType.AS },
        { "break", TokenType.BREAK },
        { "case", TokenType.CASE },
        { "class", TokenType.CLASS },
        { "const", TokenType.CONST },
        { "constructor", TokenType.CONSTRUCTOR },
        { "continue", TokenType.CONTINUE },
        { "default", TokenType.DEFAULT },
        { "do", TokenType.DO },
        { "else", TokenType.ELSE },
        { "enum", TokenType.ENUM },
        { "extends", TokenType.EXTENDS },
        { "false", TokenType.FALSE },
        { "function", TokenType.FUNCTION },
        { "for", TokenType.FOR },
        { "get", TokenType.GET },
        { "if", TokenType.IF },
        { "implements", TokenType.IMPLEMENTS },
        { "in", TokenType.IN },
        { "instanceof", TokenType.INSTANCEOF },
        { "interface", TokenType.INTERFACE },
        { "let", TokenType.LET },
        { "never", TokenType.NEVER },
        { "new", TokenType.NEW },
        { "null", TokenType.NULL },
        { "of", TokenType.OF },
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
        { "unknown", TokenType.UNKNOWN },
        { "while", TokenType.WHILE },
        { "string", TokenType.TYPE_STRING },
        { "number", TokenType.TYPE_NUMBER },
        { "boolean", TokenType.TYPE_BOOLEAN }
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
                if (Match('?')) AddToken(TokenType.QUESTION_QUESTION);
                else if (Match('.')) AddToken(TokenType.QUESTION_DOT);
                else AddToken(TokenType.QUESTION);
                break;
            case '&':
                if (Match('&')) AddToken(TokenType.AND_AND);
                else if (Match('=')) AddToken(TokenType.AMPERSAND_EQUAL);
                else AddToken(TokenType.AMPERSAND);
                break;
            case '|':
                if (Match('|')) AddToken(TokenType.OR_OR);
                else if (Match('=')) AddToken(TokenType.PIPE_EQUAL);
                else AddToken(TokenType.PIPE);
                break;
            case '^':
                AddToken(Match('=') ? TokenType.CARET_EQUAL : TokenType.CARET);
                break;
            case '~':
                AddToken(TokenType.TILDE);
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
        while (char.IsDigit(Peek())) Advance();

        if (Peek() == '.' && char.IsDigit(PeekNext()))
        {
            Advance();
            while (char.IsDigit(Peek())) Advance();
        }

        AddToken(TokenType.NUMBER, double.Parse(_source[_start.._current]));
    }

    private void StringLiteral(char delimiter)
    {
        while (Peek() != delimiter && !IsAtEnd())
        {
            if (Peek() == '\n') _line++;
            Advance();
        }

        if (IsAtEnd()) return;

        Advance(); // The closing delimiter
        string value = _source[(_start + 1)..(_current - 1)];
        AddToken(TokenType.STRING, value);
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
    }
}
