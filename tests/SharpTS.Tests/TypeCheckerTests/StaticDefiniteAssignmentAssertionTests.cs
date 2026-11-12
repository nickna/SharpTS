using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class StaticDefiniteAssignmentAssertionTests
{
    [Theory]
    [InlineData("class C { static value!: number; }")]
    [InlineData("class C<T> { static value!: number; }")]
    [InlineData("const C = class { static value!: number; };")]
    public void StaticAssertionsReportTheGrammarDiagnostic(string source)
    {
        foreach (var options in new[] { TypeCheckerOptions.Strict, TypeCheckerOptions.Default })
        {
            var diagnostics = new TypeChecker(options)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
            Assert.Equal("TS1255", Assert.Single(diagnostics).TsCode);
        }
    }

    [Theory]
    [InlineData("class C { value!: number; }")]
    [InlineData("const C = class { value!: number; };")]
    public void InstanceAssertionsRemainValid(string source)
    {
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
    }
}
