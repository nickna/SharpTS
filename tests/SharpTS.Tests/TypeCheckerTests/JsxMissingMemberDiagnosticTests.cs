using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class JsxMissingMemberDiagnosticTests
{
    [Theory]
    [InlineData(1, "TS2741")]
    [InlineData(2, "TS2739")]
    [InlineData(3, "TS2739")]
    [InlineData(4, "TS2739")]
    [InlineData(5, "TS2739")]
    [InlineData(6, "TS2740")]
    [InlineData(7, "TS2740")]
    public void MissingChildMembersUseTheTypeScriptListThreshold(int count, string code)
    {
        foreach (string child in new[] { "JSX.Element", "typeof Model" })
        {
            var diagnostic = Assert.Single(Check(count, child));
            Assert.Equal(code, diagnostic.TsCode);
            Assert.Equal(9, diagnostic.Line);
        }
    }

    [Fact]
    public void MatchingInstanceChildrenRemainValid()
        => Assert.Empty(Check(7, "Model"));

    [Fact]
    public void WrongMemberTypesRemainAssignmentErrors()
        => Assert.Equal("TS2322", Assert.Single(Check(4,
            "{ p0: string; p1: number; p2: number; p3: number }")).TsCode);

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(int count, string childType)
    {
        string entry = Path.GetFullPath("jsx-missing-member-diagnostics/main.tsx");
        string fields = string.Join(" ", Enumerable.Range(0, count).Select(index => $"p{index} = {index};"));
        var files = new Dictionary<string, string>
        {
            [entry] = $$"""
                declare const React: any;
                declare namespace JSX {
                    interface Element { brand: string; }
                    interface ElementChildrenAttribute { children: {}; }
                }
                class Model { {{fields}} }
                declare function Comp(props: { children: Model }): JSX.Element;
                declare const child: {{childType}};
                const bad = <Comp>{child}</Comp>;
                """,
        };
        var resolver = new ModuleResolver(entry, files, TypeScriptProgramOptions.Disabled)
        {
            JsxOptions = new JsxParseOptions(JsxMode.Preserve),
        };
        var program = resolver.LoadProgram(entry);
        var checker = new TypeChecker(strictNullChecks: true);
        checker.CheckModules(resolver.GetModulesInOrder(program), resolver);
        Assert.Empty(program.ParseDiagnostics);
        return checker.GetDiagnostics();
    }
}
