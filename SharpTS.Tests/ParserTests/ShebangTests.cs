using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public class ShebangTests
{
    private const string Hashbang = "#!/usr/bin/env sharpts";

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Lexer_HashbangPreservesFollowingTokenLocation(string lineTerminator)
    {
        string source = Hashbang + lineTerminator + "const answer = 42;";

        var tokens = new Lexer(source).ScanTokens();

        Assert.Equal(TokenType.CONST, tokens[0].Type);
        Assert.Equal(2, tokens[0].Line);
        Assert.Equal(Hashbang.Length + lineTerminator.Length, tokens[0].Start);
    }

    [Fact]
    public void Lexer_FinalHashbangWithoutNewlineIsAccepted()
    {
        var tokens = new Lexer(Hashbang).ScanTokens();

        Assert.Single(tokens);
        Assert.Equal(TokenType.EOF, tokens[0].Type);
        Assert.Equal(1, tokens[0].Line);
    }

    [Theory]
    [InlineData(" #!/usr/bin/env sharpts\nconst answer = 42;")]
    [InlineData("\uFEFF#!/usr/bin/env sharpts\nconst answer = 42;")]
    [InlineData("\n#!/usr/bin/env sharpts\nconst answer = 42;")]
    [InlineData("const before = 1;\n#!/usr/bin/env sharpts")]
    public void Lexer_HashbangOutsideOffsetZeroIsRejected(string source)
    {
        var exception = Assert.Throws<Exception>(() => new Lexer(source).ScanTokens());

        Assert.Contains("Unexpected character '#'", exception.Message);
    }

    [Fact]
    public void Lexer_PrivateIdentifierStillLexesNormally()
    {
        var tokens = new Lexer("class Box { #value = 1; }").ScanTokens();

        var privateIdentifier = Assert.Single(tokens, token => token.Type == TokenType.PRIVATE_IDENTIFIER);
        Assert.Equal("#value", privateIdentifier.Lexeme);
    }

    [Fact]
    public void Parser_AcceptsProgramAfterHashbang()
    {
        const string source = "#!/usr/bin/env sharpts\nconsole.log(42);";

        var result = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Statements);
    }

    [Fact]
    public void Parser_DiagnosticAfterHashbangReportsSecondLine()
    {
        const string source = "#!/usr/bin/env sharpts\nconst answer = ;";

        var result = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.False(result.IsSuccess);
        Assert.Equal(2, Assert.Single(result.Diagnostics).Line);
    }
}
