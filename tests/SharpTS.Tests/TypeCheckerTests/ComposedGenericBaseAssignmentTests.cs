using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ComposedGenericBaseAssignmentTests
{
    [Theory]
    [InlineData("string[]", false)]
    [InlineData("number[]", true)]
    public void AncestorArgumentsComposeAcrossGenericLevels(string expected, bool rejected)
    {
        var source = $$"""
            class Base<T> { value: T = null as any; }
            class Middle<U> extends Base<U[]> {}
            class Leaf extends Middle<string> {}
            const value: Base<{{expected}}> = new Leaf();
            """;
        void Check() => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        if (rejected) Assert.ThrowsAny<TypeCheckException>(Check);
        else Check();
    }

    [Fact]
    public void KnownGenericAncestorSurvivesDynamicTail()
    {
        const string source = "declare const Parent: any; class Base<T> extends Parent { #brand = 1; value: T = null as any; } class Middle<U> extends Base<U[]> {} class Leaf extends Middle<string> {} const value: Base<string[]> = new Leaf();";
        new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
    }
}
