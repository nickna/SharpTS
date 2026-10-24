using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DeepInstanceBrandTests
{
    [Theory]
    [InlineData("#p = 1;", false)]
    [InlineData("#p = 1;", true)]
    [InlineData("#p() {}", false)]
    [InlineData("#p() {}", true)]
    [InlineData("private p = 1;", false)]
    [InlineData("private p = 1;", true)]
    [InlineData("protected p = 1;", false)]
    [InlineData("protected p = 1;", true)]
    public void DeepAncestorsRetainPrivateAndProtectedBrands(string member, bool generic)
    {
        var lines = Hierarchy(member, generic);
        string type = generic ? "Child<number>" : "C259";
        lines.Add($"const valid: {type} = new {type}();");
        lines.Add($"const missing: {type} = {{}};");
        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(string.Join("\n", lines))).Diagnostics);
        Assert.Contains(diagnostic.TsCode, new[] { "TS2322", "TS2741" });
        Assert.Equal(lines.Count, diagnostic.Line);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepUnbrandedClassesRemainStructural(bool generic)
    {
        var lines = Hierarchy("", generic);
        lines.Add($"const valid: {(generic ? "Child<number>" : "C259")} = {{}};");
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(string.Join("\n", lines))).Diagnostics);
    }

    private static List<string> Hierarchy(string member, bool generic)
    {
        List<string> lines = [$"class Root{(generic ? "<T>" : "")} {{ {member} }}"];
        for (int i = 0; i < 260; i++)
            lines.Add($"class C{i} extends {(i == 0 ? (generic ? "Root<number>" : "Root") : $"C{i - 1}")} {{}}");
        if (generic) lines.Add("class Child<T> extends C259 {}");
        return lines;
    }
}
