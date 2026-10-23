using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class SymbolNullishComparisonDiagnosticTests
{
    // Declaration-only inputs: collect diagnostics without executing or emitting them.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            declare const possiblyUndefined: symbol | undefined;
            possiblyUndefined == null;
            null == possiblyUndefined;
            possiblyUndefined === null;
            null === possiblyUndefined;
            declare const possiblyNull: symbol | null;
            possiblyNull == undefined;
            undefined == possiblyNull;
            possiblyNull === undefined;
            undefined === possiblyNull;
            """.ReplaceLineEndings("\n") + "\n"];
        yield return ["inequality", """
            declare const possiblyUndefined: symbol | undefined;
            possiblyUndefined !== null;
            null !== possiblyUndefined;
            declare const possiblyNull: symbol | null;
            possiblyNull !== undefined;
            undefined !== possiblyNull;
            """.ReplaceLineEndings("\n") + "\n"];
        yield return ["bare-symbol", """
            declare const value: symbol;
            value === null;
            null === value;
            value !== null;
            null !== value;
            value === undefined;
            undefined === value;
            value !== undefined;
            undefined !== value;
            """.ReplaceLineEndings("\n") + "\n"];
    }

    public static IEnumerable<object[]> CollectionCases() => Cases()
        .SelectMany(row => new[] { "Recovery", "Modules" }
            .Select(collection => new object[] { row[0], row[1], collection }));

    [Theory, MemberData(nameof(CollectionCases))]
    public void StrictNullishComparisonsAreAcceptedWithoutExecution(
        string name, string source, string collection)
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
            var path = Path.Combine(Path.GetTempPath(), "symbol-nullish-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }

        Assert.Empty(diagnostics);
    }
}
