using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ComputedOwnInstanceSetterTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["regexp-error-class-computed-own-setters", """
            class Box {}
            for (const target of [/a/g, new Error("neutral"), new Box(), {}] as any[]) {
                let stored = 0, calls = 0;
                Object.defineProperty(target, "slot", {
                    get() { return stored; },
                    set(value: number) { stored = value; calls++; },
                    configurable: true
                });
                const key: string = "slot";
                console.log(target[key] = 7, stored, calls, target.slot);
                console.log(target.slot = 8, stored, calls, target.slot);
            }
            """ + "\n", "7 7 1 7\n8 8 2 8\n7 7 1 7\n8 8 2 8\n7 7 1 7\n8 8 2 8\n7 7 1 7\n8 8 2 8\n"];
        yield return ["closed-290-declared-setter-control", """
            class Counter {
              private _n: number = 0;
              get n(): number { return this._n; }
              set n(v: number) { this._n = v * 2; }
            }
            const a = new Counter();
            (a as any)["n"] = 5;
            console.log((a as any)["n"]);   // expect 10
            """ + "\n", "10\n"];
        yield return ["computed-own-setter-order-receiver-and-abrupt", """
            class Box {}
            const targets: any[] = [/a/g, new Error('neutral'), new Box(), {}];
            const names = ['regexp', 'error', 'class', 'record'];
            for (let i = 0; i < targets.length; i++) {
                const target: any = targets[i], events: string[] = [];
                let stored = 0, reads = 0, calls = 0, receiver = false;
                Object.defineProperty(target, 'slot', {
                    get() { reads++; return stored; },
                    set: function(this: any, value: number) {
                        'use strict';
                        events.push('set'); calls++; receiver = this === target; stored = value;
                        return 99;
                    },
                    configurable: true
                });
                function selected(): any { events.push('receiver'); return target; }
                function key(): string { events.push('key'); return 'slot'; }
                function rhs(): number { events.push('rhs'); return 7; }
                const assigned: any = selected()[key()] = rhs();
                console.log(names[i], assigned, stored, calls, receiver, reads, events.join(','));
                const token = {};
                Object.defineProperty(target, 'boom', {
                    get() { reads++; return 0; },
                    set: function(this: any, value: number) { receiver = this === target; throw token; },
                    configurable: true
                });
                let caught = false;
                const throwingKey: string = 'boom';
                try { target[throwingKey] = 9; } catch (error) { caught = error === token; }
                console.log(names[i], 'abrupt', caught, receiver, reads);
            }
            """ + "\n", "regexp 7 7 1 true 0 receiver,key,rhs,set\nregexp abrupt true true 0\nerror 7 7 1 true 0 receiver,key,rhs,set\nerror abrupt true true 0\nclass 7 7 1 true 0 receiver,key,rhs,set\nclass abrupt true true 0\nrecord 7 7 1 true 0 receiver,key,rhs,set\nrecord abrupt true true 0\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedWritesInvokeInstalledOwnSetters(
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
