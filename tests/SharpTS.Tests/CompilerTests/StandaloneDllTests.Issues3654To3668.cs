using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [InlineData("mutable-function", """
        function value(): number { return 1; }
        const read = () => value();
        value = () => 2;
        console.log(value(), read());
        """, "2 2\n")]
    [InlineData("enum-and-static-index", """
        const enum Values { A = 4 }
        class C { static 1: number = 0; }
        C[1] = 2;
        console.log(Values.A + 2, C[1]);
        """, "6 2\n")]
    [InlineData("async-arrow-identity", """
        function CatchError() {}
        const marker = new CatchError();
        const check = async () => { await Promise.resolve(); console.log(marker.constructor === CatchError); };
        check();
        """, "true\n")]
    [InlineData("inherited-factory", """
        const prototype = { get [Symbol.iterator]() {
            console.log('lookup', this === iterable);
            return function* () { yield Promise.resolve(7); return 9; };
        } };
        const iterable = Object.create(prototype);
        async function* run() { return yield* iterable; }
        async function main() {
            const generator = run();
            const first = await generator.next();
            const last = await generator.next();
            console.log(first.value, first.done, last.value, last.done);
        }
        main();
        """, "lookup true\n7 false 9 true\n")]
    [InlineData("uncaught-delegation", """
        const iterable = { [Symbol.iterator]() { return { next() { return { value: 1, done: false }; } }; } };
        async function* run() { yield* iterable; }
        async function main() {
            const generator = run();
            await generator.next();
            try { await generator.throw('marker'); } catch (error) { console.log(error.name); }
            console.log((await generator.next()).done);
        }
        main();
        """, "TypeError\ntrue\n")]
    [InlineData("primitive-result", """
        const iterable = { [Symbol.iterator]() { return {
            next() { return { value: 1, done: false }; }, return() { return 1; }
        }; } };
        async function* run() { try { yield* iterable; } catch (error) { yield error instanceof TypeError; } }
        async function main() {
            const generator = run(); await generator.next();
            console.log((await generator.return(9)).value);
        }
        main();
        """, "true\n")]
    [InlineData("acquired-generator-requests", """
        const marker = {};
        function* values() {
            try { try { yield 1; } catch (error) { console.log(error === marker); yield 2; } }
            finally { console.log('close'); }
        }
        const iterable = { [Symbol.iterator]() { return values(); } };
        function* run() { return yield* iterable; }
        const generator = run(); generator.next();
        console.log(generator.throw(marker).value);
        const result = generator.return(9); console.log(result.value, result.done);
        """, "true\n2\nclose\n9 true\n")]
    [InlineData("rejection-close", """
        const marker = {};
        const iterator = {
            next() { return { value: Promise.reject(marker), done: false }; },
            return() { console.log('close', arguments.length, this === iterator); return {}; }
        };
        async function* run() { yield* { [Symbol.iterator]() { return iterator; } }; }
        run().next().catch(error => console.log(error === marker));
        """, "close 0 true\ntrue\n")]
    [InlineData("nested-helper-capture", """
        async function check() {
            const marker = {};
            function step() { return { done: true, value: Promise.reject(marker) }; }
            const iterator = { next() { return step(); } };
            async function* run() { yield* { [Symbol.iterator]() { return iterator; } }; }
            try { await run().next(); } catch (error) { console.log(error === marker); }
        }
        check();
        """, "true\n")]
    public void Isolated_Issues3654To3668Regressions(string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var input = directory.CreateFile("main.ts", source);
        var output = directory.GetPath(name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{input}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
