using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class AnnotatedAsyncReturnDiagnosticTests
{
    private const string Source = """
        async function wrongPromise(): Promise<number> { return new Promise<string>(resolve => resolve("wrong")); }
        async function wrongScalar(): Promise<number> { return "wrong"; }
        function wrongSync(): number { return new Promise<number>(resolve => resolve(7)); }
        async function wrongUnion(value: number | Promise<string>): Promise<number> { return value; }
        async function wrongOppositePromise(): Promise<string> { return new Promise<number>(resolve => resolve(7)); }
        """ + "\n";

    [Theory]
    [InlineData("Recovery")]
    [InlineData("Modules")]
    public void IncompatibleFulfillmentAndScalarReturnsKeepPreciseDiagnostics(string collection)
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
            var path = Path.Combine(Path.GetTempPath(), "annotated-async-return-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = Source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }

        Assert.Equal("TS2322:1,TS2322:2,TS2322:3,TS2322:4,TS2322:5".Split(',').OrderBy(value => value, StringComparer.Ordinal),
            diagnostics.Select(item => (item.TsCode ?? "") + ":" + item.Line)
                .OrderBy(value => value, StringComparer.Ordinal));
    }
}
