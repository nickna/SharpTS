using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class IntersectionReadonlyMemberTests
{
    [Theory]
    [InlineData("{ readonly value: number } & { mutable: number }")]
    [InlineData("{ mutable: number } & { readonly value: number }")]
    [InlineData("{ readonly value?: number } & { mutable: number }")]
    [InlineData("({ readonly value: number } & { mutable: number }) & { more?: boolean }")]
    [InlineData("{ readonly value: number } & { readonly value: number; mutable: number }")]
    public void IntersectionsRetainReadonlyOnUnrelatedAndRepeatedMembers(string type)
    {
        var result = Check($"declare let box: {type}; box.mutable = 1; box.value = 2;");
        Assert.Equal("TS2540", Assert.Single(result.Diagnostics).TsCode);
    }

    [Theory]
    [InlineData("{ readonly value: number } & { value: number }")]
    [InlineData("{ value: number } & { readonly value: number }")]
    public void MutableConstituentsKeepTheirSharedPropertyWritable(string type)
        => Assert.Empty(Check($"declare let box: {type}; box.value = 2;").Diagnostics);

    [Fact]
    public void GenericIntersectionsPreserveReadonlyAfterSubstitution()
    {
        Assert.Equal("TS2540", Assert.Single(Check("""
            type Box<T> = T & { readonly value: number };
            declare let box: Box<{ mutable: number }>;
            box.mutable = 1;
            box.value = 2;
            """).Diagnostics).TsCode);
    }

    [Fact]
    public void InheritedReadonlyAndOptionalMetadataSurviveIntersection()
    {
        var result = Check("""
            interface Base { readonly value?: number; }
            interface Box extends Base { mutable: number; }
            declare let box: Box & { more: number };
            box.mutable = 1;
            box.value = 2;
            const wrong: number = box.value;
            """);
        Assert.Equal(new[] { "TS2540", "TS2322" }, result.Diagnostics.Select(diagnostic => diagnostic.TsCode));
    }

    [Fact]
    public void RequiredAndPartialRetainIntersectionReadonlyMetadata()
    {
        var result = Check("""
            type Box = { readonly value?: number } & { mutable?: number };
            declare let required: Required<Box>;
            declare let partial: Partial<Box>;
            required.mutable = 1;
            partial.mutable = 1;
            required.value = 2;
            partial.value = 2;
            """);
        Assert.Equal(new[] { "TS2540", "TS2540" }, result.Diagnostics.Select(diagnostic => diagnostic.TsCode));
    }

    private static SharpTS.Diagnostics.TypeCheckDiagnosticResult Check(string source) =>
        new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source));
}
