using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class SymbolUnionEqualityDiagnosticTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["overlapping-constituents-and-permissive-operands", """
            function stringMember(value: string | symbol) { return value === "name"; }
            function numberMember(value: number | symbol) { return value === 1; }
            function nullMember(value: null | symbol) { return value === null; }
            function undefinedMember(value: undefined | symbol) { return value === undefined; }
            function anyOperand(value: any, key: symbol) { return value === key; }
            function unknownOperand(value: unknown, key: symbol) { return value === key; }
            """ + "\n", new (string Code, int Line)[] {  }];
        yield return ["disjoint-symbol-and-literal-constituents", """
            function wrongPrimitive(value: number | symbol) { return value === "name"; }
            function wrongLiteral(value: "other" | symbol) { return value === "name"; }
            function pureSymbol(value: symbol) { return value === "name"; }
            """ + "\n", new (string Code, int Line)[] { ("TS2367", 1), ("TS2367", 2), ("TS2367", 3) }];
        yield return ["loose-nullish-comparisons-both-orders", """
            declare const possiblyUndefined: symbol | undefined;
            possiblyUndefined == null;
            null == possiblyUndefined;
            declare const possiblyNull: symbol | null;
            possiblyNull == undefined;
            undefined == possiblyNull;
            """ + "\n", new (string Code, int Line)[] {  }];
    }

    public static IEnumerable<object[]> CollectionCases() => Cases()
        .SelectMany(row => new[] { "Recovery", "Modules" }
            .Select(collection => new object[] { row[0], row[1], row[2], collection }));

    [Theory, MemberData(nameof(CollectionCases))]
    public void CollectedDiagnosticsUseActualUnionOverlap(
        string name, string source, (string Code, int Line)[] expected, string collection)
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
            var path = Path.Combine(Path.GetTempPath(), "symbol-union-equality-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }

        if (expected.Length == 0)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            Assert.Equal(expected.OrderBy(item => item.Line).ThenBy(item => item.Code),
                diagnostics.Select(item => (Code: item.TsCode ?? "", item.Line))
                    .OrderBy(item => item.Line).ThenBy(item => item.Code));
        }
    }
}
