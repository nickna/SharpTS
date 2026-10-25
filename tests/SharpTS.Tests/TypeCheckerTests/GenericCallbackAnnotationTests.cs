using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericCallbackAnnotationTests
{
    public static IEnumerable<object[]> IncompatibleCallbacks()
    {
        foreach (string call in new[]
        {
            "same<number>((x: number) => {}, (x: string) => {});",
            "same<number>((x: number) => 1, (x: string) => 2);",
            "same((x: number) => {}, (x: string) => {});",
            "same<number>((x: number) => {}, function (x: string) {});",
            "same<number>((x: number) => {}, ((x: string) => {}));",
            "const wrong = (x: string) => {}; same<number>((x: number) => {}, wrong);"
        })
            foreach (bool modules in new[] { false, true })
                yield return [call, modules];
    }

    [Theory, MemberData(nameof(IncompatibleCallbacks))]
    public void ExplicitCallbackParametersRemainPartOfTheArgumentType(string call, bool modules)
    {
        var diagnostics = Check("""
            declare function same<T>(first: (value: T) => void, second: (value: T) => void): void;
            """ + "\n" + call, modules);
        Assert.Equal("TS2345", Assert.Single(diagnostics).TsCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextualParametersAndValueReturningVoidCallbacksRemainAccepted(bool modules)
    {
        Assert.Empty(Check("""
            declare function use<T>(value: T, callback: (value: T) => void): void;
            use<number>(1, value => value + 1);
            use<number>(1, (value: number | string) => 2);
            declare function map<T>(value: T, callback: (value: T) => T): T;
            const result: number = map<number>(1, value => value + 1);
            const inferred: number = map(1, value => value + 1);
            declare function nested<T>(value: T, options: { callback: (value: T) => void }): void;
            nested<number>(1, { callback: value => value + 1 });
            """, modules));
    }

    [Fact]
    public void RecontextualizedCallbackReturnsAreStillValidated()
    {
        var diagnostics = Check("""
            declare function map<T>(value: T, callback: (value: T) => T): T;
            map<number>(1, value => "wrong");
            """, false);
        Assert.Equal("TS2322", Assert.Single(diagnostics).TsCode);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "generic-callback-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
