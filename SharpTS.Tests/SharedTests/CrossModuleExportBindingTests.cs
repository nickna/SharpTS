using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Same-module exported declarations are lexical bindings as well as exports. Compiled member
/// bodies must therefore read and write the module's canonical live export fields, matching the
/// interpreter and JavaScript module semantics.
/// </summary>
public sealed class CrossModuleExportBindingTests
{
    [Theory, ModeData]
    public void ExportedFunctionReadsExportedConstByBareName(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["model.ts"] = """
                export const initial = { value: 41 };
                export function next(): number { return initial.value + 1; }
                """,
            ["main.ts"] = """
                import { next } from './model';
                console.log(next());
                """
        };

        Assert.Equal("42\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void ExportedFunctionReadsAndWritesExportedLetAsLiveBinding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["counter.ts"] = """
                export let value = 1;
                export function increment(): number { value = value + 1; return value; }
                export function current(): number { return value; }
                """,
            ["main.ts"] = """
                import { increment, current } from './counter';
                console.log(increment(), current(), increment(), current());
                """
        };

        Assert.Equal("2 2 3 3\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void LocalAndImportedBindingsTakePrecedenceOverExportFieldFallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["source.ts"] = "export const shared = 'imported';",
            ["model.ts"] = """
                import { shared } from './source';
                export const exportedOnly = 'module';
                export function read(): string {
                    const exportedOnly = 'local';
                    return shared + ':' + exportedOnly;
                }
                """,
            ["main.ts"] = """
                import { read } from './model';
                console.log(read());
                """
        };

        Assert.Equal("imported:local\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Fact]
    public void CompiledExportBindingScenarioExecutes()
    {
        var files = new Dictionary<string, string>
        {
            ["state.ts"] = """
                export const seed = 4;
                export let value = seed;
                export function advance(): number { value += seed; return value; }
                """,
            ["main.ts"] = """
                import { advance } from './state';
                console.log(advance());
                """
        };

        Assert.Equal("8\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }
}
