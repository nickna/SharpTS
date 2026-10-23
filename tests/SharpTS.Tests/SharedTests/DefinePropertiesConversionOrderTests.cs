using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DefinePropertiesConversionOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["record-first-conversion-abrupt-stops-later-source", """
            let trace = "";
            const marker = {};
            const firstDescriptor: any = {};
            Object.defineProperty(firstDescriptor, "value", {
                get() { trace += "convert"; throw marker; }
            });
            const descriptors: any = {};
            Object.defineProperty(descriptors, "first", {
                enumerable: true,
                get() { trace += "first,"; return firstDescriptor; }
            });
            Object.defineProperty(descriptors, "second", {
                enumerable: true,
                get() { trace += "second,"; return { value: 2 }; }
            });
            const target = {};
            try { Object.defineProperties(target, descriptors); }
            catch (error) {
                console.log(error === marker, trace,
                    Object.hasOwn(target, "first"), Object.hasOwn(target, "second"));
            }
            """, "true first,convert false false\n"];
        yield return ["generator-first-conversion-abrupt-stops-later-source", """
            let trace = "";
            const marker = {};
            const firstDescriptor: any = {};
            Object.defineProperty(firstDescriptor, "value", {
                get() { trace += "convert"; throw marker; }
            });
            function* values() {} const descriptors: any = values();
            Object.defineProperty(descriptors, "first", {
                enumerable: true,
                get() { trace += "first,"; return firstDescriptor; }
            });
            Object.defineProperty(descriptors, "second", {
                enumerable: true,
                get() { trace += "second,"; return { value: 2 }; }
            });
            const target = {};
            try { Object.defineProperties(target, descriptors); }
            catch (error) {
                console.log(error === marker, trace,
                    Object.hasOwn(target, "first"), Object.hasOwn(target, "second"));
            }
            """, "true first,convert false false\n"];
        yield return ["record-valid-conversion-finishes-before-any-definition", """
            const trace: string[] = [];
            const target: any = {};
            Object.defineProperty(target, "first", { value: 0, writable: true, enumerable: true, configurable: true });
            const descriptors: any = {}, firstDescriptor: any = {};
            Object.defineProperty(firstDescriptor, "value", { get() { trace.push("convert-first"); return 3; } });
            Object.defineProperty(descriptors, "first", {
                enumerable: true,
                get() { trace.push("get-first"); return firstDescriptor; }
            });
            Object.defineProperty(descriptors, "second", {
                enumerable: true,
                get() {
                    trace.push("get-second:" + target.first);
                    const descriptor: any = {};
                    Object.defineProperty(descriptor, "value", { get() { trace.push("convert-second:" + target.first); return 5; } });
                    return descriptor;
                }
            });
            Object.defineProperties(target, descriptors);
            const first = Object.getOwnPropertyDescriptor(target, "first")!, second = Object.getOwnPropertyDescriptor(target, "second")!;
            console.log(trace.join(","), target.first, target.second);
            console.log(first.writable, first.enumerable, first.configurable, second.writable, second.enumerable, second.configurable);
            """, "get-first,convert-first,get-second:0,convert-second:0 3 5\ntrue true true false false false\n"];
        yield return ["generator-symbol-conversion-keeps-source-receiver-and-order", """
            const trace: string[] = [];
            function* source() {}
            const descriptors: any = source(), target: any = {};
            const key = Symbol("last");
            let receiver = false;
            Object.defineProperty(target, "first", { value: 0, writable: true, enumerable: true, configurable: true });
            const firstDescriptor: any = {};
            Object.defineProperty(firstDescriptor, "value", { get() { trace.push("convert-first"); return 3; } });
            Object.defineProperty(descriptors, "first", {
                enumerable: true,
                get() { trace.push("get-first"); return firstDescriptor; }
            });
            Object.defineProperty(descriptors, key, {
                enumerable: true,
                get: function(this: any) {
                    receiver = this === descriptors;
                    trace.push("get-symbol:" + target.first);
                    const descriptor: any = {};
                    Object.defineProperty(descriptor, "value", { get() { trace.push("convert-symbol:" + target.first); return 5; } });
                    return descriptor;
                }
            });
            Object.defineProperties(target, descriptors);
            console.log(trace.join(","), target.first, target[key], receiver);
            console.log(Object.hasOwn(target, key), Reflect.ownKeys(target)[1] === key, Object.getOwnPropertyDescriptor(target, key)!.writable);
            """, "get-first,convert-first,get-symbol:0,convert-symbol:0 3 5 true\ntrue true false\n"];
        yield return ["proxy-descriptor-public-prototype", """
            const proxy: any = new Proxy({}, {
                defineProperty(target: any, key: PropertyKey, descriptor: PropertyDescriptor) {
                    console.log(Object.getPrototypeOf(descriptor) === Object.prototype);
                    return true;
                }
            });
            Object.defineProperties(proxy, { value: { value: 1 } });
            """, "true\n"];
        yield return ["transparent-proxy-keeps-normalized-descriptor-internal", """
            const target: any = {}, empty: any = Object.create(null);
            const proxy: any = new Proxy(target, {}), prototype: any = Object.prototype;
            let reads = 0, caught = false;
            const marker = {};
            Object.defineProperty(prototype, "value", {
                get() { reads++; throw marker; },
                configurable: true
            });
            try { Object.defineProperties(proxy, { entry: empty }); }
            catch (error) { caught = true; }
            finally { delete prototype.value; }
            console.log(caught, reads, Object.hasOwn(target, "entry"));
            """, "false 0 true\n"];

    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DescriptorsConvertBeforeLaterSourceReadsAndAllDefinitions(
        string name, string source, string expected, ExecutionMode mode)
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
