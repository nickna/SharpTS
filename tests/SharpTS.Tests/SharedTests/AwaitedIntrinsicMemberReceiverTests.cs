using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AwaitedIntrinsicMemberReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-record-pending-call-retains-receiver-callee-and-once-order", """
            const first: any = {}, other: any = {}, events: string[] = [];
            let selected = first, receivers = 0, reads = 0, calls = 0;
            let current: any = function(this: any, left: number, right: number) {
                calls++; events.push('call:' + (this === first)); return left + right;
            };
            Object.defineProperty(first, 'method', {
                configurable: true,
                get() { reads++; events.push('get:' + (this === first)); return current; }
            });
            function receiver(): any { receivers++; events.push('receiver'); return selected; }
            async function run() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                function argument(): Promise<number> { events.push('argument'); return pending; }
                const change = async () => receiver().method(await argument(), 5);
                const task = change();
                console.log('pending', receivers, reads, calls, events.join(','));
                selected = other; current = () => 99; release(4);
                console.log('settled', await task, receivers, reads, calls, events.join(','));
            }
            run().catch((error: any) => console.log('unexpected', error && error.name));
            """, "pending 1 1 0 receiver,get:true,argument\nsettled 9 1 1 1 receiver,get:true,argument,call:true\n"];
        yield return ["math-pending-argument-retains-live-callee-receiver-and-later-argument-order", """
            const target: any = Math, events: string[] = [];
            const original = Object.getOwnPropertyDescriptor(target, 'min')!;
            let reads = 0, calls = 0;
            let current: any = function(this: any, left: number, right: number) {
                calls++; events.push('call:' + (this === target)); return left + right;
            };
            async function run() {
                try {
                    Object.defineProperty(target, 'min', {
                        configurable: true,
                        get() { reads++; events.push('get:' + (this === target)); return current; }
                    });
                    let release: (value: number) => void = () => {};
                    const pending = new Promise<number>(resolve => { release = resolve; });
                    function argument(): Promise<number> { events.push('argument'); return pending; }
                    function second(): number { events.push('second'); return 5; }
                    const task = (async () => Math.min(await argument(), second()))();
                    console.log('pending', reads, calls, events.join(','));
                    current = () => 99; release(4);
                    console.log('settled', await task, reads, calls, events.join(','));
                } finally { Object.defineProperty(target, 'min', original); }
                console.log(Math.min(1, 2));
            }
            run().catch((error: any) => console.log('unexpected', error && error.name));
            """, "pending 1 0 get:true,argument\nsettled 9 1 1 get:true,argument,second,call:true\n1\n"];
        // Strict TypeScript needs this annotation; SavedCases retains the unchanged original too.
        yield return ["math-exact-explicit-this-strict-companion", """
            const target: any = Math;
            const original = Object.getOwnPropertyDescriptor(target, "min")!;
            const events: string[] = [];
            async function run() {
                let result: any;
                try {
                    Object.defineProperty(target, "min", {
                        configurable: true,
                        get() {
                            events.push("get:" + (this === target));
                            return function(this: any, left: number, right: number) {
                                events.push("call:" + (this === target));
                                return left + right;
                            };
                        }
                    });
                    async function argument() {
                        events.push("argument");
                        await Promise.resolve(0);
                        Object.defineProperty(target, "min", { value: () => 99, writable: true, configurable: true });
                        return 4;
                    }
                    result = Math.min(await argument(), 5);
                } finally { Object.defineProperty(target, "min", original); }
                console.log(result, events.join(","));
                console.log(Math.min(1, 2));
            }
            run().catch((error: any) => console.log("unexpected", error && error.name));
            """, "9 get:true,argument,call:true\n1\n"];
    }

    public static IEnumerable<object[]> SavedCases()
    {
        foreach (var row in Cases()) yield return row;
        yield return IntrinsicStaticCallBindingTests.Cases().Single(row =>
            (string)row[0] == "async-getter-before-awaited-argument-and-receiver");
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AwaitedArgumentsPreserveTheSelectedCalleeAndOriginalReceiver(
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
