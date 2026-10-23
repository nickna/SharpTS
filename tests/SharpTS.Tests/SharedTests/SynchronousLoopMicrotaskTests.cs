using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class SynchronousLoopMicrotaskTests
{
    public static IEnumerable<object[]> Cases()
    {
        (string Name, string Source)[] loops =
        [
            ("for", "for (let index = 0; index < 1; index++) {}"),
            ("for-continue", "for (let index = 0; index < 2; index++) { continue; }"),
            ("while", "let index = 0; while (index++ < 1) {}"),
            ("do-while", "let index = 0; do { index++; } while (index < 1);"),
            ("for-in", "for (const key in { value: 1 }) {}"),
            ("for-of", "for (const value of [1]) {}"),
        ];
        foreach (var (name, loop) in loops)
        {
            foreach (string context in new[] { "script", "function", "async-without-await" })
            {
                string body = $$"""
                    queueMicrotask(() => console.log("job"));
                    {{loop}}
                    console.log("sync");
                    """;
                yield return [name + "-" + context, Wrap(context, body), "sync\ntail\njob\n"];
            }
        }
        yield return ["long-for-loop", """
            queueMicrotask(() => console.log("job"));
            for (let index = 0; index < 100000; index++) {}
            console.log("sync");
            """, "sync\njob\n"];
        yield return ["long-while-loop", """
            queueMicrotask(() => console.log("job"));
            let index = 0;
            while (index++ < 100000) {}
            console.log("sync");
            """, "sync\njob\n"];
        foreach (string context in new[] { "script", "function", "async-without-await" })
        {
            const string body = """
                const state = { called: false };
                const value: any = { then(resolve: any) { state.called = true; resolve(47); } };
                const pending = Promise.resolve(value);
                for (let index = 0; index < 1; index++) {}
                console.log("sync", state.called);
                const observed = pending.then(result => console.log("resolved", result));
                """;
            yield return ["thenable-" + context, Wrap(context, body), "sync false\ntail\nresolved 47\n"];
        }
        yield return ["thenable-and-due-timer", """
            setTimeout(() => console.log("timer"), 0);
            const value: any = { then(resolve: any) {
                resolve({ then(again: any) { again(47); } });
            } };
            const observed = Promise.resolve(value).then(result => console.log("resolved", result));
            Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 30);
            for (let index = 0; index < 2; index++) {}
            console.log("sync");
            """, "sync\nresolved 47\ntimer\n"];
        yield return ["timer-created-jobs-precede-next-due-timer", """
            let done = false;
            setTimeout(() => queueMicrotask(() => { done = true; console.log("job"); }), 0);
            setTimeout(() => console.log(done), 0);
            Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 30);
            for (let index = 0; index < 2; index++) {}
            console.log("sync");
            """, "sync\njob\ntrue\n"];
        yield return ["real-await-remains-a-checkpoint", """
            async function run() {
                queueMicrotask(() => console.log("job"));
                for (let index = 0; index < 1; index++) {}
                console.log("before");
                await new Promise<void>(resolve => setTimeout(resolve, 0));
                console.log("after");
            }
            const pending = run();
            console.log("tail");
            """, "before\ntail\njob\nafter\n"];
    }

    private static string Wrap(string context, string body) => context switch
    {
        "function" => "function run() {\n" + body + "\n}\nrun();\nconsole.log('tail');",
        "async-without-await" => "async function run() {\n" + body + "\n}\nconst pending = run();\nconsole.log('tail');",
        _ => body + "\nconsole.log('tail');",
    };

    public static IEnumerable<object[]> ModeCases() => Cases()
        .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Interpreted })
        .Concat(Cases()
            .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Compiled }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SynchronousLoopsDoNotIntroduceMicrotaskCheckpoints(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
