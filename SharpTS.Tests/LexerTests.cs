using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests;

/// <summary>
/// Unit tests for the Lexer - validates token production for TypeScript source code.
/// </summary>
public class LexerTests
{
    #region Helpers

    private static List<Token> Tokenize(string source)
    {
        var lexer = new Lexer(source);
        return lexer.ScanTokens();
    }

    /// <summary>
    /// Asserts that the source produces exactly the specified tokens (plus EOF).
    /// </summary>
    private static void AssertTokens(string source, params TokenType[] expected)
    {
        var tokens = Tokenize(source);
        Assert.Equal(expected.Length + 1, tokens.Count); // +1 for EOF
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], tokens[i].Type);
        }
        Assert.Equal(TokenType.EOF, tokens[^1].Type);
    }

    #endregion

    #region Single-Character Tokens

    [Fact]
    public void SingleChar_LeftParen() => AssertTokens("(", TokenType.LEFT_PAREN);

    [Fact]
    public void SingleChar_RightParen() => AssertTokens(")", TokenType.RIGHT_PAREN);

    [Fact]
    public void SingleChar_LeftBrace() => AssertTokens("{", TokenType.LEFT_BRACE);

    [Fact]
    public void SingleChar_RightBrace() => AssertTokens("}", TokenType.RIGHT_BRACE);

    [Fact]
    public void SingleChar_LeftBracket() => AssertTokens("[", TokenType.LEFT_BRACKET);

    [Fact]
    public void SingleChar_RightBracket() => AssertTokens("]", TokenType.RIGHT_BRACKET);

    [Fact]
    public void SingleChar_Comma() => AssertTokens(",", TokenType.COMMA);

    [Fact]
    public void SingleChar_Dot() => AssertTokens(".", TokenType.DOT);

    [Fact]
    public void SingleChar_Semicolon() => AssertTokens(";", TokenType.SEMICOLON);

    [Fact]
    public void SingleChar_Colon() => AssertTokens(":", TokenType.COLON);

    [Fact]
    public void SingleChar_At() => AssertTokens("@", TokenType.AT);

    [Fact]
    public void SingleChar_Tilde() => AssertTokens("~", TokenType.TILDE);

    #endregion

    #region Arithmetic Operators

    [Fact]
    public void Operator_Plus() => AssertTokens("+", TokenType.PLUS);

    [Fact]
    public void Operator_Minus() => AssertTokens("-", TokenType.MINUS);

    [Fact]
    public void Operator_Star() => AssertTokens("*", TokenType.STAR);

    [Fact]
    public void Operator_Slash_AfterOperator()
    {
        // After an operator, / is division
        var tokens = Tokenize("5 / 2");
        Assert.Equal(TokenType.NUMBER, tokens[0].Type);
        Assert.Equal(TokenType.SLASH, tokens[1].Type);
        Assert.Equal(TokenType.NUMBER, tokens[2].Type);
    }

    [Fact]
    public void Operator_Percent() => AssertTokens("%", TokenType.PERCENT);

    [Fact]
    public void Operator_StarStar_Exponentiation() => AssertTokens("**", TokenType.STAR_STAR);

    #endregion

    #region Comparison Operators

    [Fact]
    public void Operator_Less() => AssertTokens("<", TokenType.LESS);

    [Fact]
    public void Operator_LessEqual() => AssertTokens("<=", TokenType.LESS_EQUAL);

    [Fact]
    public void Operator_Greater() => AssertTokens(">", TokenType.GREATER);

    [Fact]
    public void Operator_GreaterEqual() => AssertTokens(">=", TokenType.GREATER_EQUAL);

    [Fact]
    public void Operator_EqualEqual() => AssertTokens("==", TokenType.EQUAL_EQUAL);

    [Fact]
    public void Operator_EqualEqualEqual() => AssertTokens("===", TokenType.EQUAL_EQUAL_EQUAL);

    [Fact]
    public void Operator_BangEqual() => AssertTokens("!=", TokenType.BANG_EQUAL);

    [Fact]
    public void Operator_BangEqualEqual() => AssertTokens("!==", TokenType.BANG_EQUAL_EQUAL);

    #endregion

    #region Logical Operators

    [Fact]
    public void Operator_Bang() => AssertTokens("!", TokenType.BANG);

    [Fact]
    public void Operator_AndAnd() => AssertTokens("&&", TokenType.AND_AND);

    [Fact]
    public void Operator_OrOr() => AssertTokens("||", TokenType.OR_OR);

    [Fact]
    public void Operator_QuestionQuestion_NullishCoalescing() => AssertTokens("??", TokenType.QUESTION_QUESTION);

    [Fact]
    public void Operator_QuestionDot_OptionalChaining() => AssertTokens("?.", TokenType.QUESTION_DOT);

    [Fact]
    public void Operator_Question_Ternary() => AssertTokens("?", TokenType.QUESTION);

    #endregion

    #region Bitwise Operators

    [Fact]
    public void Operator_Ampersand() => AssertTokens("&", TokenType.AMPERSAND);

    [Fact]
    public void Operator_Pipe() => AssertTokens("|", TokenType.PIPE);

    [Fact]
    public void Operator_Caret() => AssertTokens("^", TokenType.CARET);

    [Fact]
    public void Operator_LessLess_LeftShift() => AssertTokens("<<", TokenType.LESS_LESS);

    [Fact]
    public void Operator_GreaterGreater_RightShift() => AssertTokens(">>", TokenType.GREATER_GREATER);

    [Fact]
    public void Operator_GreaterGreaterGreater_UnsignedRightShift() => AssertTokens(">>>", TokenType.GREATER_GREATER_GREATER);

    #endregion

    #region Assignment Operators

    [Fact]
    public void Operator_Equal() => AssertTokens("=", TokenType.EQUAL);

    [Fact]
    public void Operator_PlusEqual() => AssertTokens("+=", TokenType.PLUS_EQUAL);

    [Fact]
    public void Operator_MinusEqual() => AssertTokens("-=", TokenType.MINUS_EQUAL);

    [Fact]
    public void Operator_StarEqual() => AssertTokens("*=", TokenType.STAR_EQUAL);

    [Fact]
    public void Operator_SlashEqual() => AssertTokens("/=", TokenType.SLASH_EQUAL);

    [Fact]
    public void Operator_PercentEqual() => AssertTokens("%=", TokenType.PERCENT_EQUAL);

    [Fact]
    public void Operator_AmpersandEqual() => AssertTokens("&=", TokenType.AMPERSAND_EQUAL);

    [Fact]
    public void Operator_PipeEqual() => AssertTokens("|=", TokenType.PIPE_EQUAL);

    [Fact]
    public void Operator_CaretEqual() => AssertTokens("^=", TokenType.CARET_EQUAL);

    [Fact]
    public void Operator_LessLessEqual() => AssertTokens("<<=", TokenType.LESS_LESS_EQUAL);

    [Fact]
    public void Operator_GreaterGreaterEqual() => AssertTokens(">>=", TokenType.GREATER_GREATER_EQUAL);

    [Fact]
    public void Operator_GreaterGreaterGreaterEqual() => AssertTokens(">>>=", TokenType.GREATER_GREATER_GREATER_EQUAL);

    #endregion

    #region Increment/Decrement and Other Operators

    [Fact]
    public void Operator_PlusPlus() => AssertTokens("++", TokenType.PLUS_PLUS);

    [Fact]
    public void Operator_MinusMinus() => AssertTokens("--", TokenType.MINUS_MINUS);

    [Fact]
    public void Operator_Arrow() => AssertTokens("=>", TokenType.ARROW);

    [Fact]
    public void Operator_Spread() => AssertTokens("...", TokenType.DOT_DOT_DOT);

    #endregion

    #region Keywords - Control Flow

    [Fact]
    public void Keyword_If() => AssertTokens("if", TokenType.IF);

    [Fact]
    public void Keyword_Else() => AssertTokens("else", TokenType.ELSE);

    [Fact]
    public void Keyword_For() => AssertTokens("for", TokenType.FOR);

    [Fact]
    public void Keyword_While() => AssertTokens("while", TokenType.WHILE);

    [Fact]
    public void Keyword_Do() => AssertTokens("do", TokenType.DO);

    [Fact]
    public void Keyword_Switch() => AssertTokens("switch", TokenType.SWITCH);

    [Fact]
    public void Keyword_Case() => AssertTokens("case", TokenType.CASE);

    [Fact]
    public void Keyword_Default() => AssertTokens("default", TokenType.DEFAULT);

    [Fact]
    public void Keyword_Break() => AssertTokens("break", TokenType.BREAK);

    [Fact]
    public void Keyword_Continue() => AssertTokens("continue", TokenType.CONTINUE);

    [Fact]
    public void Keyword_Return() => AssertTokens("return", TokenType.RETURN);

    [Fact]
    public void Keyword_Throw() => AssertTokens("throw", TokenType.THROW);

    [Fact]
    public void Keyword_Try() => AssertTokens("try", TokenType.TRY);

    [Fact]
    public void Keyword_Catch() => AssertTokens("catch", TokenType.CATCH);

    [Fact]
    public void Keyword_Finally() => AssertTokens("finally", TokenType.FINALLY);

    #endregion

    #region Keywords - Type Related

    [Fact]
    public void Keyword_TypeString() => AssertTokens("string", TokenType.TYPE_STRING);

    [Fact]
    public void Keyword_TypeNumber() => AssertTokens("number", TokenType.TYPE_NUMBER);

    [Fact]
    public void Keyword_TypeBoolean() => AssertTokens("boolean", TokenType.TYPE_BOOLEAN);

    [Fact]
    public void Keyword_TypeSymbol() => AssertTokens("symbol", TokenType.TYPE_SYMBOL);

    [Fact]
    public void Keyword_TypeBigint() => AssertTokens("bigint", TokenType.TYPE_BIGINT);

    [Fact]
    public void Keyword_Typeof() => AssertTokens("typeof", TokenType.TYPEOF);

    [Fact]
    public void Keyword_Instanceof() => AssertTokens("instanceof", TokenType.INSTANCEOF);

    [Fact]
    public void Keyword_Keyof() => AssertTokens("keyof", TokenType.KEYOF);

    [Fact]
    public void Keyword_Type() => AssertTokens("type", TokenType.TYPE);

    [Fact]
    public void Keyword_Interface() => AssertTokens("interface", TokenType.INTERFACE);

    #endregion

    #region Keywords - Class Related

    [Fact]
    public void Keyword_Class() => AssertTokens("class", TokenType.CLASS);

    [Fact]
    public void Keyword_Constructor() => AssertTokens("constructor", TokenType.CONSTRUCTOR);

    [Fact]
    public void Keyword_Extends() => AssertTokens("extends", TokenType.EXTENDS);

    [Fact]
    public void Keyword_Implements() => AssertTokens("implements", TokenType.IMPLEMENTS);

    [Fact]
    public void Keyword_Abstract() => AssertTokens("abstract", TokenType.ABSTRACT);

    [Fact]
    public void Keyword_Override() => AssertTokens("override", TokenType.OVERRIDE);

    [Fact]
    public void Keyword_New() => AssertTokens("new", TokenType.NEW);

    [Fact]
    public void Keyword_Super() => AssertTokens("super", TokenType.SUPER);

    [Fact]
    public void Keyword_This() => AssertTokens("this", TokenType.THIS);

    #endregion

    #region Keywords - Access Modifiers

    [Fact]
    public void Keyword_Public() => AssertTokens("public", TokenType.PUBLIC);

    [Fact]
    public void Keyword_Private() => AssertTokens("private", TokenType.PRIVATE);

    [Fact]
    public void Keyword_Protected() => AssertTokens("protected", TokenType.PROTECTED);

    [Fact]
    public void Keyword_Static() => AssertTokens("static", TokenType.STATIC);

    [Fact]
    public void Keyword_Readonly() => AssertTokens("readonly", TokenType.READONLY);

    [Fact]
    public void Keyword_Get() => AssertTokens("get", TokenType.GET);

    [Fact]
    public void Keyword_Set() => AssertTokens("set", TokenType.SET);

    #endregion

    #region Keywords - Variables and Functions

    [Fact]
    public void Keyword_Const() => AssertTokens("const", TokenType.CONST);

    [Fact]
    public void Keyword_Let() => AssertTokens("let", TokenType.LET);

    [Fact]
    public void Keyword_Var_IsIdentifier()
    {
        // Note: SharpTS treats "var" as an identifier, not a keyword
        // (prefers let/const for variable declarations)
        var tokens = Tokenize("var");
        Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
        Assert.Equal("var", tokens[0].Lexeme);
    }

    [Fact]
    public void Keyword_Function() => AssertTokens("function", TokenType.FUNCTION);

    [Fact]
    public void Keyword_Async() => AssertTokens("async", TokenType.ASYNC);

    [Fact]
    public void Keyword_Await() => AssertTokens("await", TokenType.AWAIT);

    [Fact]
    public void Keyword_Yield() => AssertTokens("yield", TokenType.YIELD);

    #endregion

    #region Keywords - Modules

    [Fact]
    public void Keyword_Import() => AssertTokens("import", TokenType.IMPORT);

    [Fact]
    public void Keyword_Export() => AssertTokens("export", TokenType.EXPORT);

    [Fact]
    public void Keyword_From() => AssertTokens("from", TokenType.FROM);

    [Fact]
    public void Keyword_As() => AssertTokens("as", TokenType.AS);

    [Fact]
    public void Keyword_Namespace() => AssertTokens("namespace", TokenType.NAMESPACE);

    [Fact]
    public void Keyword_Declare() => AssertTokens("declare", TokenType.DECLARE);

    #endregion

    #region Keywords - Literals and Special Values

    [Fact]
    public void Keyword_True() => AssertTokens("true", TokenType.TRUE);

    [Fact]
    public void Keyword_False() => AssertTokens("false", TokenType.FALSE);

    [Fact]
    public void Keyword_Null() => AssertTokens("null", TokenType.NULL);

    [Fact]
    public void Keyword_Undefined() => AssertTokens("undefined", TokenType.UNDEFINED);

    [Fact]
    public void Keyword_In() => AssertTokens("in", TokenType.IN);

    [Fact]
    public void Keyword_Of() => AssertTokens("of", TokenType.OF);

    [Fact]
    public void Keyword_Enum() => AssertTokens("enum", TokenType.ENUM);

    #endregion

    #region Special Constructors

    [Fact]
    public void Constructor_Symbol() => AssertTokens("Symbol", TokenType.SYMBOL);

    [Fact]
    public void Constructor_BigInt() => AssertTokens("BigInt", TokenType.BIGINT);

    #endregion

    #region Identifiers

    [Fact]
    public void Identifier_Simple()
    {
        var tokens = Tokenize("foo");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
        Assert.Equal("foo", tokens[0].Lexeme);
    }

    [Fact]
    public void Identifier_WithUnderscore()
    {
        var tokens = Tokenize("_privateVar");
        Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
        Assert.Equal("_privateVar", tokens[0].Lexeme);
    }

    [Fact]
    public void Identifier_WithNumbers()
    {
        var tokens = Tokenize("var123");
        Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
        Assert.Equal("var123", tokens[0].Lexeme);
    }

    [Fact]
    public void Identifier_CamelCase()
    {
        var tokens = Tokenize("myVariableName");
        Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
        Assert.Equal("myVariableName", tokens[0].Lexeme);
    }

    [Fact]
    public void Identifier_NotKeyword_CaseSensitive()
    {
        // Keywords are case-sensitive; "IF" is an identifier, not a keyword
        var tokens = Tokenize("IF");
        Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
    }

    #endregion

    #region String Literals

    [Fact]
    public void String_DoubleQuotes()
    {
        var tokens = Tokenize("\"hello\"");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.STRING, tokens[0].Type);
        Assert.Equal("hello", tokens[0].Literal);
    }

    [Fact]
    public void String_SingleQuotes()
    {
        var tokens = Tokenize("'hello'");
        Assert.Equal(TokenType.STRING, tokens[0].Type);
        Assert.Equal("hello", tokens[0].Literal);
    }

    [Fact]
    public void String_Empty()
    {
        var tokens = Tokenize("\"\"");
        Assert.Equal(TokenType.STRING, tokens[0].Type);
        Assert.Equal("", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_Newline()
    {
        var tokens = Tokenize("\"hello\\nworld\"");
        Assert.Equal(TokenType.STRING, tokens[0].Type);
        Assert.Equal("hello\nworld", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_Tab()
    {
        var tokens = Tokenize("\"hello\\tworld\"");
        Assert.Equal(TokenType.STRING, tokens[0].Type);
        Assert.Equal("hello\tworld", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_CarriageReturn()
    {
        var tokens = Tokenize("\"line1\\rline2\"");
        Assert.Equal("line1\rline2", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_Backslash()
    {
        var tokens = Tokenize("\"path\\\\to\\\\file\"");
        Assert.Equal("path\\to\\file", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_DoubleQuote()
    {
        var tokens = Tokenize("\"say \\\"hello\\\"\"");
        Assert.Equal("say \"hello\"", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_SingleQuote()
    {
        var tokens = Tokenize("'it\\'s'");
        Assert.Equal("it's", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_NullChar()
    {
        var tokens = Tokenize("\"null\\0char\"");
        Assert.Equal("null\0char", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_Backspace()
    {
        var tokens = Tokenize("\"back\\bspace\"");
        Assert.Equal("back\bspace", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_FormFeed()
    {
        var tokens = Tokenize("\"form\\ffeed\"");
        Assert.Equal("form\ffeed", tokens[0].Literal);
    }

    [Fact]
    public void String_Escape_VerticalTab()
    {
        var tokens = Tokenize("\"vertical\\vtab\"");
        Assert.Equal("vertical\vtab", tokens[0].Literal);
    }

    [Fact]
    public void String_MultipleEscapes()
    {
        var tokens = Tokenize("\"line1\\nline2\\ttabbed\"");
        Assert.Equal("line1\nline2\ttabbed", tokens[0].Literal);
    }

    #endregion

    #region Template Literals

    [Fact]
    public void Template_Simple()
    {
        var tokens = Tokenize("`hello world`");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.TEMPLATE_FULL, tokens[0].Type);
        var templateValue = Assert.IsType<TemplateStringValue>(tokens[0].Literal);
        Assert.Equal("hello world", templateValue.Cooked);
    }

    [Fact]
    public void Template_Empty()
    {
        var tokens = Tokenize("``");
        Assert.Equal(TokenType.TEMPLATE_FULL, tokens[0].Type);
        var templateValue = Assert.IsType<TemplateStringValue>(tokens[0].Literal);
        Assert.Equal("", templateValue.Cooked);
    }

    [Fact]
    public void Template_SingleInterpolation()
    {
        var tokens = Tokenize("`hello ${name}`");
        // Note: The closing } of an interpolation does not produce a RIGHT_BRACE token
        // It's consumed as part of the template parsing
        Assert.Equal(4, tokens.Count); // HEAD, IDENTIFIER, TAIL, EOF
        Assert.Equal(TokenType.TEMPLATE_HEAD, tokens[0].Type);
        var headValue = Assert.IsType<TemplateStringValue>(tokens[0].Literal);
        Assert.Equal("hello ", headValue.Cooked);
        Assert.Equal(TokenType.IDENTIFIER, tokens[1].Type);
        Assert.Equal("name", tokens[1].Lexeme);
        Assert.Equal(TokenType.TEMPLATE_TAIL, tokens[2].Type);
        var tailValue = Assert.IsType<TemplateStringValue>(tokens[2].Literal);
        Assert.Equal("", tailValue.Cooked);
    }

    [Fact]
    public void Template_MultipleInterpolations()
    {
        var tokens = Tokenize("`${a}${b}${c}`");
        // HEAD(""), a, MIDDLE(""), b, MIDDLE(""), c, TAIL(""), EOF
        // Note: closing } does not produce tokens
        Assert.Equal(TokenType.TEMPLATE_HEAD, tokens[0].Type);
        Assert.Equal(TokenType.IDENTIFIER, tokens[1].Type);
        Assert.Equal("a", tokens[1].Lexeme);
        Assert.Equal(TokenType.TEMPLATE_MIDDLE, tokens[2].Type);
        Assert.Equal(TokenType.IDENTIFIER, tokens[3].Type);
        Assert.Equal("b", tokens[3].Lexeme);
        Assert.Equal(TokenType.TEMPLATE_MIDDLE, tokens[4].Type);
        Assert.Equal(TokenType.IDENTIFIER, tokens[5].Type);
        Assert.Equal("c", tokens[5].Lexeme);
        Assert.Equal(TokenType.TEMPLATE_TAIL, tokens[6].Type);
    }

    [Fact]
    public void Template_TextBetweenInterpolations()
    {
        var tokens = Tokenize("`Hello ${name}, you are ${age} years old`");
        // HEAD, name, MIDDLE, age, TAIL, EOF (no } tokens)
        Assert.Equal(TokenType.TEMPLATE_HEAD, tokens[0].Type);
        var headValue = Assert.IsType<TemplateStringValue>(tokens[0].Literal);
        Assert.Equal("Hello ", headValue.Cooked);
        Assert.Equal(TokenType.IDENTIFIER, tokens[1].Type);
        Assert.Equal("name", tokens[1].Lexeme);
        Assert.Equal(TokenType.TEMPLATE_MIDDLE, tokens[2].Type);
        var middleValue = Assert.IsType<TemplateStringValue>(tokens[2].Literal);
        Assert.Equal(", you are ", middleValue.Cooked);
        Assert.Equal(TokenType.IDENTIFIER, tokens[3].Type);
        Assert.Equal("age", tokens[3].Lexeme);
        Assert.Equal(TokenType.TEMPLATE_TAIL, tokens[4].Type);
        var tailValue = Assert.IsType<TemplateStringValue>(tokens[4].Literal);
        Assert.Equal(" years old", tailValue.Cooked);
    }

    [Fact]
    public void Template_ExpressionInInterpolation()
    {
        var tokens = Tokenize("`result: ${1 + 2}`");
        // HEAD, NUMBER, PLUS, NUMBER, TAIL, EOF (no } token)
        Assert.Equal(TokenType.TEMPLATE_HEAD, tokens[0].Type);
        Assert.Equal(TokenType.NUMBER, tokens[1].Type);
        Assert.Equal(TokenType.PLUS, tokens[2].Type);
        Assert.Equal(TokenType.NUMBER, tokens[3].Type);
        Assert.Equal(TokenType.TEMPLATE_TAIL, tokens[4].Type);
    }

    [Fact]
    public void Template_ObjectInInterpolation()
    {
        var tokens = Tokenize("`${obj[key]}`");
        // HEAD, obj, [, key, ], TAIL, EOF (no } token for interpolation close)
        Assert.Equal(TokenType.TEMPLATE_HEAD, tokens[0].Type);
        Assert.Equal(TokenType.IDENTIFIER, tokens[1].Type);
        Assert.Equal("obj", tokens[1].Lexeme);
        Assert.Equal(TokenType.LEFT_BRACKET, tokens[2].Type);
        Assert.Equal(TokenType.IDENTIFIER, tokens[3].Type);
        Assert.Equal("key", tokens[3].Lexeme);
        Assert.Equal(TokenType.RIGHT_BRACKET, tokens[4].Type);
        Assert.Equal(TokenType.TEMPLATE_TAIL, tokens[5].Type);
    }

    [Fact]
    public void Template_MultiLine()
    {
        var source = "`line1\nline2\nline3`";
        var tokens = Tokenize(source);
        Assert.Equal(TokenType.TEMPLATE_FULL, tokens[0].Type);
        var templateValue = Assert.IsType<TemplateStringValue>(tokens[0].Literal);
        Assert.Equal("line1\nline2\nline3", templateValue.Cooked);
    }

    [Fact]
    public void Template_Multiline_LineNumberTracking()
    {
        var source = "`line1\nline2`\nlet x = 5";
        var tokens = Tokenize(source);
        // Note: The lexer reports the line where the token ENDS, not where it starts
        // Template ends on line 2 (after processing the \n inside)
        Assert.Equal(2, tokens[0].Line); // Template ends on line 2
        Assert.Equal(3, tokens[1].Line); // 'let' is on line 3
    }

    #endregion

    #region Number Literals

    [Fact]
    public void Number_Integer()
    {
        var tokens = Tokenize("123");
        Assert.Equal(TokenType.NUMBER, tokens[0].Type);
        Assert.Equal(123.0, tokens[0].Literal);
    }

    [Fact]
    public void Number_Zero()
    {
        var tokens = Tokenize("0");
        Assert.Equal(TokenType.NUMBER, tokens[0].Type);
        Assert.Equal(0.0, tokens[0].Literal);
    }

    [Fact]
    public void Number_Float()
    {
        var tokens = Tokenize("3.14");
        Assert.Equal(TokenType.NUMBER, tokens[0].Type);
        Assert.Equal(3.14, tokens[0].Literal);
    }

    [Fact]
    public void Number_FloatStartingWithZero()
    {
        var tokens = Tokenize("0.5");
        Assert.Equal(TokenType.NUMBER, tokens[0].Type);
        Assert.Equal(0.5, tokens[0].Literal);
    }

    [Fact]
    public void Number_Large()
    {
        var tokens = Tokenize("1000000");
        Assert.Equal(TokenType.NUMBER, tokens[0].Type);
        Assert.Equal(1000000.0, tokens[0].Literal);
    }

    [Fact]
    public void Number_BigInt()
    {
        var tokens = Tokenize("123n");
        Assert.Equal(TokenType.BIGINT_LITERAL, tokens[0].Type);
        Assert.Equal(new System.Numerics.BigInteger(123), tokens[0].Literal);
    }

    [Fact]
    public void Number_BigInt_Large()
    {
        var tokens = Tokenize("9999999999999999999n");
        Assert.Equal(TokenType.BIGINT_LITERAL, tokens[0].Type);
        Assert.Equal(System.Numerics.BigInteger.Parse("9999999999999999999"), tokens[0].Literal);
    }

    [Fact]
    public void Number_FollowedByIdentifier()
    {
        var tokens = Tokenize("123abc");
        Assert.Equal(3, tokens.Count); // NUMBER, IDENTIFIER, EOF
        Assert.Equal(TokenType.NUMBER, tokens[0].Type);
        Assert.Equal(123.0, tokens[0].Literal);
        Assert.Equal(TokenType.IDENTIFIER, tokens[1].Type);
        Assert.Equal("abc", tokens[1].Lexeme);
    }

    [Fact]
    public void Number_FollowedByOperator()
    {
        var tokens = Tokenize("5+3");
        Assert.Equal(4, tokens.Count);
        Assert.Equal(TokenType.NUMBER, tokens[0].Type);
        Assert.Equal(TokenType.PLUS, tokens[1].Type);
        Assert.Equal(TokenType.NUMBER, tokens[2].Type);
    }

    #endregion

    #region Regex Literals

    [Fact]
    public void Regex_Simple()
    {
        // After =, / starts a regex
        var tokens = Tokenize("let x = /pattern/");
        var regexToken = tokens.First(t => t.Type == TokenType.REGEX);
        Assert.NotNull(regexToken);
        var value = (RegexLiteralValue)regexToken.Literal!;
        Assert.Equal("pattern", value.Pattern);
        Assert.Equal("", value.Flags);
    }

    [Fact]
    public void Regex_WithFlags()
    {
        var tokens = Tokenize("let x = /pattern/gi");
        var regexToken = tokens.First(t => t.Type == TokenType.REGEX);
        var value = (RegexLiteralValue)regexToken.Literal!;
        Assert.Equal("pattern", value.Pattern);
        Assert.Equal("gi", value.Flags);
    }

    [Fact]
    public void Regex_WithEscapes()
    {
        var tokens = Tokenize("let x = /\\d+/");
        var regexToken = tokens.First(t => t.Type == TokenType.REGEX);
        var value = (RegexLiteralValue)regexToken.Literal!;
        Assert.Equal("\\d+", value.Pattern);
    }

    [Fact]
    public void Regex_WithCharacterClass()
    {
        var tokens = Tokenize("let x = /[a-z]/");
        var regexToken = tokens.First(t => t.Type == TokenType.REGEX);
        var value = (RegexLiteralValue)regexToken.Literal!;
        Assert.Equal("[a-z]", value.Pattern);
    }

    [Fact]
    public void Regex_WithNegatedCharacterClass()
    {
        var tokens = Tokenize("let x = /[^0-9]/");
        var regexToken = tokens.First(t => t.Type == TokenType.REGEX);
        var value = (RegexLiteralValue)regexToken.Literal!;
        Assert.Equal("[^0-9]", value.Pattern);
    }

    [Fact]
    public void Regex_SlashInCharacterClass()
    {
        // / inside [] should not end the regex
        var tokens = Tokenize("let x = /[/]/");
        var regexToken = tokens.First(t => t.Type == TokenType.REGEX);
        var value = (RegexLiteralValue)regexToken.Literal!;
        Assert.Equal("[/]", value.Pattern);
    }

    [Fact]
    public void Regex_VsDivision_AfterNumber()
    {
        // After a number, / is division, not regex
        var tokens = Tokenize("5 / 2");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.REGEX);
        Assert.Contains(tokens, t => t.Type == TokenType.SLASH);
    }

    [Fact]
    public void Regex_VsDivision_AfterIdentifier()
    {
        // After an identifier, / is division
        var tokens = Tokenize("x / y");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.REGEX);
        Assert.Contains(tokens, t => t.Type == TokenType.SLASH);
    }

    [Fact]
    public void Regex_AfterReturn()
    {
        // After 'return', / starts a regex
        var tokens = Tokenize("return /pattern/");
        var regexToken = tokens.FirstOrDefault(t => t.Type == TokenType.REGEX);
        Assert.NotNull(regexToken);
    }

    [Fact]
    public void Regex_AfterOpenParen()
    {
        // After '(', / starts a regex
        var tokens = Tokenize("(/pattern/)");
        var regexToken = tokens.FirstOrDefault(t => t.Type == TokenType.REGEX);
        Assert.NotNull(regexToken);
    }

    #endregion

    #region Comments

    [Fact]
    public void Comment_LineComment_NoTokenProduced()
    {
        var tokens = Tokenize("// this is a comment");
        Assert.Single(tokens); // Only EOF
        Assert.Equal(TokenType.EOF, tokens[0].Type);
    }

    [Fact]
    public void Comment_LineComment_BeforeCode()
    {
        var tokens = Tokenize("// comment\nlet x = 5");
        Assert.DoesNotContain(tokens, t => t.Lexeme == "comment");
        Assert.Contains(tokens, t => t.Type == TokenType.LET);
    }

    [Fact]
    public void Comment_LineComment_AfterCode()
    {
        var tokens = Tokenize("let x = 5 // comment");
        Assert.DoesNotContain(tokens, t => t.Lexeme == "comment");
        Assert.Contains(tokens, t => t.Type == TokenType.LET);
        Assert.Contains(tokens, t => t.Type == TokenType.NUMBER);
    }

    [Fact]
    public void Comment_BlockComment_NoTokenProduced()
    {
        var tokens = Tokenize("/* block comment */");
        Assert.Single(tokens); // Only EOF
    }

    [Fact]
    public void Comment_BlockComment_Inline()
    {
        var tokens = Tokenize("let /* inline */ x = 5");
        Assert.DoesNotContain(tokens, t => t.Lexeme == "inline");
        Assert.Contains(tokens, t => t.Type == TokenType.LET);
        Assert.Contains(tokens, t => t.Type == TokenType.IDENTIFIER);
    }

    [Fact]
    public void Comment_BlockComment_MultiLine()
    {
        var source = """
            let x = 1
            /* multi
            line
            comment */
            let y = 2
            """;
        var tokens = Tokenize(source);
        Assert.DoesNotContain(tokens, t => t.Lexeme?.Contains("multi") == true);
        // Find both let tokens
        var letTokens = tokens.Where(t => t.Type == TokenType.LET).ToList();
        Assert.Equal(2, letTokens.Count);
    }

    [Fact]
    public void Comment_LineNumber_MaintainedAfterBlockComment()
    {
        var source = "/*\ncomment\n*/\nlet x = 5";
        var tokens = Tokenize(source);
        var letToken = tokens.First(t => t.Type == TokenType.LET);
        Assert.Equal(4, letToken.Line);
    }

    [Fact]
    public void Comment_LineNumber_MaintainedAfterLineComment()
    {
        var source = "// comment\nlet x = 5";
        var tokens = Tokenize(source);
        var letToken = tokens.First(t => t.Type == TokenType.LET);
        Assert.Equal(2, letToken.Line);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void EdgeCase_EmptySource()
    {
        var tokens = Tokenize("");
        Assert.Single(tokens);
        Assert.Equal(TokenType.EOF, tokens[0].Type);
    }

    [Fact]
    public void EdgeCase_WhitespaceOnly()
    {
        var tokens = Tokenize("   \t\n\r   ");
        Assert.Single(tokens);
        Assert.Equal(TokenType.EOF, tokens[0].Type);
    }

    [Fact]
    public void EdgeCase_LineNumbersIncrement()
    {
        var source = "a\nb\nc";
        var tokens = Tokenize(source);
        Assert.Equal(1, tokens[0].Line);
        Assert.Equal(2, tokens[1].Line);
        Assert.Equal(3, tokens[2].Line);
    }

    [Fact]
    public void EdgeCase_ConsecutiveOperators()
    {
        var tokens = Tokenize("a+++b");
        // Should be: a, ++, +, b, EOF
        Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
        Assert.Equal(TokenType.PLUS_PLUS, tokens[1].Type);
        Assert.Equal(TokenType.PLUS, tokens[2].Type);
        Assert.Equal(TokenType.IDENTIFIER, tokens[3].Type);
    }

    [Fact]
    public void EdgeCase_MultipleStatements()
    {
        var tokens = Tokenize("let x = 5; let y = 10;");
        var letTokens = tokens.Where(t => t.Type == TokenType.LET).ToList();
        Assert.Equal(2, letTokens.Count);
        var semicolons = tokens.Where(t => t.Type == TokenType.SEMICOLON).ToList();
        Assert.Equal(2, semicolons.Count);
    }

    [Fact]
    public void EdgeCase_MixedContent()
    {
        var source = """
            let name: string = "test";
            const PI = 3.14;
            function add(a: number, b: number): number {
                return a + b;
            }
            """;
        var tokens = Tokenize(source);
        // Verify we got all the major token types
        Assert.Contains(tokens, t => t.Type == TokenType.LET);
        Assert.Contains(tokens, t => t.Type == TokenType.CONST);
        Assert.Contains(tokens, t => t.Type == TokenType.FUNCTION);
        Assert.Contains(tokens, t => t.Type == TokenType.STRING);
        Assert.Contains(tokens, t => t.Type == TokenType.NUMBER);
        Assert.Contains(tokens, t => t.Type == TokenType.TYPE_STRING);
        Assert.Contains(tokens, t => t.Type == TokenType.TYPE_NUMBER);
        Assert.Contains(tokens, t => t.Type == TokenType.RETURN);
    }

    [Fact]
    public void EdgeCase_CarriageReturnHandling()
    {
        var tokens = Tokenize("a\r\nb");
        Assert.Equal(1, tokens[0].Line);
        Assert.Equal(2, tokens[1].Line);
    }

    [Fact]
    public void EdgeCase_TabHandling()
    {
        var tokens = Tokenize("\tlet\tx\t=\t5");
        Assert.Contains(tokens, t => t.Type == TokenType.LET);
        Assert.Contains(tokens, t => t.Type == TokenType.IDENTIFIER);
        Assert.Contains(tokens, t => t.Type == TokenType.EQUAL);
        Assert.Contains(tokens, t => t.Type == TokenType.NUMBER);
    }

    #endregion
}
