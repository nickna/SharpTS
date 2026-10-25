using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericInterfaceProjectionReadonlyTests
{
    [Theory]
    [InlineData("Pick<Box, 'value' | 'mutable'>")]
    [InlineData("Omit<Box, 'other'>")]
    [InlineData("Required<Box>")]
    public void FlattenedGenericInheritancePreservesReadonlyMembers(string projection)
    {
        var source = $$"""
            interface Base<T> { readonly value: T; }
            interface Middle<T> extends Base<T> { mutable: T; }
            interface Child<T> extends Middle<T> { other: string; }
            type Box = Child<number>;
            declare let box: {{projection}};
            const value: number = box.value;
            box.mutable = 2;
            box.value = 2;
            """;
        var diagnostic = Assert.Single(Check(source).Diagnostics);
        Assert.Equal("TS2540", diagnostic.TsCode);
        Assert.Equal(8, diagnostic.Line);
    }

    [Theory]
    [InlineData("Pick<Box, 'value'>", false)]
    [InlineData("Pick<Box, 'value'>", true)]
    [InlineData("Omit<Box, 'other'>", false)]
    [InlineData("Required<Box>", false)]
    public void MutableOverrideWinsOverInheritedReadonlyAndRetainsSubstitution(string projection, bool overrideAtLeaf)
    {
        var source = $$"""
            interface Base<T> { readonly value: T; }
            interface Middle<T> extends Base<T> { {{(overrideAtLeaf ? "" : "value: T;")}} }
            interface Child<T> extends Middle<T> { {{(overrideAtLeaf ? "value: T;" : "")}} other: string; }
            type Box = Child<number>;
            declare let box: {{projection}};
            box.value = 2;
            const value: number = box.value;
            box.value = 'wrong';
            """;
        var diagnostic = Assert.Single(Check(source).Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(8, diagnostic.Line);
    }

    private static SharpTS.Diagnostics.TypeCheckDiagnosticResult Check(string source) =>
        new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source));
}
