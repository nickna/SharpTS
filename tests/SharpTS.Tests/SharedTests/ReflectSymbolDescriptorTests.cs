using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectSymbolDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            const key = Symbol("key"), target: any = {}, source: any = {};
            console.log(Reflect.defineProperty(target, key, { value: 3, configurable: true }));
            console.log(target[key], target["Symbol(key)"]);
            Object.defineProperty(source, key, { value: 4, configurable: true });
            const descriptor: any = Reflect.getOwnPropertyDescriptor(source, key);
            console.log(descriptor === undefined ? "missing" : descriptor.value);
            """, "true\n3 undefined\n4\n"];
        yield return ["identity-and-flags", """
            const key = Symbol("same"), other = Symbol("same"), target: any = {};
            const define: any = Reflect.defineProperty, describe: any = Reflect.getOwnPropertyDescriptor;
            target["Symbol(same)"] = 11;
            console.log(define(target, key, { value: 3, writable: true, configurable: true }),
                define(target, other, { value: 9 }));
            console.log(define(target, key, { enumerable: true }));
            const descriptor = describe(target, key);
            console.log(descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable,
                target[other], target["Symbol(same)"], describe(target, Symbol("same")) === undefined);
            console.log(define(target, other, { value: 10 }), target[other]);
            Object.preventExtensions(target);
            console.log(define(target, Symbol("new"), { value: 1 }), define(target, key, { value: 4 }), target[key]);
            """, "true true\ntrue\n3 true true true 9 11 true\nfalse 9\nfalse true 4\n"];
        yield return ["coercion-order", """
            const key = Symbol("coerced"), target: any = {}, marker = {};
            const state = { order: "", calls: 0, valid: true };
            const propertyKey: any = { [Symbol.toPrimitive](hint: string) {
                state.order += "k"; state.calls++; state.valid = state.valid && hint === "string";
                return key;
            } };
            const description: any = { get value() { state.order += "v"; return 5; }, configurable: true };
            console.log(Reflect.defineProperty(target, propertyKey, description), state.order, state.calls);
            const descriptor: any = Reflect.getOwnPropertyDescriptor(target, propertyKey);
            console.log(descriptor.value, state.order, state.calls, state.valid);
            """, "true kv 1\n5 kvk 2 true\n"];
        yield return ["abrupt-descriptors", """
            const key = Symbol("abrupt"), target: any = {}, marker = {};
            Object.defineProperty(target, key, { value: 5, configurable: true });
            try { Reflect.defineProperty(target, key, { get value() { throw marker; } }); }
            catch (error) { console.log(error === marker, target[key]); }
            let reads = 0;
            const getter = () => { reads++; return 6; };
            console.log(Reflect.defineProperty(target, key, { get: getter }));
            const accessor: any = Reflect.getOwnPropertyDescriptor(target, key);
            console.log(accessor.get === getter, reads, "value" in accessor);
            for (const description of [
                { get value() { throw marker; } }, { get get() { throw marker; } }
            ] as any[]) {
                try { Reflect.defineProperty({}, "x", description); }
                catch (error) { console.log(error === marker); }
            }
            for (const description of [{ value: 1, get() { return 2; } }, { get: 1 }, { set: 1 }] as any[]) {
                try { Reflect.defineProperty({}, "x", description); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            """, "true 5\ntrue\ntrue 0 false\ntrue\ntrue\ntrue\ntrue\ntrue\n"];
        yield return ["proxy-forwarding-and-traps", """
            const key = Symbol("proxy"), target: any = {}, state = { valid: true };
            const forwarding: any = new Proxy(new Proxy(target, {}), {});
            console.log(Reflect.defineProperty(forwarding, key, { value: 3, writable: true, configurable: true }),
                Reflect.getOwnPropertyDescriptor(forwarding, key).value);
            const trapping: any = new Proxy(target, {
                defineProperty(object: any, actualKey: any, descriptor: any) {
                    state.valid = state.valid && actualKey === key;
                    return Reflect.defineProperty(object, actualKey, descriptor);
                },
                getOwnPropertyDescriptor(object: any, actualKey: any) {
                    state.valid = state.valid && actualKey === key;
                    return Reflect.getOwnPropertyDescriptor(object, actualKey);
                }
            });
            console.log(Reflect.defineProperty(trapping, key, { value: 4 }),
                Reflect.getOwnPropertyDescriptor(trapping, key).value, state.valid);
            const refuses: any = new Proxy(target, { defineProperty() { return false; } });
            console.log(Reflect.defineProperty(refuses, key, { value: 8 }), target[key]);
            """, "true 3\ntrue 4 true\nfalse 4\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        // Compiled descriptor key coercion remains #2591.
        ((string)row[0] is "coercion-order"
            ? new[] { ExecutionMode.Interpreted }
            : new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }).Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DescriptorApisRetainSymbolKeys(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        string output;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, compiledOutput) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            output = compiledOutput;
        }
        else
        {
            output = TestHarness.Run(source, mode);
        }
        Assert.Equal(expected, output);
    }
}
