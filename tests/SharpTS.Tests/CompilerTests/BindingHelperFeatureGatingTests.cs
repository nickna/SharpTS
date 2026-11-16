using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class BindingHelperFeatureGatingTests
{
    [Fact]
    public void WithOnlyIndirectEvalRetainsItsHelpersWithoutAsyncFeatures()
    {
        const string source = """
            (0, eval)("function body(){ with({value:7}) { console.log(value); } } body();");
            """;
        Assert.Equal("7\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Theory]
    [InlineData("eval")]
    [InlineData("(eval)")]
    [InlineData("(0, eval)")]
    [InlineData("Function")]
    [InlineData("globalThis.eval")]
    [InlineData("global['eval']")]
    [InlineData("globalThis['Function']")]
    public void OpaqueEvaluatorReferencesRetainBindingHelpers(string expression)
    {
        var statements = new Parser(new Lexer($"const evaluator = {expression};").ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);
        Assert.True(features.UsesArrayBinding);
        Assert.True(features.UsesWithEnvironment);
    }

    [Theory]
    [InlineData("", "eval")]
    [InlineData("", "(eval)")]
    [InlineData("", "(0, eval)")]
    [InlineData("const execute = eval;", "execute")]
    [InlineData("const execute = (0, eval);", "execute")]
    [InlineData("", "globalThis.eval")]
    public void LiteralEvalRetainsHelpersForLateDiscoveredBodies(string declaration, string callee)
    {
        // Keep async-family selection separate: indirect literal eval does not
        // currently discover Promise/for-await features before runtime emission.
        string source = $$"""
            async function keepAsyncRuntime() { for await (const value of []) {} }
            {{declaration}}
            {{callee}}("async function body(){ with({source:[[7]]}) { for await(const [value] of source) console.log(value); } } body();");
            """;
        Assert.Equal("7\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void IndirectEvalExpressionBodiesReceiveHelperMetadata()
    {
        // This preserves the exact helper-selection reduction. Executing an
        // indirect-eval IIFE has a separate callable-identity runtime defect.
        const string source = """
            async function keepAsyncRuntime() { for await (const value of []) {} }
            (0, eval)("(async function(){ with({source:[[7]]}) { for await(const [value] of source) console.log(value); } })()");
            """;
        Assert.NotEmpty(TestHarness.CompileUncheckedAndVerify(source));
    }
}
