using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    public static IEnumerable<object[]> AsyncArrowLoopCellReadCases()
    {
        yield return ["existing-async-arrow-mutate-restore-issue", """
            const run = async () => {
                const g: any[] = [];
                for (let i = 0; i < 3; i++) { i = i + 10; g.push(() => i); i = i - 10; }
                console.log(g.map((f: any) => f()).join(","));
            };
            run();
            """, "0,1,2\n"];
        yield return ["loop-cell-shadows-private-async-expression-name", """
            const run: any = async function i(): Promise<void> {
                const identity = i, callbacks: any[] = [];
                for (let i = 0; i < 3; i++) { i = i + 10; callbacks.push(() => i); i = i - 10; }
                console.log(callbacks.map((read: any) => read()).join(','));
                console.log(i === identity);
                const read = () => i, readAsync = async () => i;
                console.log(read() === identity);
                console.log((await readAsync()) === identity);
            };
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "0,1,2\ntrue\ntrue\ntrue\n"];
        yield return ["async-declaration-loop-cell-preservation", """
            async function run() {
                const g: any[] = [];
                for (let i = 0; i < 3; i++) { i = i + 10; g.push(() => i); i = i - 10; }
                console.log(g.map((f: any) => f()).join(','));
            }
            run();
            """, "0,1,2\n"];
    }

    [Theory, MemberData(nameof(AsyncArrowLoopCellReadCases))]
    public void Isolated_Issue2771AsyncArrowLoopCellReads_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("async-loop-cell-read-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
