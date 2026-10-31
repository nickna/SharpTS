using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BoundAccessorDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported", """
            'use strict';
            const read = function(this: any) { return this; };
            const receiver = {}, getter: any = read.bind(receiver), owner: any = function() {};
            console.log(typeof getter, getter() === receiver);
            Object.defineProperty(owner, 'value', { get: getter, configurable: true });
            console.log(owner.value === receiver);
            """, "function true\ntrue\n"];
        yield return ["nested-partial-getter", """
            const read = function(this: any, prefix: string) { return prefix + this.value; };
            const receiver = {value: 'kept'}, getter: any = read.bind(receiver, 'bound:').bind({value: 'ignored'});
            const owner: any = {};
            Object.defineProperty(owner, 'value', {get: getter, configurable: true});
            console.log(owner.value, Reflect.get(owner, 'value', {value: 'other'}), Object.getOwnPropertyDescriptor(owner, 'value')!.get === getter);
            """, "bound:kept bound:kept true\n"];
        yield return ["partial-bound-setter", """
            const state = {value: 0, calls: 0};
            const write = function(this: any, prefix: number, value: number) { this.value = prefix + value; this.calls++; };
            const setter: any = write.bind(state, 10), owner: any = {};
            Object.defineProperty(owner, 'value', {set: setter, configurable: true});
            owner.value = 7; Reflect.set(owner, 'value', 9, {value: 100});
            console.log(state.value, state.calls, Object.getOwnPropertyDescriptor(owner, 'value')!.set === setter, owner.value === undefined);
            """, "19 2 true true\n"];
        yield return ["bound-setter-on-function", """
            const state = {value: 0};
            const write = function(this: any, value: number) { this.value = value; };
            const setter: any = write.bind(state), owner: any = function() {};
            Object.defineProperty(owner, 'value', {set: setter, configurable: true});
            owner.value = 7;
            console.log(state.value, owner.value === undefined, Object.getOwnPropertyDescriptor(owner, 'value')!.set === setter);
            """, "7 true true\n"];
        yield return ["invalid-and-undefined", """
            const values: any[] = [null, 1, 'function', {}, []];
            for (const value of values) {
              for (const key of ['get', 'set']) {
                const descriptor: any = {}; descriptor[key] = value;
                try { Object.defineProperty({}, 'value', descriptor); console.log('accepted'); } catch (error) { console.log(error instanceof TypeError); }
              }
            }
            const owner: any = {}; Object.defineProperty(owner, 'value', {get: undefined, set: undefined});
            const descriptor: any = Object.getOwnPropertyDescriptor(owner, 'value');
            console.log(owner.value === undefined, Object.hasOwn(descriptor, 'get'), Object.hasOwn(descriptor, 'set'));
            """, "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue true true\n"];
        yield return ["callability-does-not-invoke-getter", """
            const events: string[] = [], receiver = {};
            const read = function(this: any) { events.push('called'); return this; };
            const getter: any = read.bind(receiver), owner: any = {};
            const descriptor: any = {
              configurable: true,
              get get() { events.push('get'); return getter; },
              get set() { events.push('set'); return 1; }
            };
            try { Object.defineProperty(owner, 'value', descriptor); } catch (error) { console.log(error instanceof TypeError); }
            console.log(events.join(','), Object.hasOwn(owner, 'value'));
            """, "true\nget,set false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void BoundCallablesRemainValidAccessorValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
