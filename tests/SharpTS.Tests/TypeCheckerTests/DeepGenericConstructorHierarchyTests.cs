using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DeepGenericConstructorHierarchyTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(260, false)]
    [InlineData(260, true)]
    public void ConstructorCompatibilityRetainsInheritedParameters(int depth, bool compatible)
    {
        List<string> lines = ["class Root { constructor(value: string) {} }"];
        for (int i = 0; i < depth; i++)
            lines.Add($"class C{i} extends {(i == 0 ? "Root" : $"C{i - 1}")} {{}}");
        lines.Add($"class Target<T> extends C{depth - 1} {{}}");
        lines.Add($"class Source<U> {{ constructor(value?: {(compatible ? "string" : "number")}) {{}} }}");
        lines.Add("const Alias: typeof Target = Source;");
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(string.Join("\n", lines))).Diagnostics;

        if (compatible)
            Assert.Empty(diagnostics);
        else
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal("TS2322", diagnostic.TsCode);
            Assert.Equal(depth + 4, diagnostic.Line);
        }
    }

    [Fact]
    public void DynamicSuperclassRetainsItsKnownCompatiblePrefix()
    {
        const string source = """
            declare const Dynamic: any;
            class Target<T> extends Dynamic { value = 1; }
            class Source<U> { value = 1; }
            const Alias: typeof Target = Source;
            """;
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
    }
}
