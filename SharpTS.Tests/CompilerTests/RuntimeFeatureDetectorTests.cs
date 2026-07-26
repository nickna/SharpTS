using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class RuntimeFeatureDetectorTests
{
    [Theory]
    [InlineData("async function f() { for await (const x of [1]) {} }", true)]
    [InlineData("function f() { for (const x of [1]) {} }", false)]
    public void DetectsForAwaitOfAdapterRequirement(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);

        Assert.Equal(expected, features.UsesForAwaitOf);
    }
}
