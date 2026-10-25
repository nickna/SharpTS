using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class UtilityProjectionReadonlyMemberTests
{
    public static IEnumerable<object[]> ReadonlyProjections()
    {
        foreach (string declaration in new[]
        {
            "interface Box { readonly value: number; mutable: number; other: string; }",
            "type Box = { readonly value: number; mutable: number; other: string; };",
            "interface Box { readonly value?: number; mutable?: number; other?: string; }",
            "interface Base { readonly value: number; } interface Box extends Base { mutable: number; other: string; }",
            "interface Base { readonly value?: number; } interface Box extends Base { mutable?: number; other?: string; }",
            "interface Base<T> { readonly value: T; } interface Child<T> extends Base<T> { mutable: T; other: string; } type Box = Child<number>;",
            "class Box { readonly value = 1; mutable = 1; other = 'ok'; }",
            "class Box { get value(): number { return 1; } mutable = 1; other = 'ok'; }",
            "type Box = { readonly value: number } & { mutable: number; other: string; };",
            "type Box = { readonly value: number; mutable: number; other: string } | { value: number; mutable: number; other: string };",
            "interface Base { readonly value: number; } interface Child extends Base { mutable: number; other: string; } type Box = Child | { value: number; mutable: number; other: string };"
        })
            foreach (string projection in new[] { "Pick<Box, 'value' | 'mutable'>", "Omit<Box, 'other'>" })
                yield return [declaration, projection];
    }

    [Theory, MemberData(nameof(ReadonlyProjections))]
    public void PickAndOmitRetainReadonlyWithoutFreezingMutableFields(string declaration, string projection)
    {
        var diagnostics = Check(declaration + $" declare let box: {projection}; box.mutable = 2; box.value = 2;");
        Assert.Equal("TS2540", Assert.Single(diagnostics).TsCode);
    }

    [Theory]
    [InlineData("Pick<Box, 'value' | 'mutable'>")]
    [InlineData("Omit<Box, 'other'>")]
    public void MutableOverridesAndIntersectionConstituentsRemainWritable(string projection)
    {
        foreach (string declaration in new[]
        {
            "interface Base { readonly value?: number; } interface Box extends Base { value: number; mutable: number; other: string; }",
            "type Box = { readonly value: number } & { value: number; mutable: number; other: string; };",
            "type Box = { value: number; mutable: number; other: string } | { value: number; mutable: number; other: string; extra?: boolean };"
        })
            Assert.Empty(Check(declaration + $" declare let box: {projection}; box.mutable = 2; box.value = 2;"));
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source) =>
        new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
}
