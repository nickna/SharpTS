using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ReadonlyTupleCompoundAssignmentTests
{
    // Checker-only sources: none of these profiles executes or emits either program.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            const values: readonly [number, number] = [4, 8];
            values[0] -= 2;
            console.log(values[0]);
            """.ReplaceLineEndings("\n") + "\n", "TS2540:2"];
        yield return ["mutable-tuple-subtract", """
            const values: [number, number] = [4, 8];
            values[0] -= 2;
            console.log(values[0]);
            """.ReplaceLineEndings("\n") + "\n", ""];
    }

    public static IEnumerable<object[]> CollectionCases() => Cases().SelectMany(row =>
        new[] { "Recovery", "Modules" }
            .Select(collection => new object[] { row[0], row[1], row[2], collection }));

    public static IEnumerable<object[]> CliCases() => Cases().SelectMany(row =>
        new[] { "Default", "Strict" }
            .Select(collection => new object[] { row[0], row[1], row[2], collection }));

    [Theory, MemberData(nameof(CollectionCases))]
    public void ReadonlyAndMutableTupleCompoundsKeepPreciseDiagnostics(
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
            var path = Path.Combine(Path.GetTempPath(), "readonly-tuple-compound-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }

        var expectedDiagnostics = expected.Length == 0 ? Array.Empty<string>() : expected.Split(',');
        Assert.Equal(expectedDiagnostics.OrderBy(value => value, StringComparer.Ordinal),
            diagnostics.Select(item => (item.TsCode ?? "") + ":" + item.Line)
                .OrderBy(value => value, StringComparer.Ordinal));
    }
}
