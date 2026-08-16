using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Arrow function expressions may declare optional parameters: <c>(x?: T) =&gt; ...</c>. Previously
/// the speculative arrow parser only accepted <c>name:</c> (not <c>name?:</c>), so such arrows
/// backtracked to a grouped expression and failed with "Expect expression".
/// </summary>
public class ArrowOptionalParameterTests
{
    [Fact]
    public void OptionalParameter_WithType_Parses()
    {
        TestHarness.RunInterpreted("let a = (x?: number) => 1; a();");
    }

    [Fact]
    public void OptionalParameter_NoType_Parses()
    {
        TestHarness.RunInterpreted("let f = (x?) => 1; f();");
    }

    [Fact]
    public void MixedRequiredAndOptionalParameters_Parse()
    {
        TestHarness.RunInterpreted("let g = (a: number, b?: string) => a; g(1);");
    }

    [Fact]
    public void ParenthesizedTernary_StillParses()
    {
        // Regression guard: `(c ? x : y)` must not be mistaken for an arrow with an optional param.
        var output = TestHarness.RunInterpreted("let c = true; console.log((c ? 1 : 2));");
        Assert.Equal("1\n", output);
    }
}
