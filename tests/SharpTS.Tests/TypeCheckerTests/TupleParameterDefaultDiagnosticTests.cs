using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class TupleParameterDefaultDiagnosticTests
{
    // All four sources are checked only; none of these profiles executes or emits them.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            const target: any = function (
                [value]: [(value: number) => number] = [(value: number) => value]
            ) { return value; };
            """.ReplaceLineEndings("\n") + "\n", "", 0];
        yield return ["named-declaration", """
            function target(
                [value]: [(value: number) => number] = [(value: number) => value]
            ) { return value; }
            """.ReplaceLineEndings("\n") + "\n", "", 0];
        yield return ["missing-tuple-element", """
            const target: any = function (
                [value]: [(value: number) => number] = []
            ) { return value; };
            """.ReplaceLineEndings("\n") + "\n", "TS2322", 2];
        yield return ["wrong-tuple-element", """
            const target: any = function (
                [value]: [(value: number) => number] = [0]
            ) { return value; };
            """.ReplaceLineEndings("\n") + "\n", "TS2322", 2];
    }

    public static IEnumerable<object[]> CollectionCases() => Cases()
        .SelectMany(row => new[] { false, true }
            .Select(modules => new object[] { row[0], row[1], row[2], row[3], modules }));

    [Theory, MemberData(nameof(CollectionCases))]
    public void CollectedDefaultsRetainContextualTupleTypes(
        string name, string source, string expectedCode, int expectedLine, bool modules)
    {
        Assert.NotEmpty(name);
        IReadOnlyList<Diagnostic> diagnostics;
        if (modules)
        {
            var path = Path.Combine(Path.GetTempPath(), "tuple-parameter-default-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }
        else
        {
            diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        }

        if (expectedCode.Length == 0)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(expectedCode, diagnostic.TsCode);
            // Both collectors must retain the parameter/default line reported by TypeScript.
            Assert.Equal(expectedLine, diagnostic.Line);
        }
    }
}
