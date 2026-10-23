using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassPrototypeComputedWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-sync-computed-class-prototype-inherited-setter", """
            let stored: any = 0;
            class Base { set value(next: any) { stored = next; } }
            class Derived extends Base {}
            const target: any = Derived.prototype, key: string = "value";
            target[key] = 7;
            console.log(stored, Object.hasOwn(target, "value"));
            """, "7 false\n"];
        yield return ["dot-record-and-own-data-shadow-preservation", """
            const state = { calls: 0, stored: 0, receiver: null as any };
            class Base {
                set value(next: number) { state.calls++; state.stored = next; state.receiver = this; }
            }
            class Derived extends Base {}
            const prototype: any = Derived.prototype;
            const key: string = 'value';
            console.log(prototype.value = 3, state.stored, state.calls, state.receiver === prototype, Object.hasOwn(prototype, key));
            console.log(prototype[key] = 7, state.stored, state.calls, state.receiver === prototype, Object.hasOwn(prototype, key));
            Object.defineProperty(prototype, key, { value: 11, writable: true, enumerable: false, configurable: true });
            console.log(prototype[key] = 13, prototype[key], state.stored, state.calls);
            const own = Object.getOwnPropertyDescriptor(prototype, key)!;
            console.log(own.writable, own.enumerable, own.configurable);
            const recordState = { calls: 0, stored: 0, receiver: null as any };
            const recordParent = { set value(next: number) { recordState.calls++; recordState.stored = next; recordState.receiver = this; } };
            const record: any = Object.create(recordParent);
            console.log(record.value = 5, record[key] = 9, recordState.stored, recordState.calls, recordState.receiver === record, Object.hasOwn(record, key));
            """, "3 3 1 true false\n7 7 2 true false\n13 13 7 2\ntrue false true\n5 9 9 2 true false\n"];
        yield return ["nearest-readonly-and-getter-only-block-computed-writes", """
            const state = { far: 0, gets: 0 };
            class Grand {}
            class Middle extends Grand {}
            class Leaf extends Middle {}
            const far: any = Grand.prototype, nearest: any = Middle.prototype, target: any = Leaf.prototype;
            Object.defineProperty(far, 'locked', { set(next: number) { state.far++; }, configurable: true });
            Object.defineProperty(far, 'getterOnly', { set(next: number) { state.far++; }, configurable: true });
            Object.defineProperty(nearest, 'locked', { value: 4, writable: false, configurable: true });
            Object.defineProperty(nearest, 'getterOnly', { get() { state.gets++; return 5; }, configurable: true });
            function sloppyWrite(value: any, key: string) { return value[key] = 9; }
            function strictWrite(value: any, key: string) { 'use strict'; return value[key] = 11; }
            console.log(sloppyWrite(target, 'locked'), sloppyWrite(target, 'getterOnly'), state.far, state.gets,
                Object.hasOwn(target, 'locked'), Object.hasOwn(target, 'getterOnly'));
            for (const key of ['locked', 'getterOnly']) {
                try { strictWrite(target, key); console.log(false); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            console.log(state.far, state.gets, Object.hasOwn(target, 'locked'), Object.hasOwn(target, 'getterOnly'));
            console.log(target.locked, target.getterOnly, state.gets);
            """, "9 9 0 0 false false\ntrue\ntrue\n0 0 false false\n4 5 1\n"];
        yield return ["pending-computed-write-keeps-reference-and-setter-abrupt-identity", """
            const trace: string[] = [];
            const marker = { tag: 'setter' };
            const state = { calls: 0, receiver: null as any, stored: 0 };
            class Base {
                set value(next: number) {
                    trace.push('set:' + next);
                    state.calls++;
                    state.receiver = this;
                    state.stored = next;
                    throw marker;
                }
            }
            class First extends Base {}
            class Second extends Base {}
            const first: any = First.prototype, second: any = Second.prototype;
            let current: any = first;
            let release: any;
            const pending = new Promise<number>(resolve => { release = resolve; });
            function target(): any { trace.push('receiver'); return current; }
            function key(): string { trace.push('key'); return 'value'; }
            function rhs() { trace.push('rhs'); return pending; }
            async function run() {
                try { target()[key()] = await rhs(); console.log(false); }
                catch (error) { console.log(error === marker); }
                console.log(trace.join(','), state.calls, state.stored, state.receiver === first,
                    Object.hasOwn(first, 'value'), Object.hasOwn(second, 'value'));
            }
            const operation = run();
            console.log(trace.join(','), state.calls);
            current = second;
            release(7);
            """, "receiver,key,rhs 0\ntrue\nreceiver,key,rhs,set:7 1 7 true false false\n"];
    }

    public static IEnumerable<object[]> SavedCases() => Cases().Concat(
        ClassPrototypeInheritedWriteTests.Cases().Where(row =>
            (string)row[0] == "computed-async-write-retains-reference-and-evaluation-order"));

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedPrototypeWritesUseNearestDescriptorsAndOriginalReceiver(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
