using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3RequiredSpreadDiagnosticTests
{
    [Fact]
    public void RequiredSpreadReportsTheOverwrittenExplicitMember()
    {
        const string source = """
            declare const required: { value: number };
            const result = {
                value: 'before',
                ...required
            };
            const finalValue: number = result.value;
            """;
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TS2783", diagnostic.TsCode);
        Assert.Equal(3, diagnostic.Line);
    }

    [Theory]
    [InlineData("declare const later: { value?: number }; const result = { value:'before', ...later };")]
    [InlineData("declare const later: { value:number }|undefined; const result = { value:'before', ...later };")]
    [InlineData("declare const later: { value:number }; const result = { ...later, value:'after' };")]
    public void PossibleOverwritesAndLaterExplicitMembersRemainLegal(string source)
        => Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
}
