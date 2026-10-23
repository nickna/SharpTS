using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NamedExpressionDefaultScopeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["same-name-parameter-shadowing", """
            const own: any = function sameName(sameName: any = sameName) { return sameName; };
            const later: any = function laterName(value: any = laterName, laterName: any = 1) { return value; };
            const patternOwn: any = function patternName({ patternName = patternName }: any = {}) { return patternName; };
            const patternLater: any = function futureName(value: any = futureName, { futureName }: any = { futureName: 1 }) { return value; };
            const deferred: any = function deferredName(read: any = () => deferredName, deferredName: any = 1) { return read(); };
            const invoked: any = function invokedName(read: any = (() => invokedName)(), invokedName: any = 1) { return read; };
            """ + "\n", "TS2372:1,TS2373:2,TS2372:3,TS2373:4,TS2373:6"];
        yield return ["typed-pattern-shadow-errors", """
            const objectTarget: any = function objectSelf(read: any = () => objectSelf("wrong"), { objectSelf }: { objectSelf: (value: number) => number } = { objectSelf: (value: number) => value }) { return read(); };
            const arrayTarget: any = function arraySelf(read: any = () => arraySelf("wrong"), [arraySelf]: [(value: number) => number] = [(value: number) => value]) { return read(); };
            """ + "\n", "TS2345:1,TS2345:2"];
        yield return ["optional-pattern-without-leaf-default", """
            const objectTarget: any = function objectSelf(read: any = () => objectSelf(7), { objectSelf }: { objectSelf?: (value: number) => number } = {}) { return read(); };
            const arrayTarget: any = function arraySelf(read: any = () => arraySelf(7), [arraySelf]: [((value: number) => number)?] = []) { return read(); };
            """ + "\n", "TS2722:1,TS2722:2"];
        yield return ["optional-pattern-with-leaf-default", """
            const objectTarget: any = function objectSelf(read: any = () => objectSelf(7), { objectSelf = (value: number) => value }: { objectSelf?: (value: number) => number } = {}) { return read(); };
            const arrayTarget: any = function arraySelf(read: any = () => arraySelf(7), [arraySelf = (value: number) => value]: [((value: number) => number)?] = []) { return read(); };
            """ + "\n", ""];
        yield return ["typed-readonly-and-private-name-supported-errors", """
            const wrongArgument: any = function typedSelf(value: number = typedSelf("wrong")): number { return value; };
            const readonlyDefault: any = function readonlySelf(value: any = (readonlySelf = () => 1)) { return value; };
            const noLeak: any = function hiddenSelf(value: any = hiddenSelf) { return value; };
            hiddenSelf;
            """ + "\n", "TS2345:1,TS2630:2,TS2304:4"];
        yield return ["typed-pattern-shadow-explicit-tuple-default-preservation", """
            const objectTarget: any = function objectSelf(read: any = () => objectSelf(7), { objectSelf }: { objectSelf: (value: number) => number } = { objectSelf: (value: number) => value }) { return read(); };
            const arrayTarget: any = function arraySelf(read: any = () => arraySelf(7), [arraySelf]: [(value: number) => number] = [(value: number) => value] as [(value: number) => number]) { return read(); };
            """ + "\n", ""];
        yield return ["mutable-shadow-and-generic-call-context-preservation", """
            const parameter: any = function self(self: any, read: any = () => self) { self = 7; return read(); };
            const body: any = function bodySelf(read: any = () => bodySelf) { let bodySelf = 7; bodySelf = 8; return [read(), bodySelf]; };
            const generic: any = function genericSelf<T>(value: T, read: (next: T) => T = (next: T) => genericSelf<T>(next)): T { return value; };
            const contextual: (value: number, read?: (next: number) => number) => number = function contextualSelf(value, read = contextualSelf) { return value; };
            """ + "\n", ""];
    }

    public static IEnumerable<object[]> CollectionCases() => Cases()
        .SelectMany(row => new[] { "Recovery", "Modules" }
            .Select(collection => new object[] { row[0], row[1], row[2], collection }));

    [Theory, MemberData(nameof(CollectionCases))]
    public void DefaultScopesPreserveTypedPrivateBindingsAndParameterShadows(
        string name, string source, string expected, string collection)
    {
        Assert.NotEmpty(name);
        IReadOnlyList<Diagnostic> diagnostics;
        if (collection == "Recovery")
        {
            diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        }
        else
        {
            Assert.Equal("Modules", collection);
            var path = Path.Combine(Path.GetTempPath(), "named-default-scope-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }

        AssertDiagnostics(expected, diagnostics);
    }

    private static void AssertDiagnostics(string expected, IReadOnlyList<Diagnostic> diagnostics)
    {
        var wanted = expected.Length == 0 ? Array.Empty<string>() : expected.Split(',');
        Assert.Equal(wanted.OrderBy(value => value, StringComparer.Ordinal),
            diagnostics.Select(item => (item.TsCode ?? "") + ":" + item.Line)
                .OrderBy(value => value, StringComparer.Ordinal));
    }

    private sealed record Checked(SourceDocument Document, TypeCheckDiagnosticResult Result,
        FrozenEditorSemanticIndex Facts);

    private static Checked CheckEditor(string source)
    {
        var document = new SourceDocument("named-default-scopes.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        return new(document, result, checker.EditorFacts.Freeze());
    }

    private static EditorVisibleBinding Local(Checked value, int offset, string name) =>
        Assert.Single(value.Facts.GetVisibleBindings(value.Document, offset),
            binding => binding.LocalName == name && binding.Facet == BindingNamespace.Value);

    [Fact]
    public void EditorPrivateNameIsVisibleInDefaultAndAbsentOutside()
    {
        var source = (string)NamedExpressionDefaultSelfTests.Cases()
            .Single(row => (string)row[0] == "ordinary-closure")[1];
        var value = CheckEditor(source);
        Assert.Empty(value.Result.Diagnostics);
        int use = source.IndexOf("=> privateSelf", StringComparison.Ordinal) + 3;
        Assert.True(use >= 3);
        var self = Local(value, use, "privateSelf");
        Assert.Equal(EditorFactAvailability.Available, self.Availability);
        Assert.Contains("=>", self.Type.Text);
        Assert.DoesNotContain(value.Facts.GetVisibleBindings(value.Document,
            source.IndexOf("console.log", StringComparison.Ordinal)), binding => binding.LocalName == "privateSelf");
    }

    [Fact]
    public void EditorLaterParameterMasksPrivateNameBeforeInitialization()
    {
        var row = Cases().Single(item => (string)item[0] == "same-name-parameter-shadowing");
        var source = (string)row[1];
        var value = CheckEditor(source);
        AssertDiagnostics((string)row[2], value.Result.Diagnostics);
        int use = source.IndexOf("=> deferredName", StringComparison.Ordinal) + 3;
        Assert.True(use >= 3);
        var parameter = Local(value, use, "deferredName");
        Assert.Equal(EditorFactAvailability.Unavailable, parameter.Availability);
        Assert.Equal("any", parameter.Type.Text);
    }

    [Fact]
    public void EditorBodyShadowDoesNotReplacePrivateDefaultBinding()
    {
        var source = (string)Cases().Single(row => (string)row[0] == "mutable-shadow-and-generic-call-context-preservation")[1];
        var value = CheckEditor(source);
        Assert.Empty(value.Result.Diagnostics);
        int defaultUse = source.IndexOf("=> bodySelf", StringComparison.Ordinal) + 3;
        int bodyUse = source.IndexOf("bodySelf = 8", StringComparison.Ordinal);
        Assert.True(defaultUse >= 3 && bodyUse >= 0);
        var privateSelf = Local(value, defaultUse, "bodySelf");
        var bodyLocal = Local(value, bodyUse, "bodySelf");
        Assert.Equal(EditorFactAvailability.Available, privateSelf.Availability);
        Assert.Contains("=>", privateSelf.Type.Text);
        Assert.Equal(EditorFactAvailability.Available, bodyLocal.Availability);
        Assert.Equal("number", bodyLocal.Type.Text);
        Assert.NotEqual(privateSelf.Binding!.Id, bodyLocal.Binding!.Id);
    }
}
