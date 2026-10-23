using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ReadonlyArrayCompoundAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["readonly-array-subtract", """
            const values: readonly number[] = [4];
            values[0] -= 2;
            console.log(values[0]);
            """, "TS2542:2"];
        yield return ["readonly-array-supported-arithmetic", """
            const values: readonly number[] = [120];
            values[0] += 2;
            values[0] -= 2;
            values[0] *= 2;
            values[0] /= 2;
            values[0] %= 7;
            """ + "\n", "TS2542:2,TS2542:3,TS2542:4,TS2542:5,TS2542:6"];
        yield return ["mutable-array-supported-arithmetic", """
            const values: number[] = [120];
            values[0] += 2;
            values[0] -= 2;
            values[0] *= 2;
            values[0] /= 2;
            values[0] %= 7;
            """ + "\n", ""];
    }

    public static IEnumerable<object[]> CollectionCases() => Cases().SelectMany(row =>
        new[] { "Recovery", "Modules" }
            .Select(collection => new object[] { row[0], row[1], row[2], collection }));

    [Theory, MemberData(nameof(CollectionCases))]
    public void ReadonlyAndMutableArrayCompoundsKeepPreciseDiagnostics(
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
            var path = Path.Combine(Path.GetTempPath(), "readonly-array-compound-" + Guid.NewGuid().ToString("N"), "main.ts");
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
