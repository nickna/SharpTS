using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ValueFlowExceptionOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedTryLocalWritesRemainOwnedByTheirLexicalBinding(bool modules)
        => Assert.Empty(Check("""
            let value: string | number = 'outer';
            try {
                let local: string | number = 'inner';
                try { local = 1; } catch {}
                {
                    let value: string | number = 'shadow';
                    try { value = 2; } catch {}
                }
            } catch {}
            const unchanged: string = value;
            """, modules));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedTryWritesToAnActualOuterBindingStillReachItsCatch(bool modules)
        => Assert.Equal("TS2322", Assert.Single(Check("""
            let value: string | number = 'outer';
            try { try { value = 1; } catch {} } catch {}
            const wrong: string = value;
            """, modules)).TsCode);

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "exception-flow-owner-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
