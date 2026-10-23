using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class RegExpOwnSetterReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["regexp-own-setter-receiver", """
            for (const target of [/a/g, {}] as any[]) {
                let stored = 0, observed: any = undefined;
                Object.defineProperty(target, "slot", {
                    get() { return stored; },
                    set(value: number) { stored = value; observed = this; },
                    configurable: true
                });
                target.slot = 7;
                console.log(stored, observed === target, target.slot);
            }
            """ + "\n", "7 true 7\n7 true 7\n"];
        yield return ["regexp-own-dot-setter-completion-and-receiver", """
            const target: any = /a/g;
            let stored: any = 0, calls = 0, reads = 0, receiver = false;
            const events: string[] = [];
            const getter = function() { reads++; return stored; };
            const setter = function(this: any, value: number) {
                'use strict';
                events.push('set:' + value);
                calls++;
                receiver = this === target;
                stored = value;
                return 99;
            };
            Object.defineProperty(target, 'slot', { get: getter, set: setter, configurable: true });
            function selected(): any { events.push('receiver'); return target; }
            function rhs(): number { events.push('rhs'); return 7; }
            const result: any = selected().slot = rhs();
            console.log('strict', result, stored, calls, receiver, reads, events.join(','));
            console.log('read', target.slot, reads);
            const token = {};
            Object.defineProperty(target, 'only', {
                set: function(this: any, value: any) { calls++; receiver = this === target; stored = value; },
                configurable: true
            });
            target.only = token;
            console.log('setter-only', stored === token, calls, receiver, target.only === undefined);
            Object.defineProperty(target, 'boom', {
                get() { reads++; return 0; },
                set: function(this: any, value: number) { receiver = this === target; throw token; },
                configurable: true
            });
            let caught = false;
            try { target.boom = 9; } catch (error) { caught = error === token; }
            console.log('abrupt', caught, receiver, reads);
            target.lastIndex = 0;
            console.log('native', target.source, target.global, target.lastIndex, target.test('a'));
            """ + "\n", "strict 7 7 1 true 0 receiver,rhs,set:7\nread 7 1\nsetter-only true 2 true true\nabrupt true true 1\nnative a true 0 true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OwnRegExpDotSettersReceiveTheirOriginalInstance(
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
