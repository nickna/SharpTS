using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class UnknownCatchInstanceofDiagnosticTests
{
    internal static readonly string Source = """
        const target: any = globalThis, key = "parseInt";
        const original = Object.getOwnPropertyDescriptor(target, key)!;
        const replacement = (_text: string, _radix?: number) => 41;
        let reads = 0, receiver = true;
        try {
            const data = Reflect.defineProperty(target, key, {
                value: replacement, writable: true, enumerable: false, configurable: true
            });
            console.log("data", data, parseInt === replacement, parseInt("7", 10),
                Object.getOwnPropertyDescriptor(target, key)!.value === replacement);
            const attributes = Reflect.defineProperty(target, key, { enumerable: true });
            const updated = Object.getOwnPropertyDescriptor(target, key)!;
            console.log("attributes", attributes, updated.writable, updated.enumerable,
                updated.configurable, updated.value === replacement);
            const getter = function(this: any) { reads++; receiver = receiver && this === target; return replacement; };
            const accessor = Reflect.defineProperty(target, key, {
                get: getter, set: undefined, enumerable: false, configurable: true
            });
            const descriptor = Object.getOwnPropertyDescriptor(target, key)!;
            console.log("accessor", accessor, descriptor.get === getter, descriptor.set === undefined, reads);
            const held = parseInt;
            console.log("read", held === replacement, held("8", 10), reads, receiver);
            const cleared = Reflect.defineProperty(target, key, {
                value: undefined, writable: true, enumerable: false, configurable: true
            });
            console.log("undefined", cleared, parseInt === undefined,
                Object.getOwnPropertyDescriptor(target, key)!.value === undefined);
            let abrupt = false;
            const throwing: any = { get value() { throw new Error("descriptor"); } };
            try { Reflect.defineProperty(target, key, throwing); }
            catch (error) { abrupt = error instanceof Error && error.message === "descriptor"; }
            console.log("abrupt", abrupt, parseInt === undefined);
        } finally {
            Object.defineProperty(target, key, original);
        }
        console.log("restored", parseInt("12", 10), Object.getOwnPropertyDescriptor(target, key)!.value === original.value);
        const ordinary: any = {};
        Object.defineProperty(ordinary, "locked", { value: 3, writable: false, configurable: false });
        console.log("refused", Reflect.defineProperty(ordinary, "locked", { value: 4 }), ordinary.locked);
        """.ReplaceLineEndings("\n") + "\n";

    internal static readonly string ShadowedSource = """
        function probe(value: unknown) {
            class Error { code = 1; }
            if (value instanceof Error) {
                console.log(value.message);
            }
        }
        """.ReplaceLineEndings("\n") + "\n";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", Source];
    }

    public static IEnumerable<object[]> CollectedCases() => Cases()
        .SelectMany(row => new[] { "Recovery", "Modules" }
            .Select(collection => new object[] { row[0], row[1], collection }));

    [Theory, MemberData(nameof(CollectedCases))]
    public void GuardedUnknownCatchHasNoStrictDiagnostics(
        string name, string source, string collection)
    {
        Assert.Equal("public", name);
        Assert.True(collection is "Recovery" or "Modules");
        if (collection == "Recovery")
        {
            var result = new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source));
            Assert.Empty(result.Diagnostics);
        }
        else
        {
            var path = Path.Combine(Path.GetTempPath(),
                "unknown-catch-instanceof-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Empty(checker.GetDiagnostics());
        }
    }

    public static IEnumerable<object[]> ShadowedCases()
    {
        yield return ["shadowed-error", ShadowedSource];
    }

    public static IEnumerable<object[]> ShadowedCollectedCases() => ShadowedCases()
        .SelectMany(row => new[] { "Recovery", "Modules" }
            .Select(collection => new object[] { row[0], row[1], collection }));

    [Theory, MemberData(nameof(ShadowedCollectedCases))]
    public void ShadowedErrorRetainsMissingMemberDiagnostic(
        string name, string source, string collection)
    {
        Assert.Equal("shadowed-error", name);
        Assert.True(collection is "Recovery" or "Modules");
        IReadOnlyList<SharpTS.Diagnostics.Diagnostic> diagnostics;
        if (collection == "Recovery")
        {
            diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        }
        else
        {
            var path = Path.Combine(Path.GetTempPath(),
                "shadowed-error-instanceof-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TS2339", diagnostic.TsCode);
        Assert.Equal(4, diagnostic.Line);
        Assert.Contains("message", diagnostic.Message);
        Assert.Contains("Error", diagnostic.Message);
    }
}
