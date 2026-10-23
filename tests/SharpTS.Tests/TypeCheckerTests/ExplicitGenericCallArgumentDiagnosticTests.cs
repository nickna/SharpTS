using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ExplicitGenericCallArgumentDiagnosticTests
{
    // Both sources are checked only; the invalid call must never execute or emit.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            function identity<T>(value: T): T { return value; }
            identity<number>("wrong");
            """.ReplaceLineEndings("\n") + "\n", "TS2345", 2];
        yield return ["valid-explicit-and-inferred", """
            function identity<T>(value: T): T { return value; }
            const numberResult: number = identity<number>(7);
            const stringResult: string = identity("text");
            """.ReplaceLineEndings("\n") + "\n", "", 0];
    }

    public static IEnumerable<object[]> CollectionCases() => Cases()
        .SelectMany(row => new[] { false, true }
            .Select(modules => new object[] { row[0], row[1], row[2], row[3], modules }));

    [Theory, MemberData(nameof(CollectionCases))]
    public void CollectedExplicitArgumentsRespectSubstitutedParameters(
        string name, string source, string expectedCode, int expectedLine, bool modules)
    {
        Assert.NotEmpty(name);
        IReadOnlyList<Diagnostic> diagnostics;
        if (modules)
        {
            var path = Path.Combine(Path.GetTempPath(), "explicit-generic-argument-" + Guid.NewGuid().ToString("N"), "main.ts");
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
            Assert.Equal(expectedLine, diagnostic.Line);
        }
    }
}
