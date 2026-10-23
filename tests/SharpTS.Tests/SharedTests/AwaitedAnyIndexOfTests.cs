using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AwaitedAnyIndexOfTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["any-block-arrow-control", """
            const input: any = "abc";
            const task = (async () => { return input.indexOf(await Promise.resolve("b"), 0); })();
            task.then(value => console.log(value)).catch((error: any) => console.log("unexpected", error && error.name));
            """, "1\n"];
        yield return ["async-declaration-inherited-call-no-awaited-argument", """
            async function run() {
                await Promise.resolve(0);
                const prototype: any = String.prototype, object: any = Object.prototype;
                const original = Object.getOwnPropertyDescriptor(prototype, "indexOf")!;
                const inherited = Object.getOwnPropertyDescriptor(object, "indexOf");
                const input: any = "abc";
                try {
                    delete prototype.indexOf;
                    object.indexOf = function(this: any, _needle: string, _position?: number): number {
                        "use strict";
                        return this === "abc" ? 41 : 0;
                    };
                    console.log(input.indexOf("b", 0));
                } finally {
                    Object.defineProperty(prototype, "indexOf", original);
                    if (inherited) Object.defineProperty(object, "indexOf", inherited); else delete object.indexOf;
                }
                console.log(input.indexOf("b", 0));
            }
            run().catch((error: any) => console.log("unexpected", error && error.name));
            """ + "\n", "41\n1\n"];
        yield return ["awaited-any-array-indexof-order-and-abrupt", """
            const input: any = [0, 7, 0, 7];
            const native: any = input.indexOf;
            const events: string[] = [], token = {};
            let argumentsRead = 0;
            async function run() {
                console.log('native', input.indexOf(await Promise.resolve(7), 2));
                Object.defineProperty(input, 'indexOf', {
                    configurable: true,
                    get() {
                        events.push('get:' + (this === input));
                        return function(this: any, value: any, position: any, _extra: any): number {
                            'use strict'; events.push('call:' + (this === input));
                            return native.call(this, value, position);
                        };
                    }
                });
                function needle() { argumentsRead++; events.push('needle'); return Promise.resolve(7); }
                function position() { argumentsRead++; events.push('position'); return Promise.resolve(2); }
                function extra() { argumentsRead++; events.push('extra'); return 4; }
                console.log('live', input.indexOf(await needle(), await position(), extra()), argumentsRead, events.join(','));
                argumentsRead = 0;
                Object.defineProperty(input, 'indexOf', { configurable: true, get() { throw token; } });
                try { input.indexOf(await needle(), 0); }
                catch (error) { console.log('getter', error === token, argumentsRead); }
                Object.defineProperty(input, 'indexOf', { configurable: true, writable: true, value: 5 });
                try { input.indexOf(await needle(), 0); }
                catch (error) { console.log('noncallable', error instanceof TypeError, argumentsRead); }
                Object.defineProperty(input, 'indexOf', { configurable: true, writable: true, value: native });
                console.log('restored', input.indexOf(await Promise.resolve(7), 2));
            }
            run().catch((error: any) => console.log('unexpected', error && error.name));
            """ + "\n", "native 3\nlive 3 3 get:true,needle,position,extra,call:true\ngetter true 0\nnoncallable true 1\nrestored 3\n"];
        yield return ["pending-inherited-call-preserves-receiver-and-callee", """
            async function run() {
                const prototype: any = String.prototype, object: any = Object.prototype;
                const original = Object.getOwnPropertyDescriptor(prototype, "indexOf")!;
                const inherited = Object.getOwnPropertyDescriptor(object, "indexOf");
                const events: string[] = [];
                let input: any = "abc";
                try {
                    delete prototype.indexOf;
                    object.indexOf = function(this: any, _needle: string, _position?: number): number {
                        "use strict";
                        events.push("call:" + (this === "abc"));
                        return 41;
                    };
                    let release: (value: string) => void = () => {};
                    const pending = new Promise<string>(resolve => { release = resolve; });
                    function argument() { events.push("argument"); return pending; }
                    const task = (async () => input.indexOf(await argument(), 0))();
                    console.log("pending", events.join(","));
                    input = "changed";
                    object.indexOf = () => 99;
                    release("b");
                    console.log("settled", await task, events.join(","));
                } finally {
                    Object.defineProperty(prototype, "indexOf", original);
                    if (inherited) Object.defineProperty(object, "indexOf", inherited); else delete object.indexOf;
                }
                console.log("abc".indexOf("b", 0));
            }
            run().catch((error: any) => console.log("unexpected", error && error.name));
            """ + "\n", "pending argument\nsettled 41 argument,call:true\n1\n"];
        yield return ["public-any-concise", """
            const input: any = "abc";
            const task = (async () => input.indexOf(await Promise.resolve("b"), 0))();
            task.then(value => console.log(value)).catch((error: any) => console.log("unexpected", error && error.name));
            """, "1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void IndexOfSuspensionsPreserveTheReceiverCalleeAndArgumentOrder(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
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
