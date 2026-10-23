using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassPrototypeSpreadRestTests
{
    public const string IssueSource = """
        class Owner {}
        const source: any = Owner.prototype;
        source.value = 1;
        try {
            const copy: any = { ...source };
            console.log("spread", copy.value);
        } catch (error: any) {
            console.log("spread error", error.name, error.message);
        }
        const { ...rest } = source;
        console.log("rest", rest.value);
        console.log("own", Object.prototype.hasOwnProperty.call(source, "value"));
        """;

    public const string LiveSpreadSource = """
        const state = { calls: 0 };
        class Owner {}
        const source: any = Owner.prototype;
        Object.defineProperty(source, 'first', { enumerable: true, configurable: true, get() {
            state.calls++;
            source.later = 20;
            delete source.removed;
            Object.defineProperty(source, 'hidden', { enumerable: true });
            source.late = 30;
            return 10;
        } });
        source.later = 2; source.removed = 3;
        Object.defineProperty(source, 'hidden', { value: 4, writable: true, enumerable: false, configurable: true });
        const copy: any = { ...source };
        console.log(Object.keys(copy).join(','), copy.first, copy.later, copy.hidden,
            copy.removed === undefined, copy.late === undefined, state.calls);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-class-prototype-spread-and-rest", IssueSource, "spread 1\nrest 1\nown true\n"];
        yield return ["own-string-symbol-order-and-rest-exclusions", """
            const own = Symbol('own'), inherited = Symbol('inherited'), hidden = Symbol('hidden');
            class Parent {}
            const basePrototype: any = Parent.prototype;
            basePrototype.inherited = 8; basePrototype[inherited] = 9;
            class Owner extends Parent { method() {} }
            const source: any = Owner.prototype;
            source.beta = 2; source['10'] = 10; source['2'] = 2; source.alpha = 1; source[own] = 7;
            Object.defineProperty(source, 'hidden', { value: 4, enumerable: false });
            Object.defineProperty(source, hidden, { value: 5, enumerable: false });
            const copy: any = { ...source };
            const { beta, [own]: symbolValue, ...rest } = source;
            console.log(Object.keys(copy).join(','), copy[own], Object.getOwnPropertySymbols(copy)[0] === own);
            console.log(beta, symbolValue, Object.keys(rest).join(','), Object.getOwnPropertySymbols(rest).length);
            console.log(Object.hasOwn(copy, 'constructor'), Object.hasOwn(copy, 'method'),
                Object.hasOwn(copy, 'inherited'), Object.hasOwn(copy, inherited),
                Object.hasOwn(copy, 'hidden'), Object.hasOwn(copy, hidden));
            """, "2,10,beta,alpha 7 true\n2 7 2,10,alpha 0\nfalse false false false false false\n"];
        yield return ["getter-receivers-and-fresh-data-descriptors", """
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
        yield return ["rest-key-snapshot-with-live-descriptors-and-values", """
            const state = { calls: 0 };
            function prepare(source: any): any {
                Object.defineProperty(source, 'first', { enumerable: true, configurable: true, get() {
                    state.calls++;
                    source.later = 20;
                    delete source.removed;
                    Object.defineProperty(source, 'hidden', { enumerable: true });
                    source.late = 30;
                    return 10;
                } });
                source.later = 2; source.removed = 3;
                Object.defineProperty(source, 'hidden', { value: 4, writable: true, enumerable: false, configurable: true });
                return source;
            }
            class RestSource {}
            const { ...rest } = prepare(RestSource.prototype);
            console.log(Object.keys(rest).join(','), rest.first, rest.later, rest.hidden,
                rest.removed === undefined, rest.late === undefined, state.calls);
            """, "first,later,hidden 10 20 4 true true 1\n"];
        yield return ["excluded-getters-and-abrupt-completion", """
            const symbol = Symbol('skip'), state = { kept: 0, later: 0 };
            class Owner {}
            const source: any = Owner.prototype;
            Object.defineProperty(source, 'skip', { enumerable: true, get() { return 1; } });
            Object.defineProperty(source, 'keep', { enumerable: true, get() { state.kept++; return 2; } });
            Object.defineProperty(source, symbol, { enumerable: true, get() { return 3; } });
            const { skip, [symbol]: taken, ...rest } = source;
            console.log(skip, taken, rest.keep, state.kept,
                Object.hasOwn(rest, 'skip'), Object.hasOwn(rest, symbol));
            class Broken {}
            const broken: any = Broken.prototype, marker = {};
            Object.defineProperty(broken, 'first', { enumerable: true, get() { throw marker; } });
            Object.defineProperty(broken, 'later', { enumerable: true, get() { state.later++; return 9; } });
            try { const copy = { ...broken }; } catch (error) { console.log(error === marker, state.later); }
            try { const { ...copy } = broken; } catch (error) { console.log(error === marker, state.later); }
            """, "1 3 2 1 false false\ntrue 0\ntrue 0\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Fact]
    public void InterpretedSpreadRechecksLiveDescriptorsAfterItsKeySnapshot()
    {
        var result = SourceExecutionService.Interpret(LiveSpreadSource);
        Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
        Assert.Empty(result.Errors);
        Assert.Equal("first,later,hidden 10 20 4 true true 1\n", result.Output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void CompiledSpreadRechecksLiveDescriptorsAfterItsKeySnapshot()
    {
        var (errors, output) = TestHarness.CompileVerifyAndRun(LiveSpreadSource);
        Assert.Empty(errors);
        Assert.Equal("first,later,hidden 10 20 4 true true 1\n", output);
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void SpreadAndRestCopyEnumerableOwnClassPrototypeProperties(
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
