using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class OptionalClassFieldWriteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalFields_RespectExactOptionalWritePolicy(bool exact)
    {
        var source = "class C { label?: string; } new C().label = undefined;";
        var result = new TypeChecker(new TypeCheckerOptions { StrictNullChecks = true, ExactOptionalPropertyTypes = exact })
            .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        Assert.Equal(exact, result.Diagnostics.Any(d => d.TsCode == "TS2322"));
    }
    [Fact]
    public void OptionalConstructorAssignment_AcceptsUndefinedButRejectsOtherValues()
    {
        foreach (var value in new[] { "label", "7" })
        {
            var source = "class C { label?: string; constructor(label?: string) { this.label = " + value + "; } }";
            var result = new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            Assert.Equal(value == "7", result.Diagnostics.Any(d => d.TsCode == "TS2322"));
        }
    }
}
