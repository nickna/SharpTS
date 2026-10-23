using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UndefinedSetterCallableDescriptorTests
{
    private static IEnumerable<(string Name, string Constructor)> Shapes()
    {
        yield return ("class-expression", "class {}");
        yield return ("ordinary-function", "function() {}");
    }

    private const string SloppyLifecycle = """
        const state = { inheritedCalls: 0 };
        Object.defineProperty(Function.prototype, "value", {
            set: function(value: any) { state.inheritedCalls++; }, configurable: true
        });
        Object.defineProperty(C, "value", { set: undefined, enumerable: true, configurable: true });
        let descriptor: any = Object.getOwnPropertyDescriptor(C, "value");
        console.log(descriptor.get === undefined, descriptor.set === undefined,
            Object.hasOwn(descriptor, "get"), Object.hasOwn(descriptor, "set"),
            Object.hasOwn(descriptor, "value"), Object.hasOwn(descriptor, "writable"),
            descriptor.enumerable, descriptor.configurable);
        try { C.value = 7; console.log("ignored", C.value, Object.hasOwn(C, "value")); }
        catch (error) { console.log("caught"); }
        console.log(state.inheritedCalls);
        descriptor = Object.getOwnPropertyDescriptor(C, "value");
        console.log(descriptor.get === undefined, descriptor.set === undefined,
            Object.hasOwn(descriptor, "value"), Object.hasOwn(descriptor, "writable"));
        console.log(delete C.value, Object.hasOwn(C, "value"));
        delete (Function.prototype as any).value;
        const result: any = (C.value = 8);
        descriptor = Object.getOwnPropertyDescriptor(C, "value");
        console.log(result, C.value, descriptor.value, descriptor.writable,
            descriptor.enumerable, descriptor.configurable);
        """;

    private const string SloppyExpected = "true true true true false false true true\nignored undefined true\n0\ntrue true false false\ntrue false\n8 8 8 true true true\n";

    private const string StrictControl = """
        const state = { gets: 0, sets: 0, last: 0, receiver: false };
        const getter: any = function() { state.gets++; return 21; };
        const setter: any = function(this: any, value: number) {
            state.sets++; state.last = value; state.receiver = this === C;
        };
        function strictWrite(target: any, value: number) { "use strict"; target.value = value; }
        Object.defineProperty(C, "value", { get: getter, set: undefined, configurable: true });
        C.value = 1;
        console.log(state.gets, state.sets, Object.hasOwn(C, "value"));
        try { strictWrite(C, 2); console.log(false); }
        catch (error) { console.log(error instanceof TypeError); }
        console.log(state.gets, state.sets);
        let descriptor: any = Object.getOwnPropertyDescriptor(C, "value");
        console.log(descriptor.get === getter, descriptor.set === undefined,
            Object.hasOwn(descriptor, "value"), Object.hasOwn(descriptor, "writable"));
        Object.defineProperty(C, "value", { set: setter });
        Object.freeze(C);
        C.value = 3; strictWrite(C, 4);
        console.log(state.gets, state.sets, state.last, state.receiver);
        descriptor = Object.getOwnPropertyDescriptor(C, "value");
        console.log(descriptor.get === getter, descriptor.set === setter,
            descriptor.configurable, descriptor.enumerable);
        console.log(C.value, state.gets);
        """;

    private const string StrictExpected = "0 0 true\ntrue\n0 0\ntrue true false false\n0 2 4 true\ntrue true false false\n21 1\n";

    private const string ArrayPresenceControl = """
        function strictNamed(target: any) { "use strict"; target.value = 1; }
        function strictString(target: any) { "use strict"; target["913"] = 2; }
        function strictNumeric(target: any) { "use strict"; target[913] = 3; }
        const own: any = [];
        Object.defineProperty(own, "value", { set: undefined, configurable: true });
        try { own.value = 7; console.log("ignored"); }
        catch (error) { console.log("caught"); }
        try { strictNamed(own); console.log(false); }
        catch (error) { console.log(error instanceof TypeError); }
        const descriptor: any = Object.getOwnPropertyDescriptor(own, "value");
        console.log(own.value === undefined, Object.hasOwn(own, "value"), descriptor.set === undefined);
        const state = { calls: 0, gets: 0 };
        const stringArray: any = [], numericArray: any = [], propertyArray: any = [];
        try {
            Object.defineProperty(Object.prototype, "913", {
                set: function(value: any) { state.calls++; }, configurable: true
            });
            Object.defineProperty(Array.prototype, "913", { set: undefined, configurable: true });
            try { strictString(stringArray); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            try { strictNumeric(numericArray); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            try { Object.assign(propertyArray, { "913": 4 }); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(state.calls, Object.hasOwn(stringArray, "913"), Object.hasOwn(numericArray, "913"),
                Object.hasOwn(propertyArray, "913"), stringArray.length, numericArray.length, propertyArray.length);
            Object.defineProperty(Array.prototype, "913", {
                get: function() { state.gets++; return 10; }, set: undefined, configurable: true
            });
            try { strictString(stringArray); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            try { strictNumeric(numericArray); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            try { Object.assign(propertyArray, { "913": 4 }); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(state.calls, state.gets, Object.hasOwn(stringArray, "913"),
                Object.hasOwn(numericArray, "913"), Object.hasOwn(propertyArray, "913"));
            Object.defineProperty(Array.prototype, "913", { value: 10, writable: true, configurable: true });
            strictString(stringArray); strictNumeric(numericArray); Object.assign(propertyArray, { "913": 4 });
            console.log(state.calls, state.gets, stringArray[913], numericArray[913], propertyArray[913], Array.prototype[913]);
        } finally {
            delete (Array.prototype as any)["913"];
            delete (Object.prototype as any)["913"];
        }
        """;

    private const string ArrayPresenceExpected = "ignored\ntrue\ntrue true true\ntrue\ntrue\ntrue\n0 false false false 0 0 0\ntrue\ntrue\ntrue\n0 0 false false false\n0 0 2 3 4 10\n";

    public static IEnumerable<object[]> Cases()
    {
        foreach (var (name, constructor) in Shapes())
            yield return [name + "-sloppy-lifecycle", "const C: any = " + constructor + ";\n" + SloppyLifecycle, SloppyExpected];
    }

    public static IEnumerable<object[]> CompiledCases()
    {
        foreach (var row in Cases()) yield return row;
        foreach (var (name, constructor) in Shapes())
            yield return [name + "-strict-and-frozen-setter", "const C: any = " + constructor + ";\n" + StrictControl, StrictExpected];
        yield return ["array-own-and-nearest-inherited-presence", ArrayPresenceControl, ArrayPresenceExpected];
    }

    public static IEnumerable<object[]> ModeCases() => CompiledCases()
        .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Interpreted })
        .Concat(CompiledCases().Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Compiled }));

    [Theory, MemberData(nameof(ModeCases))]
    public void UndefinedSetterRetainsAccessorPresenceAndWritePolicy(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
