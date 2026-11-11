using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassExpressionStaticBlockTests
{
    [Theory]
    [InlineData("const C = class { static { const n: number = 'wrong'; } };", "TS2322")]
    [InlineData("const C = class Named { static value = 1; static { const n: string = Named.value; } };", "TS2322")]
    [InlineData("const C = class { static value = 1; static { const n: string = this.value; } };", "TS2322")]
    [InlineData("const C = class { static { const local = 1; } static { local; } };", "TS2304")]
    public void StaticBlockBodiesAreChecked(string source, string code)
    {
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        Assert.Equal(code, Assert.Single(diagnostics).TsCode);
    }

    [Fact]
    public void NamedExpressionStaticBlocksUseTheConstructorAndIsolatedLocals()
    {
        var statements = TestHarness.ParseOrThrow("""
            const C = class Named {
                static value = 1;
                static { const local: number = Named.value; }
                static { const local: number = this.value; }
            };
            """);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
    }
}
