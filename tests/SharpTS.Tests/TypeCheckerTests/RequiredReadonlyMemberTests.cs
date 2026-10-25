using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class RequiredReadonlyMemberTests
{
    [Theory]
    [InlineData("interface Box { readonly value?: number; mutable?: number; }")]
    [InlineData("type Box = { readonly value?: number; mutable?: number; };")]
    [InlineData("interface Base { readonly value?: number; } interface Box extends Base { mutable?: number; }")]
    [InlineData("interface Generic<T> { readonly value?: T; mutable?: T; } type Box = Generic<number>;")]
    [InlineData("class Box { readonly value?: number; mutable?: number; }")]
    [InlineData("class Box { get value(): number { return 1; } mutable?: number; }")]
    public void RequiredPreservesReadonlyAndKeepsMutableMembersWritable(string declaration)
    {
        var result = Check(declaration + """

            const box: Required<Box> = { value: 1, mutable: 1 };
            box.mutable = 2;
            box.value = 2;
            """);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TS2540", diagnostic.TsCode);
        Assert.Equal(4, diagnostic.Line);
    }

    [Fact]
    public void DeferredRequiredRetainsReadonlyAfterSubstitution()
    {
        Assert.Equal("TS2540", Assert.Single(Check("""
            interface Box { readonly value?: number; }
            type Copy<T> = Required<T>;
            const box: Copy<Box> = { value: 1 };
            box.value = 2;
            """).Diagnostics).TsCode);
    }

    [Fact]
    public void RequiredStillRequiresOptionalSourceProperties()
    {
        Assert.Equal("TS2741", Assert.Single(Check("interface Box { readonly value?: number; } const box: Required<Box> = {};")
            .Diagnostics).TsCode);
    }

    [Fact]
    public void MutableOverridesAndExplicitMutableMappingsRemainWritable()
    {
        Assert.Empty(Check("""
            interface Base { readonly value?: number; }
            interface Child extends Base { value: number; }
            const child: Required<Child> = { value: 1 };
            child.value = 2;
            type Mutable<T> = { -readonly [K in keyof T]: T[K] };
            const mutable: Mutable<Required<Base>> = { value: 1 };
            mutable.value = 2;
            """).Diagnostics);
    }

    private static SharpTS.Diagnostics.TypeCheckDiagnosticResult Check(string source) =>
        new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source));
}
