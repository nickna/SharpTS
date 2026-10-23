using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class QualifiedLiveGlobalParseIntTests
{
    public static IEnumerable<object[]> Cases()
    {
        var original = GlobalParseIntBindingTests.Cases().Single(row => (string)row[0] == "issue");
        // Preserve the old whole CRLF companion and its historical final CRLF.
        yield return ["existing-global-binding-issue-companion", (string)original[1] + "\r\n", original[2]];
        yield return ["public-qualified-live-replacement", """
            const original = globalThis.parseInt;
            const replacement = function (_text: string, _radix?: number): number { return null as any; };
            globalThis.parseInt = replacement;
            console.log(parseInt === replacement, globalThis.parseInt === replacement,
                parseInt("7", 10), globalThis.parseInt("7", 10));
            globalThis.parseInt = original;
            """ + "\n", "true true null null\n"];
        yield return ["qualified-live-lookup-receiver-and-abrupt", """
            const target: any = globalThis;
            const original = Object.getOwnPropertyDescriptor(target, 'parseInt')!;
            const token = {}, marker = {}, events: string[] = [];
            let reads = 0, argumentsRead = 0, receiver = true;
            function first() {
                argumentsRead++; events.push('first');
                Object.defineProperty(target, 'parseInt', { value: () => 99, writable: true, configurable: true });
                return '7';
            }
            function second() { argumentsRead++; events.push('second'); return 10; }
            try {
                Object.defineProperty(target, 'parseInt', {
                    configurable: true,
                    get() {
                        reads++; events.push('get:' + (this === target));
                        return function(this: any, text: string, radix?: number): number {
                            'use strict'; receiver = receiver && this === target;
                            events.push('call:' + text + ':' + radix); return null as any;
                        };
                    }
                });
                const value: any = globalThis.parseInt(first(), second());
                console.log('lookup', value === null, reads, argumentsRead, receiver, events.join(','));
                argumentsRead = 0;
                Object.defineProperty(target, 'parseInt', { configurable: true, get() { throw token; } });
                try { globalThis.parseInt(first(), second()); }
                catch (error) { console.log('getter', error === token, argumentsRead); }
                Object.defineProperty(target, 'parseInt', {
                    configurable: true, writable: true,
                    value: function(this: any, _text: string, _radix?: number): number {
                        'use strict'; receiver = receiver && this === target; throw token;
                    }
                });
                function argument() { argumentsRead++; return '7'; }
                try { globalThis.parseInt(argument(), 10); }
                catch (error) { console.log('call', error === token, argumentsRead, receiver); }
                target.parseInt = 5;
                argumentsRead = 0;
                try { globalThis.parseInt(argument(), 10); }
                catch (error) { console.log('noncallable', error instanceof TypeError, argumentsRead); }
                target.parseInt = function(this: any, _text: string, _radix?: number): number { 'use strict'; return this; };
                const held = globalThis.parseInt;
                console.log('receiver', (globalThis.parseInt('7', 10) as any) === target,
                    (parseInt('7', 10) as any) === undefined, (held('7', 10) as any) === undefined,
                    (held.call(marker, '7', 10) as any) === marker);
            } finally { Object.defineProperty(target, 'parseInt', original); }
            console.log('restored', globalThis.parseInt('12', 10), parseInt('12', 10), Number.parseInt('12', 10));
            """ + "\n", "lookup true 1 2 true get:true,first,second,call:7:10\ngetter true 0\ncall true 1 true\nnoncallable true 1\nreceiver true true true true\nrestored 12 12 12\n"];
        yield return ["qualified-pending-argument-retains-live-callee", """
            const target: any = globalThis;
            const original = Object.getOwnPropertyDescriptor(target, 'parseInt')!;
            const events: string[] = [];
            let reads = 0, calls = 0, receiver = true;
            async function run() {
                let release: (value: string) => void = () => {};
                const pending = new Promise<string>(resolve => { release = resolve; });
                let current: any = function(this: any, text: string, radix: number): number {
                    'use strict'; calls++; receiver = receiver && this === target;
                    events.push('call:' + text + ':' + radix); return 17;
                };
                try {
                    Object.defineProperty(target, 'parseInt', {
                        configurable: true,
                        get() { reads++; events.push('get:' + (this === target)); return current; }
                    });
                    function argument() { events.push('argument'); return pending; }
                    const task = (async () => globalThis.parseInt(await argument(), 10))();
                    console.log('pending', reads, calls, events.join(','));
                    current = () => 99;
                    Object.defineProperty(target, 'parseInt', { value: current, writable: true, configurable: true });
                    release('7');
                    console.log('settled', await task, reads, calls, receiver, events.join(','));
                } finally { Object.defineProperty(target, 'parseInt', original); }
                console.log('restored', globalThis.parseInt('12', 10));
            }
            run().catch((error: any) => console.log('unexpected', error && error.name));
            """ + "\n", "pending 1 0 get:true,argument\nsettled 17 1 1 true get:true,argument,call:7:10\nrestored 12\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void QualifiedCallsUseTheSelectedLivePropertyAndReceiver(
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
