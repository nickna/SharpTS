using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class PartialReadonlyMemberTests
{
    [Theory]
    [InlineData("interface Box { readonly value: number; mutable: number; }")]
    [InlineData("type Box = { readonly value: number; mutable: number; };")]
    [InlineData("interface Generic<T> { readonly value: T; mutable: T; } type Box = Generic<number>;")]
    public void PartialPreservesReadonlyWhileAddingOptionality(string declaration)
    {
        var result = Check(declaration + """

            const empty: Partial<Box> = {};
            const box: Partial<Box> = { value: 1, mutable: 1 };
            box.mutable = 2;
            box.value = 2;
            """);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TS2540", diagnostic.TsCode);
        Assert.Equal(5, diagnostic.Line);
    }

    [Fact]
    public void DeferredPartialPreservesReadonlyAfterSubstitution()
    {
        var result = Check("""
            interface Box { readonly value: number; }
            type Copy<T> = Partial<T>;
            const box: Copy<Box> = { value: 1 };
            box.value = 2;
            """);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TS2540", diagnostic.TsCode);
        Assert.Equal(4, diagnostic.Line);
    }

    [Fact]
    public void ExplicitMutableMappingStillRemovesReadonly()
    {
        Assert.Empty(Check("""
            interface Box { readonly value: number; }
            type Mutable<T> = { -readonly [K in keyof T]: T[K] };
            const box: Mutable<Partial<Box>> = { value: 1 };
            box.value = 2;
            """).Diagnostics);
    }

    private static SharpTS.Diagnostics.TypeCheckDiagnosticResult Check(string source) =>
        new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source));
}
