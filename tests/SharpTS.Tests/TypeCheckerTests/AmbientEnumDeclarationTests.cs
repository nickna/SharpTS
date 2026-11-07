using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class AmbientEnumDeclarationTests
{
    [Theory]
    [InlineData("enum", false, false)]
    [InlineData("const enum", false, true)]
    [InlineData("export enum", true, false)]
    [InlineData("export const enum", true, true)]
    public void ParsesAmbientModuleEnum(string prefix, bool exported, bool constant)
    {
        var tokens = new Lexer($"declare module \"fixture\" {{ {prefix} Value {{ Known }} }}").ScanTokens();
        var result = new Parser(tokens, DecoratorMode.None).Parse();
        Assert.Empty(result.Errors);
        var module = Assert.IsType<Stmt.DeclareModule>(Assert.Single(result.Statements));
        var member = Assert.Single(module.Members);
        if (exported)
            member = Assert.IsType<Stmt.Export>(member).Declaration!;

        var declaration = Assert.IsType<Stmt.Enum>(member);
        Assert.Equal("Value", declaration.Name.Lexeme);
        Assert.Equal(constant, declaration.IsConst);
    }
}
