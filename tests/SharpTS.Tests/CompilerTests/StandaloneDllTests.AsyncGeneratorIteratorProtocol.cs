using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_AsyncGeneratorForAwaitValueGetterFailure()
        => AssertStandaloneAsyncGeneratorProtocol(AsyncGeneratorIteratorProtocolTests.ValueGetterFailureSource,
            "step\nafter catch\ntrue\n");

    [Fact]
    public void Isolated_AsyncGeneratorSyncDelegationRequests()
    {
        // Interpreter request forwarding and next accessors are tracked in
        // #3641 and #3643; this pins the emitted async-from-sync adapter.
        const string source = """
            const iterable = {
                [Symbol.iterator]() {
                    const iterator = {
                        count: 0,
                        get next() {
                            console.log("lookup next");
                            return function (sent) {
                                if (this !== iterator) throw new Error("wrong receiver");
                                if (++this.count === 1) return { value: Promise.resolve(7), done: false };
                                return { value: Promise.resolve(sent), done: true };
                            };
                        },
                        return(value) {
                            console.log("return", value, this === iterator);
                            return { value: Promise.resolve(value + 1), done: true };
                        },
                        throw(value) {
                            console.log("throw", value, this === iterator);
                            return { value: "handled:" + value, done: false };
                        }
                    };
                    return iterator;
                }
            };
            async function* run() { return yield* iterable; }
            async function* invalidResult() {
                try {
                    yield* { [Symbol.iterator]() { return { next() { return 1; } }; } };
                } catch (error) { yield error.name; }
            }
            async function show(result) { console.log(result.value, result.done); }
            async function main() {
                let generator = run();
                const first = await generator.next();
                console.log(first.value, first.done, first.value instanceof Promise);
                await show(await generator.next(9));
                generator = run();
                await generator.next();
                await show(await generator.return(10));
                generator = run();
                await generator.next();
                await show(await generator.throw("boom"));
                await show(await generator.next(12));
                await show(await invalidResult().next());
            }
            main();
            """;
        AssertStandaloneAsyncGeneratorProtocol(source,
            "lookup next\n7 false false\n9 true\nlookup next\nreturn 10 true\n11 true\n" +
            "lookup next\nthrow boom true\nhandled:boom false\n12 true\nTypeError false\n");
    }

    private static void AssertStandaloneAsyncGeneratorProtocol(string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("iterator-protocol.dll");
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
