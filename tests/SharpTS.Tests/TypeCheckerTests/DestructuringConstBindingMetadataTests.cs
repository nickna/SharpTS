using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DestructuringConstBindingMetadataTests
{
    [Theory]
    [InlineData("const", true)]
    [InlineData("let", false)]
    [InlineData("var", false)]
    public void NestedDefaultAndRestBindingsRetainOnlyTheirSourceConstModifier(string keyword, bool isConst)
    {
        foreach (string pattern in new[] { "[first, { inner: [second = 1, ...tail] }, ...rest]", "{ renamed: first, nested: [second = 1, ...tail], ...rest }" })
        {
            var variables = Variables(TestHarness.ParseOrThrow($"{keyword} {pattern} = input;"));
            var written = variables.Where(variable => variable.Name.Start >= 0).ToArray();
            Assert.Equal(new[] { "first", "rest", "second", "tail" }, written.Select(variable => variable.Name.Lexeme).Order());
            Assert.All(written, variable => Assert.Equal(isConst, variable.IsSourceConst));
            Assert.All(variables.Where(variable => variable.Name.Start < 0), variable => Assert.False(variable.IsSourceConst));
        }
    }

    [Theory]
    [InlineData("const", true)]
    [InlineData("let", false)]
    [InlineData("var", false)]
    public void DestructuringLoopHeadsRetainSourceConstButParameterBindingsDoNot(string keyword, bool isConst)
    {
        var variables = Variables(TestHarness.ParseOrThrow($"for ({keyword} [first, {{ value: second }}, ...rest] of input) {{}}"));
        Assert.All(variables.Where(variable => variable.Name.Start >= 0), variable => Assert.Equal(isConst, variable.IsSourceConst));
        Assert.All(Variables(TestHarness.ParseOrThrow("function read([first, { value: second }]: any) {}")),
            variable => Assert.False(variable.IsSourceConst));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstFactsBelongToTheExactLoweredDeclarationAndDoNotMarkMutableShadows(bool modules)
    {
        const string source = "const [value] = [1]; { let value = 2; value; }";
        var statements = TestHarness.ParseOrThrow(source);
        var checkedProgram = Check(source, modules, statements);
        Assert.Empty(checkedProgram.Diagnostics);
        var variables = Variables(checkedProgram.Statements).Where(variable => variable.Name.Lexeme == "value").ToArray();
        Assert.Equal(2, variables.Length);
        var outer = checkedProgram.Map.GetValueBinding(variables[0].Name);
        var inner = checkedProgram.Map.GetValueBinding(variables[1].Name);
        Assert.NotNull(outer);
        Assert.NotNull(inner);
        Assert.NotSame(outer, inner);
        Assert.True(checkedProgram.Map.IsConstCaptureBinding(outer));
        Assert.False(checkedProgram.Map.IsConstCaptureBinding(inner));
        Assert.False(checkedProgram.Map.IsConstBinding(outer));
    }

    public static IEnumerable<object[]> StablePrograms()
    {
        foreach (string source in new[]
        {
            "interface Job { progress: number; } declare function state(): [Job | null, (next: Job | null) => void]; function read() { const [job, setJob] = state(); if (job === null) return; return () => job.progress; }",
            "interface Job { progress: number; } function read(input: { nested: { job: Job | null }; extra: number }) { const { nested: { job }, ...rest } = input; if (job === null) return; return () => job.progress + rest.extra; }",
            "interface Job { progress: number; } function read(input: [Job | null, ...number[]]) { const [job, ...rest] = input; if (job === null) return; return () => job.progress + rest.length; }"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(StablePrograms))]
    public void NarrowedConstDestructuredBindingsStayStableInDeferredCallbacks(string source, bool modules) =>
        Assert.Empty(Check(source, modules).Diagnostics);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutableShadowStillNeedsItsOwnNullableReceiverProof(bool modules)
    {
        const string source = """
            interface Job { progress: number; }
            function read(input: [Job | null]) {
                const [job] = input;
                if (job === null) return;
                { let job: Job | null = null; return () => job.progress; }
            }
            """;
        Assert.Equal("TS18047", Assert.Single(Check(source, modules).Diagnostics).TsCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureConstProvenanceDoesNotChangeAssertedAssignmentAdmission(bool modules) =>
        Assert.Empty(Check("const [value] = [1]; (value as number) = 2;", modules).Diagnostics);

    private sealed record Checked(List<Stmt> Statements, TypeMap Map, IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Diagnostics);

    private static Checked Check(string source, bool modules, List<Stmt>? statements = null)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
        {
            statements ??= TestHarness.ParseOrThrow(source);
            var result = checker.CheckWithRecovery(statements);
            return new(statements, result.TypeMap, result.Diagnostics);
        }
        string path = Path.Combine(Path.GetTempPath(), "destructure-const-facts-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        var map = checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return new(entry.Statements, map, checker.GetDiagnostics());
    }

    private static List<Stmt.Var> Variables(IEnumerable<Stmt> statements)
    {
        var collector = new VariableCollector();
        foreach (var statement in statements) collector.Visit(statement);
        return collector.Variables;
    }

    private sealed class VariableCollector : AstVisitorBase
    {
        public List<Stmt.Var> Variables { get; } = [];
        protected override void VisitVar(Stmt.Var statement) { Variables.Add(statement); base.VisitVar(statement); }
    }
}
