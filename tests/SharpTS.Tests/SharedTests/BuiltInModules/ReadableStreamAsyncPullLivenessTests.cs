using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

[Collection("CompilationService")]
public sealed class ReadableStreamAsyncPullLivenessTests
{
    internal static IEnumerable<object[]> Cases()
    {
        yield return ["exact-existing-async-pull", """
            let i = 0;
            const rs = new ReadableStream({
                pull: async (c: any) => {
                    await Promise.resolve();
                    c.enqueue(i++);
                    if (i >= 2) c.close();
                }
            });
            const reader = rs.getReader();
            async function run() {
                const r1 = await reader.read();
                const r2 = await reader.read();
                const r3 = await reader.read();
                console.log(r1.value, r2.value, r3.done);
            }
            run();
            """.ReplaceLineEndings("\r\n"), "0 1 true\n"];
        yield return ["read-return-before-pull-jobs", """
            let release: any;
            const gate = new Promise<any>(resolve => { release = resolve; });
            const stream = new ReadableStream({
                async pull(controller: any) {
                    await gate;
                    controller.enqueue(7);
                    controller.close();
                }
            });
            const reader = stream.getReader();
            queueMicrotask(() => console.log('queued'));
            const pending = reader.read();
            console.log('returned');
            pending.then(result => console.log(result.value, result.done), (error: any) => console.log('unexpected', error));
            release(0);
            """.ReplaceLineEndings("\n") + "\n", "returned\nqueued\n7 false\n"];
        yield return ["forever-pending-pull-does-not-keep-process-alive", """
            const stream = new ReadableStream({
                pull() { return new Promise<void>(() => {}); }
            });
            stream.getReader().read();
            console.log('returned');
            """.ReplaceLineEndings("\n") + "\n", "returned\n"];
    }

    public static IEnumerable<object[]> Names() => Cases().Select(row => new object[] { row[0] });

    public static IEnumerable<object[]> ModeCases() => Names()
        .Where(row => (string)row[0] != "exact-existing-async-pull")
        .SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], mode }));

    internal static (string Source, string Expected) GetCase(string name)
    {
        var row = Cases().Single(row => (string)row[0] == name);
        return ((string)row[1], (string)row[2]);
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ReadReturnsBeforeAsyncPullJobsAndPendingWorkDoesNotKeepAlive(string name, ExecutionMode mode)
    {
        var (source, expected) = GetCase(name);
        AssertStrictSource(source);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }

    internal static void AssertStrictSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "readable-async-pull-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
