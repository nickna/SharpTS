using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AmbientEnumRuntimeTests
{
    [Theory, ModeData]
    public void AmbientEnumsUseExistingGlobalBindings(ExecutionMode mode)
    {
        const string source = """
            (globalThis as any).E = { A: 7 };
            declare enum E { A }
            declare enum Missing { A }
            console.log(E.A, typeof E, typeof Missing, Object.hasOwn(globalThis, 'Missing'));
            """;
        Assert.Equal("7 object undefined false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AnExportedAmbientEnumDoesNotAddARuntimeExport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["declarations.ts"] = """
                export declare enum Erased { A }
                export const kept = 1;
                """,
            ["main.ts"] = """
                import * as declarations from './declarations';
                console.log(Object.hasOwn(declarations, 'Erased'), declarations.kept);
                """
        };
        Assert.Equal("false 1\n", TestHarness.RunModules(files, "./main.ts", mode));
    }
}
