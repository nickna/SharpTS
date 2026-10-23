using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class CanonicalArrayStringIndexReadDiagnosticTests
{
    internal const string Source = """
        const numbers: number[] = [4, 8];
        const wrongNumber: string = numbers['0'];
        const words: readonly string[] = ['left', 'right'];
        const wrongReadonly: number = words['0'];
        function wrongUnion(values: number[] | string[]) {
            const result: boolean = values['0'];
        }
        function wrongKey(values: number[], key: '0' | '1') {
            const result: boolean = values[key];
        }
        console.log(numbers['missing']);
        """ + "\n";

    [Theory]
    [InlineData("Recovery")]
    [InlineData("Modules")]
    public void ElementReadsRetainPreciseTypesAndRejectArbitraryNames(string collection)
    {
        IReadOnlyList<Diagnostic> diagnostics;
        if (collection == "Recovery")
        {
            diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(Source)).Diagnostics;
        }
        else
        {
            Assert.Equal("Modules", collection);
            var path = Path.Combine(Path.GetTempPath(), "canonical-array-string-read-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = Source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }

        Assert.Equal(5, diagnostics.Count);
        // The invalid named array key must retain the array-specific diagnostic.
        Assert.Equal("TS7015", Assert.Single(diagnostics, diagnostic => diagnostic.Line == 11).TsCode);
        Assert.Equal("TS2322:2,TS2322:4,TS2322:6,TS2322:9".Split(',').OrderBy(value => value, StringComparer.Ordinal),
            diagnostics.Where(item => item.Line != 11)
                .Select(item => (item.TsCode ?? "") + ":" + item.Line)
                .OrderBy(value => value, StringComparer.Ordinal));
    }
}
