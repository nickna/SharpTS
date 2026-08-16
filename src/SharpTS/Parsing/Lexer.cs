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
    // Line where the current token starts. Snapshotted before each ScanToken so
    // that multi-line tokens (template literals) record their starting line, not
    // their ending line. Needed so `return <template>` on the same line doesn't
    // spuriously trigger ASI via Parser.HasLineTerminatorBeforeCurrent.
    private int _tokenStartLine = 1;
    // Stack to track brace depth when inside template interpolations
    private readonly Stack<int> _templateBraceDepth = new();
    // Tracks whether we're expecting an expression (true) or operator (false)
    // Used to disambiguate regex literals from division operator
    private bool _expectExpr = true;

    /// <summary>
    /// Lenient mode for .tsx sources (and JSX suffix re-lexing): characters that are invalid
    /// in TypeScript but legal inside JSX text (a bare '#') are dropped instead of throwing,
    /// so the upfront pass survives to reach the parser's source-driven JSX text scanning.
    /// </summary>
    public bool JsxTolerant { get; init; } = false;

    // Triple-slash directive support
    private readonly List<TripleSlashDirective> _tripleSlashDirectives = [];
    // Track when we've emitted a code token (directives only valid before code)
    private bool _hasEmittedCodeToken = false;

    // TypeScript pragma directives (// @ts-check / @ts-nocheck / @ts-ignore / @ts-expect-error)
    private bool _hasTsCheck;
    private bool _hasTsNoCheck;
    private readonly HashSet<int> _tsIgnoreLines = [];
    private readonly HashSet<int> _tsExpectErrorLines = [];

    // JSX pragmas (/** @jsx h */, @jsxFrag, @jsxImportSource, @jsxRuntime), honored only
    // before the first code token — same discipline as @ts-check.
    private string? _jsxFactoryPragma;
    private string? _jsxFragmentPragma;
    private string? _jsxImportSourcePragma;
    private string? _jsxRuntimePragma;

    /// <summary>
    /// Triple-slash directives parsed from the source file.
    /// Only populated for directives that appear before any code.
    /// </summary>
    public IReadOnlyList<TripleSlashDirective> TripleSlashDirectives => _tripleSlashDirectives;

    /// <summary>
    /// TypeScript pragma directives discovered in `//` comments.
    /// File-level pragmas (`@ts-check`, `@ts-nocheck`) are only honored before the first code token.
    /// Line-level pragmas (`@ts-ignore`, `@ts-expect-error`) record the comment's line number.
    /// </summary>
    public TypeScriptPragmas Pragmas =>
        new(_hasTsCheck, _hasTsNoCheck, _tsIgnoreLines, _tsExpectErrorLines,
            _jsxFactoryPragma, _jsxFragmentPragma, _jsxImportSourcePragma, _jsxRuntimePragma);

    /// <summary>
    /// Every reserved word the lexer recognizes, for REPL autocomplete.
    /// </summary>
    internal static IEnumerable<string> KeywordNames => Keywords.Keys;

    /// <summary>Maps a keyword's spelling to its token type, or null if it is not a keyword.</summary>
    internal static TokenType? KeywordTokenType(string name) =>
        Keywords.TryGetValue(name, out var type) ? type : null;

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "abstract", TokenType.ABSTRACT },
        { "accessor", TokenType.ACCESSOR },
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
        { "global", TokenType.GLOBAL },
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
        { "module", TokenType.MODULE },
        { "namespace", TokenType.NAMESPACE },
        { "never", TokenType.NEVER },
        { "new", TokenType.NEW },
        { "null", TokenType.NULL },
        { "of", TokenType.OF },
        { "out", TokenType.OUT },
        { "override", TokenType.OVERRIDE },
        { "private", TokenType.PRIVATE },
        { "protected", TokenType.PROTECTED },
        { "public", TokenType.PUBLIC },
        { "readonly", TokenType.READONLY },
        { "return", TokenType.RETURN },
        { "satisfies", TokenType.SATISFIES },
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
        { "using", TokenType.USING },
        { "void", TokenType.VOID },
        { "var", TokenType.VAR },
        { "delete", TokenType.DELETE },
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
            _tokenStartLine = _line;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.EOF, "", null, _line));
        return _tokens;
    }

    /// <summary>Suffix lexer for JSX token-stream repair. See <see cref="Relex"/>.</summary>
    private Lexer(string source, int startOffset, int startLine, int templateInterpolationDepth) : this(source)
    {
        _start = startOffset;
        _current = startOffset;
        _line = startLine;
        _tokenStartLine = startLine;
        // A suffix re-lex is never at the top of the file: keep triple-slash directive and
        // file-level pragma collection off.
        _hasEmittedCodeToken = true;
        // Every JSX repair point follows a completed value (closing quote, '>', '}'), so a
        // '/' as the first re-lexed character is JSX punctuation (`/>`), never a regex start.
        _expectExpr = false;
        // When the re-lex starts inside template interpolations, seed the brace stack so a
        // closing '}' resumes template scanning (TEMPLATE_MIDDLE/TAIL) instead of lexing
        // the template tail as ordinary code.
        for (int i = 0; i < templateInterpolationDepth; i++)
            _templateBraceDepth.Push(0);
    }

    /// <summary>
    /// Re-lexes <paramref name="source"/> from <paramref name="fromOffset"/> with fresh default
    /// lexer state, for the parser's JSX token-stream repair (the upfront pass applies TS
    /// string/comment rules inside JSX text and can mis-lex everything after it).
    /// Tokens carry absolute <see cref="Token.Start"/> offsets. Each token is paired with
    /// whether the lexer was in neutral state after producing it (no open template
    /// interpolation) — the parser only treats a token as a safe splice convergence point when
    /// the original stream could agree with a fresh-state lex from there onward.
    /// </summary>
    internal static List<(Token Token, bool NeutralAfter)> Relex(
        string source, int fromOffset, int startLine, int templateInterpolationDepth = 0)
    {
        var lexer = new Lexer(source, fromOffset, startLine, templateInterpolationDepth) { JsxTolerant = true };
        var result = new List<(Token, bool)>();
        while (!lexer.IsAtEnd())
        {
            lexer._start = lexer._current;
            lexer._tokenStartLine = lexer._line;
            int before = lexer._tokens.Count;
            lexer.ScanToken();
            bool neutral = lexer._templateBraceDepth.Count == 0;
            for (int i = before; i < lexer._tokens.Count; i++)
                result.Add((lexer._tokens[i], neutral));
        }
        result.Add((new Token(TokenType.EOF, "", null, lexer._line, source.Length), true));
        return result;
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
                else if (char.IsDigit(Peek()))
                {
                    // ECMA-262 NumericLiteral: a leading `.digit` is a valid
                    // DecimalLiteral (e.g. `.5` === `0.5`). Consume fractional
                    // digits + optional exponent inline; the resulting token
                    // text starts with '.' so double.Parse handles it.
                    while (char.IsDigit(Peek())) Advance();
                    if (Peek() == 'e' || Peek() == 'E')
                    {
                        Advance();
                        if (Peek() == '+' || Peek() == '-') Advance();
                        if (!char.IsDigit(Peek()))
                            throw new Exception($"Invalid number: expected digit after exponent at line {_line}");
                        while (char.IsDigit(Peek())) Advance();
                    }
                    AddToken(TokenType.NUMBER, double.Parse(_source[_start.._current]));
                }
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
            case '#':
                if (_start == 0 && Peek() == '!')
                {
                    SkipHashbang();
                }
                else if (char.IsLetter(Peek()) || Peek() == '_' || Peek() == '$')
                {
                    PrivateIdentifier();
                }
                else if (JsxTolerant)
                {
                    // A stray '#' in a .tsx file usually sits inside JSX text, which the
                    // parser rescans from source; dropping it here (like other unknown
                    // characters) lets the upfront pass survive to reach the parser.
                }
                else
                {
                    throw new Exception($"Unexpected character '#' at line {_line}");
                }
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
                    // Check for triple-slash directive (/// <reference ...)
                    if (Peek() == '/' && !_hasEmittedCodeToken)
                    {
                        Advance(); // consume third slash
                        if (TryParseTripleSlashDirective())
                        {
                            break; // Successfully parsed directive
                        }
                        // Not a valid directive syntax, continue as regular comment
                    }
                    // Line comment - capture text and skip to end of line.
                    // Scan it for TypeScript pragmas (@ts-check, @ts-nocheck,
                    // @ts-ignore, @ts-expect-error) so the type checker can
                    // honor tsc-style directives.
                    int commentStart = _current;
                    int commentLine = _line;
                    while (Peek() != '\n' && !IsAtEnd()) Advance();
                    ScanForTsPragma(commentStart, _current, commentLine);
                    ScanForJsxPragmas(commentStart, _current);
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
                else if (LooksLikeJsxClosingTag())
                {
                    AddToken(TokenType.SLASH);
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
                else if (char.IsLetter(c) || c == '_' || c == '$')
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

    private bool LooksLikeJsxClosingTag()
    {
        if (_tokens.Count == 0 || _tokens[^1].Type != TokenType.LESS)
            return false;

        int i = _current;
        if (i < _source.Length && _source[i] == '>')
            return true; // fragment: </>
        if (i >= _source.Length ||
            !(char.IsLetter(_source[i]) || _source[i] is '_' or '$'))
            return false;

        while (i < _source.Length &&
               (char.IsLetterOrDigit(_source[i]) || _source[i] is '_' or '$' or '-' or '.'))
        {
            i++;
        }
        return i < _source.Length && _source[i] == '>';
    }

    private void Identifier()
    {
        while (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '$') Advance();

        string text = _source[_start.._current];
        if (!Keywords.TryGetValue(text, out TokenType type))
        {
            type = TokenType.IDENTIFIER;
        }
        AddToken(type);
    }

    /// <summary>
    /// Scans a private identifier starting with # (e.g., #fieldName, #method).
    /// </summary>
    private void PrivateIdentifier()
    {
        // _start already points to '#', consume identifier chars
        while (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '$') Advance();
        AddToken(TokenType.PRIVATE_IDENTIFIER);
    }

    /// <summary>
    /// Skips a Unix hashbang at the very beginning of a source file. The leading '#'
    /// has already been consumed; consume the rest of the line as trivia, including
    /// its terminator, so the next token keeps its original offset and line number.
    /// </summary>
    private void SkipHashbang()
    {
        Advance(); // consume '!'
        while (!IsAtEnd() && Peek() != '\r' && Peek() != '\n') Advance();

        if (Match('\r'))
        {
            Match('\n');
            _line++;
        }
        else if (Match('\n'))
        {
            _line++;
        }
    }

    private void NumberLiteral()
    {
        // Check for hex (0x), binary (0b), or octal (0o) literals
        if (_source[_start] == '0' && _current < _source.Length)
        {
            char next = Peek();
            if (next == 'x' || next == 'X')
            {
                HexLiteral();
                return;
            }
            else if (next == 'b' || next == 'B')
            {
                BinaryLiteral();
                return;
            }
            else if (next == 'o' || next == 'O')
            {
                OctalLiteral();
                return;
            }
            // Check for legacy octal literals (0-prefixed numbers like 0777)
            // TypeScript does not support legacy octals - always reject them
            else if (next >= '0' && next <= '7')
            {
                throw new Exception($"SyntaxError: Legacy octal literals are not allowed. Use '0o' prefix for octal numbers at line {_line}");
            }
        }

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

        // ECMA-262 NumericLiteral grammar:
        //   DecimalIntegerLiteral . DecimalDigits[opt] ExponentPart[opt]
        // Both `1.5` and `1.` (trailing dot, no fractional digits) are valid.
        // Only consume the `.` when it can't be a property accessor —
        // i.e. NOT followed by an identifier char (so `(0).toFixed(2)` keeps
        // dot as DOT). Digit / underscore / e / E / EOF / non-ident punct after
        // dot all bind to the number; identifier letters bind as property.
        if (Peek() == '.' && !char.IsLetter(PeekNext()) && PeekNext() != '_'
            || Peek() == '.' && (char.IsDigit(PeekNext()) || PeekNext() == '_'))
        {
            // Heuristic: if next char after `.` is a letter (a-z/A-Z), treat
            // dot as property accessor (e.g. `(123).toFixed`). Otherwise it's
            // a fractional separator. Check fractional-digit case explicitly
            // so the underscore-after-dot path remains the same.
            char afterDot = PeekNext();
            bool hasFractional = char.IsDigit(afterDot) || afterDot == '_';

            Advance(); // consume '.'

            if (hasFractional)
            {
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
            // else: trailing-dot form (`123.`) — dot already consumed, no
            // fractional digits, fall through to ExponentPart check.
        }

        // Scientific notation: e/E followed by optional +/- and digits
        if (Peek() == 'e' || Peek() == 'E')
        {
            Advance(); // consume e/E
            if (Peek() == '+' || Peek() == '-') Advance(); // optional sign
            if (!char.IsDigit(Peek()))
                throw new Exception($"Invalid number: expected digit after exponent at line {_line}");
            while (char.IsDigit(Peek())) Advance();
        }

        string numberStr = _source[_start.._current].Replace("_", "");
        AddToken(TokenType.NUMBER, double.Parse(numberStr));
    }

    /// <summary>
    /// Parses a hexadecimal literal (0x... or 0X...)
    /// </summary>
    private void HexLiteral()
    {
        Advance(); // consume 'x' or 'X'

        if (!IsHexDigit(Peek()))
        {
            throw new Exception($"Invalid hexadecimal literal at line {_line}");
        }

        // Consume hex digits and numeric separators
        while (IsHexDigit(Peek()) || Peek() == '_')
        {
            if (Peek() == '_')
            {
                char prev = _source[_current - 1];
                char next = PeekNext();
                if (!IsHexDigit(prev) || !IsHexDigit(next))
                {
                    throw new Exception($"Numeric separator must be between digits at line {_line}");
                }
            }
            Advance();
        }

        // Check for bigint suffix
        if (Peek() == 'n')
        {
            string hexStr = _source[(_start + 2).._current].Replace("_", "");
            Advance(); // consume 'n'
            // NumberStyles.HexNumber interprets a leading high bit as a signed
            // two's-complement value. JavaScript hexadecimal BigInt literals
            // are unsigned magnitudes, so prefix a zero nibble.
            AddToken(TokenType.BIGINT_LITERAL, BigInteger.Parse(
                "0" + hexStr, System.Globalization.NumberStyles.HexNumber));
            return;
        }

        string numStr = _source[(_start + 2).._current].Replace("_", "");
        long value = Convert.ToInt64(numStr, 16);
        AddToken(TokenType.NUMBER, (double)value);
    }

    /// <summary>
    /// Parses a binary literal (0b... or 0B...)
    /// </summary>
    private void BinaryLiteral()
    {
        Advance(); // consume 'b' or 'B'

        if (!IsBinaryDigit(Peek()))
        {
            throw new Exception($"Invalid binary literal at line {_line}");
        }

        // Consume binary digits and numeric separators
        while (IsBinaryDigit(Peek()) || Peek() == '_')
        {
            if (Peek() == '_')
            {
                char prev = _source[_current - 1];
                char next = PeekNext();
                if (!IsBinaryDigit(prev) || !IsBinaryDigit(next))
                {
                    throw new Exception($"Numeric separator must be between digits at line {_line}");
                }
            }
            Advance();
        }

        // Check for bigint suffix
        if (Peek() == 'n')
        {
            string binStr = _source[(_start + 2).._current].Replace("_", "");
            Advance(); // consume 'n'
            AddToken(TokenType.BIGINT_LITERAL, BinaryStringToBigInteger(binStr));
            return;
        }

        string numStr = _source[(_start + 2).._current].Replace("_", "");
        long value = Convert.ToInt64(numStr, 2);
        AddToken(TokenType.NUMBER, (double)value);
    }

    /// <summary>
    /// Parses an octal literal (0o... or 0O...)
    /// </summary>
    private void OctalLiteral()
    {
        Advance(); // consume 'o' or 'O'

        if (!IsOctalDigit(Peek()))
        {
            throw new Exception($"Invalid octal literal at line {_line}");
        }

        // Consume octal digits and numeric separators
        while (IsOctalDigit(Peek()) || Peek() == '_')
        {
            if (Peek() == '_')
            {
                char prev = _source[_current - 1];
                char next = PeekNext();
                if (!IsOctalDigit(prev) || !IsOctalDigit(next))
                {
                    throw new Exception($"Numeric separator must be between digits at line {_line}");
                }
            }
            Advance();
        }

        // Check for bigint suffix
        if (Peek() == 'n')
        {
            string octStr = _source[(_start + 2).._current].Replace("_", "");
            Advance(); // consume 'n'
            AddToken(TokenType.BIGINT_LITERAL, OctalStringToBigInteger(octStr));
            return;
        }

        string numStr = _source[(_start + 2).._current].Replace("_", "");
        long value = Convert.ToInt64(numStr, 8);
        AddToken(TokenType.NUMBER, (double)value);
    }

    private static bool IsOctalDigit(char c)
    {
        return c >= '0' && c <= '7';
    }

    private static bool IsBinaryDigit(char c)
    {
        return c == '0' || c == '1';
    }

    private static BigInteger BinaryStringToBigInteger(string binary)
    {
        BigInteger result = 0;
        foreach (char c in binary)
        {
            result = result * 2 + (c - '0');
        }
        return result;
    }

    private static BigInteger OctalStringToBigInteger(string octal)
    {
        BigInteger result = 0;
        foreach (char c in octal)
        {
            result = result * 8 + (c - '0');
        }
        return result;
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
                    switch (escaped)
                    {
                        case '\r':
                            // A CRLF sequence is a single line continuation.
                            if (Peek() == '\n') Advance();
                            _line++;
                            break;
                        case '\n':
                            _line++;
                            break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        case '0':
                            // \0 is allowed when not followed by another digit (null character)
                            // \00 through \07 are octal escapes and not allowed
                            if (Peek() >= '0' && Peek() <= '7')
                            {
                                throw new Exception($"SyntaxError: Octal escape sequences are not allowed in strict mode at line {_line}");
                            }
                            sb.Append('\0');
                            break;
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                            // \1 through \7 are octal escapes - not allowed
                            throw new Exception($"SyntaxError: Octal escape sequences are not allowed in strict mode at line {_line}");
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'v': sb.Append('\v'); break;
                        case 'u':
                            // Unicode escape: \uXXXX (4 hex digits) or \u{XXXX} (1-6 hex digits)
                            if (Peek() == '{')
                            {
                                Advance(); // consume '{'
                                var hex = new System.Text.StringBuilder();
                                while (Peek() != '}' && !IsAtEnd() && hex.Length < 6)
                                {
                                    if (IsHexDigit(Peek()))
                                        hex.Append(Advance());
                                    else
                                        break;
                                }
                                if (Peek() == '}') Advance(); // consume '}'
                                if (hex.Length > 0 && int.TryParse(hex.ToString(), System.Globalization.NumberStyles.HexNumber, null, out int codePoint))
                                {
                                    sb.Append(char.ConvertFromUtf32(codePoint));
                                }
                            }
                            else
                            {
                                // \uXXXX - exactly 4 hex digits
                                var hex = new System.Text.StringBuilder();
                                for (int i = 0; i < 4 && !IsAtEnd() && IsHexDigit(Peek()); i++)
                                {
                                    hex.Append(Advance());
                                }
                                if (hex.Length == 4 && int.TryParse(hex.ToString(), System.Globalization.NumberStyles.HexNumber, null, out int codePoint))
                                {
                                    sb.Append((char)codePoint);
                                }
                                else
                                {
                                    // Invalid escape, keep original
                                    sb.Append('u');
                                    sb.Append(hex);
                                }
                            }
                            break;
                        case 'x':
                            // Hex escape: \xXX (2 hex digits)
                            {
                                var hex = new System.Text.StringBuilder();
                                for (int i = 0; i < 2 && !IsAtEnd() && IsHexDigit(Peek()); i++)
                                {
                                    hex.Append(Advance());
                                }
                                if (hex.Length == 2 && int.TryParse(hex.ToString(), System.Globalization.NumberStyles.HexNumber, null, out int value))
                                {
                                    sb.Append((char)value);
                                }
                                else
                                {
                                    // Invalid escape, keep original
                                    sb.Append('x');
                                    sb.Append(hex);
                                }
                            }
                            break;
                        default:
                            sb.Append(escaped); // unrecognized escape, keep as-is
                            break;
                    }
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
        int bodyStart = _current;
        while (!IsAtEnd())
        {
            if (Peek() == '*' && PeekNext() == '/')
            {
                ScanForJsxPragmas(bodyStart, _current);
                Advance(); // consume *
                Advance(); // consume /
                return;
            }
            if (Peek() == '\n') _line++;
            Advance();
        }
        throw new Exception($"Unterminated block comment at line {_line}");
    }

    /// <summary>
    /// Scans a comment body for JSX pragmas (<c>@jsx</c>, <c>@jsxFrag</c>,
    /// <c>@jsxImportSource</c>, <c>@jsxRuntime</c>), each of which takes a value. Honored only
    /// before the first code token; tsc conventionally uses JSDoc block comments but line
    /// comments are accepted too (matching tsc's lenient pragma regex).
    /// </summary>
    private void ScanForJsxPragmas(int bodyStart, int bodyEndExclusive)
    {
        if (_hasEmittedCodeToken) return;
        for (int i = bodyStart; i < bodyEndExclusive; i++)
        {
            if (_source[i] != '@') continue;
            int cursor = i + 1;
            // Longest names first — "@jsx" is a prefix of the others.
            if (TryReadJsxPragma(ref cursor, bodyEndExclusive, "jsxImportSource", out var value))
                _jsxImportSourcePragma = value;
            else if (TryReadJsxPragma(ref cursor, bodyEndExclusive, "jsxRuntime", out value))
                _jsxRuntimePragma = value;
            else if (TryReadJsxPragma(ref cursor, bodyEndExclusive, "jsxFrag", out value))
                _jsxFragmentPragma = value;
            else if (TryReadJsxPragma(ref cursor, bodyEndExclusive, "jsx", out value))
                _jsxFactoryPragma = value;
            else
                continue;
            i = cursor;
        }
    }

    /// <summary>
    /// Matches a pragma name at <paramref name="cursor"/> (case-insensitively — tsc lowercases
    /// pragma names, so <c>@jsxfrag</c> is as valid as <c>@jsxFrag</c>) and reads its
    /// whitespace-separated value.
    /// </summary>
    private bool TryReadJsxPragma(ref int cursor, int endExclusive, string name, out string? value)
    {
        value = null;
        if (cursor + name.Length > endExclusive) return false;
        for (int k = 0; k < name.Length; k++)
            if (char.ToLowerInvariant(_source[cursor + k]) != char.ToLowerInvariant(name[k])) return false;

        int position = cursor + name.Length;
        // Name must end at whitespace ("@jsxes" is not "@jsx").
        if (position < endExclusive && _source[position] is not (' ' or '\t' or '\r' or '\n'))
            return false;
        while (position < endExclusive && _source[position] is ' ' or '\t' or '\r' or '\n')
            position++;
        int valueStart = position;
        while (position < endExclusive && !char.IsWhiteSpace(_source[position]) && _source[position] != '*')
            position++;
        if (position == valueStart) return false;

        value = _source[valueStart..position];
        cursor = position;
        return true;
    }

    /// <summary>
    /// Attempts to parse a triple-slash directive after consuming "///".
    /// Returns true if a valid directive was parsed, false otherwise.
    /// On failure, the comment should be consumed as a regular line comment.
    /// </summary>
    private bool TryParseTripleSlashDirective()
    {
        int startLine = _line;
        int startColumn = _start + 1; // 1-based column

        // Skip whitespace after ///
        while (Peek() == ' ' || Peek() == '\t') Advance();

        // Check for '<reference'
        if (Peek() != '<') return false;
        Advance(); // consume '<'

        // Skip whitespace after '<'
        while (Peek() == ' ' || Peek() == '\t') Advance();

        // Check for 'reference'
        if (!MatchKeyword("reference")) return false;

        // Skip whitespace after 'reference'
        while (Peek() == ' ' || Peek() == '\t') Advance();

        // Parse attribute: path="value" or types="value" or lib="value" or no-default-lib="true"
        TripleSlashReferenceType? refType = null;
        string? refValue = null;

        if (MatchKeyword("path"))
        {
            refType = TripleSlashReferenceType.Path;
        }
        else if (MatchKeyword("types"))
        {
            refType = TripleSlashReferenceType.Types;
        }
        else if (MatchKeyword("lib"))
        {
            refType = TripleSlashReferenceType.Lib;
        }
        else if (MatchKeyword("no-default-lib"))
        {
            refType = TripleSlashReferenceType.NoDefaultLib;
        }
        else
        {
            // Unknown attribute - error
            throw new Exception($"Type Error at line {_line}: Invalid triple-slash directive. Expected 'path', 'types', 'lib', or 'no-default-lib' attribute.");
        }

        // Skip whitespace after attribute name
        while (Peek() == ' ' || Peek() == '\t') Advance();

        // Expect '='
        if (Peek() != '=')
        {
            throw new Exception($"Type Error at line {_line}: Invalid triple-slash directive. Expected '=' after attribute name.");
        }
        Advance(); // consume '='

        // Skip whitespace after '='
        while (Peek() == ' ' || Peek() == '\t') Advance();

        // Parse quoted value
        char quote = Peek();
        if (quote != '"' && quote != '\'')
        {
            throw new Exception($"Type Error at line {_line}: Invalid triple-slash directive. Expected quoted value after '='.");
        }
        Advance(); // consume opening quote

        var valueBuilder = new System.Text.StringBuilder();
        while (!IsAtEnd() && Peek() != quote && Peek() != '\n')
        {
            valueBuilder.Append(Advance());
        }

        if (Peek() != quote)
        {
            throw new Exception($"Type Error at line {_line}: Invalid triple-slash directive. Unterminated string value.");
        }
        Advance(); // consume closing quote

        refValue = valueBuilder.ToString();

        // Skip whitespace before '/>'
        while (Peek() == ' ' || Peek() == '\t') Advance();

        // Expect '/>' to close the directive
        if (Peek() != '/')
        {
            throw new Exception($"Type Error at line {_line}: Invalid triple-slash directive. Expected '/>' to close directive.");
        }
        Advance(); // consume '/'

        if (Peek() != '>')
        {
            throw new Exception($"Type Error at line {_line}: Invalid triple-slash directive. Expected '>' after '/'.");
        }
        Advance(); // consume '>'

        // Skip any remaining content on the line (trailing whitespace/comments)
        while (Peek() != '\n' && !IsAtEnd()) Advance();

        // Successfully parsed - add to directives list
        _tripleSlashDirectives.Add(new TripleSlashDirective(refType.Value, refValue, startLine, startColumn));
        return true;
    }

    /// <summary>
    /// Attempts to match a specific keyword at the current position.
    /// Returns true and advances past the keyword if matched, false otherwise.
    /// </summary>
    private bool MatchKeyword(string keyword)
    {
        for (int i = 0; i < keyword.Length; i++)
        {
            if (_current + i >= _source.Length || _source[_current + i] != keyword[i])
            {
                return false;
            }
        }

        // Make sure the keyword isn't part of a larger identifier
        int afterKeyword = _current + keyword.Length;
        if (afterKeyword < _source.Length)
        {
            char next = _source[afterKeyword];
            if (char.IsLetterOrDigit(next) || next == '_' || next == '-')
            {
                // For "no-default-lib", the '-' is part of the keyword, so check carefully
                if (keyword != "no-default-lib")
                {
                    return false;
                }
            }
        }

        _current += keyword.Length;
        return true;
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

        // Scan flags (g, i, m, s, u, y, d, v)
        var flags = new System.Text.StringBuilder();
        while (!IsAtEnd() && IsRegexFlag(Peek()))
        {
            flags.Append(Advance());
        }

        AddToken(TokenType.REGEX, new RegexLiteralValue(pattern.ToString(), flags.ToString()));
    }

    // ECMA-262 22.2.5.2 Properties of RegExp instances — accept all eight
    // currently-defined flag characters at lex time. d (hasIndices, ES2022)
    // and v (unicodeSets, ES2024) join the original six. Runtime semantics
    // for v are partial — flag is accepted and threaded through, character-
    // class extensions aren't implemented — but rejecting it at parse time
    // blocked the entire CharacterClassEscapes generated test cluster.
    private static bool IsRegexFlag(char c) => c is 'g' or 'i' or 'm' or 's' or 'u' or 'y' or 'd' or 'v';

    private static bool IsHexDigit(char c) => char.IsAsciiHexDigit(c);

    private void TemplateLiteral()
    {
        var (cooked, raw) = ProcessTemplateSegment();

        if (Peek() == '`')
        {
            Advance(); // consume closing `
            AddToken(TokenType.TEMPLATE_FULL, new TemplateStringValue(cooked, raw));
        }
        else if (Peek() == '$' && PeekNext() == '{')
        {
            Advance(); // consume $
            Advance(); // consume {
            _templateBraceDepth.Push(0);
            AddToken(TokenType.TEMPLATE_HEAD, new TemplateStringValue(cooked, raw));
        }
    }

    private void ContinueTemplateLiteral()
    {
        _start = _current;
        _tokenStartLine = _line;
        var (cooked, raw) = ProcessTemplateSegment();

        if (Peek() == '`')
        {
            Advance(); // consume closing `
            AddToken(TokenType.TEMPLATE_TAIL, new TemplateStringValue(cooked, raw));
        }
        else if (Peek() == '$' && PeekNext() == '{')
        {
            Advance(); // consume $
            Advance(); // consume {
            _templateBraceDepth.Push(0);
            AddToken(TokenType.TEMPLATE_MIDDLE, new TemplateStringValue(cooked, raw));
        }
    }

    /// <summary>
    /// Process a template string segment, returning (cooked, raw) strings.
    /// Cooked is null if any invalid escape sequence was encountered (ES2018).
    /// </summary>
    private (string? Cooked, string Raw) ProcessTemplateSegment()
    {
        var raw = new System.Text.StringBuilder();
        var cooked = new System.Text.StringBuilder();
        bool hasInvalidEscape = false;

        while (!IsAtEnd() && Peek() != '`' && !(Peek() == '$' && PeekNext() == '{'))
        {
            // Template source characters normalize CR and CRLF to LF in both
            // cooked and raw strings (ECMA-262 TV/TRV semantics).
            if (Peek() == '\r')
            {
                Advance();
                if (!IsAtEnd() && Peek() == '\n') Advance();
                raw.Append('\n');
                cooked.Append('\n');
                _line++;
                continue;
            }
            if (Peek() == '\n') _line++;

            if (Peek() == '\\' && !IsAtEnd())
            {
                // Check if this is \${ - if so, don't consume the $ as an escape,
                // let it be processed as interpolation. The backslash becomes a literal.
                if (PeekNext() == '$' && _current + 2 < _source.Length && _source[_current + 2] == '{')
                {
                    // Just add the backslash literally and let ${...} be interpolation
                    char backslash = Advance();
                    raw.Append(backslash);
                    cooked.Append(backslash);
                }
                else
                {
                    raw.Append(Advance()); // consume backslash, add to raw

                    if (!IsAtEnd())
                    {
                        char next = Peek();

                        // A line continuation contributes no cooked character,
                        // while its raw line terminator is normalized to LF.
                        if (next is '\r' or '\n')
                        {
                            Advance();
                            if (next == '\r' && !IsAtEnd() && Peek() == '\n') Advance();
                            raw.Append('\n');
                            _line++;
                        }
                        // \uXXXX or \u{XXXXXX} — Unicode escape
                        else if (next == 'u')
                        {
                            raw.Append(Advance()); // consume 'u'
                            var unicode = ProcessUnicodeEscape(raw);
                            if (unicode == null) hasInvalidEscape = true;
                            else cooked.Append(unicode);
                        }
                        // \xXX — hex byte escape
                        else if (next == 'x')
                        {
                            raw.Append(Advance()); // consume 'x'
                            var hex = ProcessHexEscape(raw);
                            if (hex == null) hasInvalidEscape = true;
                            else cooked.Append(hex);
                        }
                        else
                        {
                            raw.Append(Advance()); // add escaped char to raw

                            // Process escape for cooked string
                            var processed = ProcessTemplateEscape(next);
                            if (processed == null)
                            {
                                hasInvalidEscape = true;
                            }
                            else
                            {
                                cooked.Append(processed);
                            }
                        }
                    }
                }
            }
            else
            {
                char c = Advance();
                raw.Append(c);
                cooked.Append(c);
            }
        }

        return (hasInvalidEscape ? null : cooked.ToString(), raw.ToString());
    }

    /// <summary>
    /// Process a single escape sequence character for template literals.
    /// Returns null for invalid escapes (ES2018 tagged template revision).
    /// </summary>
    private static string? ProcessTemplateEscape(char escaped)
    {
        return escaped switch
        {
            'n' => "\n",
            't' => "\t",
            'r' => "\r",
            '\\' => "\\",
            '`' => "`",
            '$' => "$",
            '0' => "\0",
            'b' => "\b",
            'f' => "\f",
            'v' => "\v",
            '\n' => "", // line continuation
            '\r' => "", // line continuation
            // Digits \1-\9 are invalid in template literals (NotEscapeSequence)
            _ when char.IsDigit(escaped) && escaped != '0' => null,
            // Any other char (including letters) is an identity escape per spec:
            // the backslash is dropped and the char appears as-is in cooked.
            _ => escaped.ToString()
        };
    }

    /// <summary>
    /// Process \uXXXX or \u{X...} Unicode escape after the lexer has consumed
    /// backslash and 'u'. Appends consumed chars to <paramref name="raw"/>.
    /// Returns the decoded string (may be a surrogate pair) or null if malformed.
    /// </summary>
    private string? ProcessUnicodeEscape(System.Text.StringBuilder raw)
    {
        // \u{HEX...} — ES6 code point escape
        if (!IsAtEnd() && Peek() == '{')
        {
            raw.Append(Advance()); // {
            int value = 0;
            int digits = 0;
            while (!IsAtEnd() && Peek() != '}')
            {
                char d = Peek();
                int dv = HexDigitValue(d);
                if (dv < 0) return null;
                value = (value << 4) | dv;
                if (value > 0x10FFFF) return null;
                raw.Append(Advance());
                digits++;
            }
            if (digits == 0 || IsAtEnd() || Peek() != '}') return null;
            raw.Append(Advance()); // }
            return char.ConvertFromUtf32(value);
        }

        // \uXXXX — four hex digits
        if (_current + 3 >= _source.Length) return null;
        int cp = 0;
        for (int i = 0; i < 4; i++)
        {
            char d = Peek();
            int dv = HexDigitValue(d);
            if (dv < 0) return null;
            cp = (cp << 4) | dv;
            raw.Append(Advance());
        }
        return ((char)cp).ToString();
    }

    /// <summary>
    /// Process \xHH hex byte escape after lexer has consumed backslash and 'x'.
    /// </summary>
    private string? ProcessHexEscape(System.Text.StringBuilder raw)
    {
        if (_current + 1 >= _source.Length) return null;
        int cp = 0;
        for (int i = 0; i < 2; i++)
        {
            char d = Peek();
            int dv = HexDigitValue(d);
            if (dv < 0) return null;
            cp = (cp << 4) | dv;
            raw.Append(Advance());
        }
        return ((char)cp).ToString();
    }

    private static int HexDigitValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
        if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
        return -1;
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
        _tokens.Add(new Token(type, text, literal, _tokenStartLine, _start));
        // Update expression state for regex literal disambiguation
        _expectExpr = !IsExpressionEnd(type);
        // Mark that we've emitted a code token (triple-slash directives no longer valid)
        _hasEmittedCodeToken = true;
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
            TokenType.PRIVATE_IDENTIFIER or
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

    /// <summary>
    /// Scans a `//` comment body for TypeScript pragma directives. Recognized:
    /// <c>// @ts-check</c>, <c>// @ts-nocheck</c>, <c>// @ts-ignore</c>, <c>// @ts-expect-error</c>.
    /// File-level pragmas (check/nocheck) are only honored before the first emitted code token,
    /// matching tsc's documented semantics. Tolerates leading whitespace inside the comment.
    /// </summary>
    private void ScanForTsPragma(int bodyStart, int bodyEndExclusive, int commentLine)
    {
        // Skip leading whitespace inside the comment body.
        int i = bodyStart;
        while (i < bodyEndExclusive && (_source[i] == ' ' || _source[i] == '\t'))
            i++;
        if (i >= bodyEndExclusive || _source[i] != '@')
            return;
        i++; // consume @

        // Match the pragma name. We accept the directive when followed by EOL or whitespace.
        if (MatchesPragma(i, bodyEndExclusive, "ts-check"))
        {
            if (!_hasEmittedCodeToken) _hasTsCheck = true;
        }
        else if (MatchesPragma(i, bodyEndExclusive, "ts-nocheck"))
        {
            if (!_hasEmittedCodeToken) _hasTsNoCheck = true;
        }
        else if (MatchesPragma(i, bodyEndExclusive, "ts-ignore"))
        {
            _tsIgnoreLines.Add(commentLine);
        }
        else if (MatchesPragma(i, bodyEndExclusive, "ts-expect-error"))
        {
            _tsExpectErrorLines.Add(commentLine);
        }
    }

    private bool MatchesPragma(int start, int endExclusive, string name)
    {
        if (start + name.Length > endExclusive) return false;
        for (int k = 0; k < name.Length; k++)
            if (_source[start + k] != name[k]) return false;
        // Directive must end at EOL or whitespace — `@ts-check-ignore` shouldn't match `ts-check`.
        int after = start + name.Length;
        if (after >= endExclusive) return true;
        char c = _source[after];
        return c == ' ' || c == '\t' || c == '\r';
    }
}
