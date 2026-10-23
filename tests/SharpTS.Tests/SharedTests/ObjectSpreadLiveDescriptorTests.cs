using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ObjectSpreadLiveDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-ordinary-live-descriptor-spread", """
            const source: any = { first: 1, removed: 2, later: 3 };
            Object.defineProperty(source, "first", {
                enumerable: true, configurable: true,
                get() {
                    delete source.removed;
                    Object.defineProperty(source, "later", { enumerable: false });
                    return 7;
                }
            });
            const copy: any = { ...source };
            console.log(Object.keys(copy).join(","), copy.first,
                Object.hasOwn(copy, "removed"), Object.hasOwn(copy, "later"));
            console.log(Object.hasOwn(source, "removed"),
                Object.getOwnPropertyDescriptor(source, "later")!.enumerable);
            """ + "\n", "first 7 false false\nfalse false\n"];
        yield return ["full-class-prototype-live-spread-source", ClassPrototypeSpreadRestTests.LiveSpreadSource, "first,later,hidden 10 20 4 true true 1\n"];
        yield return ["ordinary-hidden-reveal-and-new-key-snapshot", """
            const state = { calls: 0 };
            const source: any = { first: 1, hidden: 2 };
            const hiddenProperty = Object.defineProperty(source, "hidden", {
                value: 2, writable: true, enumerable: false, configurable: true
            });
            const firstProperty = Object.defineProperty(source, "first", {
                enumerable: true, configurable: true,
                get() {
                    state.calls++;
                    const revealed = Object.defineProperty(source, "hidden", { enumerable: true });
                    source.late = 9;
                    return 7;
                }
            });
            const copy: any = { ...source };
            console.log(Object.keys(copy).join(","), copy.first, copy.hidden,
                Object.hasOwn(copy, "late"), state.calls);
            """ + "\n", "first,hidden 7 2 false 1\n"];
        yield return ["historical-getter-receivers-and-fresh-descriptors", """
            const symbol = Symbol('access'), state = { gets: 0, wrong: 0 };
            class Owner {}
            const source: any = Owner.prototype;
            Object.defineProperty(source, 'marker', { value: 7, enumerable: false });
            Object.defineProperty(source, 'access', { enumerable: true, configurable: true,
                get: function(this: any) { state.gets++; if (this !== source) state.wrong++; return this.marker; } });
            Object.defineProperty(source, 'setterOnly', { enumerable: true, set(value: any) {} });
            Object.defineProperty(source, symbol, { enumerable: true,
                get: function(this: any) { state.gets++; if (this !== source) state.wrong++; return 11; } });
            const copy: any = { ...source };
            const { ...rest } = source;
            console.log(copy.access, copy[symbol], rest.access, rest[symbol], state.gets, state.wrong);
            const data = Object.getOwnPropertyDescriptor(copy, 'access')!;
            const symbolData = Object.getOwnPropertyDescriptor(rest, symbol)!;
            console.log(data.value, data.get === undefined, data.set === undefined,
                data.writable, data.enumerable, data.configurable);
            console.log(Object.hasOwn(copy, 'setterOnly'), copy.setterOnly === undefined,
                symbolData.value, symbolData.writable, symbolData.enumerable, symbolData.configurable);
            """, "7 11 7 11 4 0\n7 true true true true true\ntrue true 11 true true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SpreadRechecksLiveOwnDescriptors(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }
}
