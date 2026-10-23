using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class DefinePropertiesSymbolMapTests
{
    public const string IssueSource = """
        const symbol = Symbol("value"), target: any = {};
        Object.defineProperties(target, {
            ordinary: { value: 4, enumerable: true },
            [symbol]: { value: 8, enumerable: true }
        });
        console.log(target.ordinary, target[symbol], Object.getOwnPropertySymbols(target).length);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-ordinary-string-and-symbol-map", IssueSource, "4 8 1\n"];
        yield return ["identity-order-repeated-symbol-and-omitted-map-keys", """
            const first = Symbol('same'), second = Symbol('same'), hidden = Symbol('hidden'), inherited = Symbol('inherited');
            const ancestor: any = { inherited: { value: 90 }, [inherited]: { value: 91 } };
            const descriptors: any = Object.create(ancestor), target: any = {}, marker = {};
            descriptors[first] = { value: 7, enumerable: true };
            descriptors.beta = { value: 2, enumerable: true };
            descriptors['10'] = { value: 10, enumerable: true };
            descriptors['2'] = { value: 2, enumerable: true };
            descriptors[second] = { value: 8, enumerable: false };
            descriptors['Symbol(same)'] = { value: 11, enumerable: true };
            descriptors[first] = { value: 9, enumerable: true };
            Object.defineProperty(descriptors, 'hidden', { enumerable: false, get() { throw marker; } });
            Object.defineProperty(descriptors, hidden, { enumerable: false, get() { throw marker; } });
            Object.defineProperties(target, descriptors);
            const symbols = Object.getOwnPropertySymbols(target), keys = Reflect.ownKeys(target);
            console.log(Object.keys(target).join(','), target[first], target[second], target['Symbol(same)']);
            console.log(symbols.length, symbols[0] === first, symbols[1] === second,
                keys[4] === first, keys[5] === second, Object.getOwnPropertyDescriptor(target, second)!.enumerable);
            console.log(Object.hasOwn(target, 'hidden'), Object.hasOwn(target, hidden),
                Object.hasOwn(target, 'inherited'), Object.hasOwn(target, inherited));
            """, "2,10,beta,Symbol(same) 9 8 11\n2 true true true true false\nfalse false false false\n"];
        yield return ["symbol-getter-and-conversion-finish-before-application", """
            const symbol = Symbol('last'), events: string[] = [], target: any = {}, descriptors: any = {};
            let receiver = false;
            Object.defineProperty(target, 'first', { value: 0, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(descriptors, 'first', { enumerable: true, get() {
                events.push('get:first');
                return { get value() { events.push('convert:first'); return 3; } };
            } });
            Object.defineProperty(descriptors, symbol, { enumerable: true, get: function(this: any) {
                receiver = this === descriptors;
                events.push('get:symbol:' + target.first);
                return { get value() { events.push('convert:symbol:' + target.first); return 5; } };
            } });
            Object.defineProperties(target, descriptors);
            console.log(events.join(','), target.first, target[symbol], receiver);
            const first = Object.getOwnPropertyDescriptor(target, 'first')!;
            const last = Object.getOwnPropertyDescriptor(target, symbol)!;
            console.log(first.writable, first.enumerable, first.configurable,
                last.writable, last.enumerable, last.configurable);
            """, "get:first,convert:first,get:symbol:0,convert:symbol:0 3 5 true\ntrue true true false false false\n"];
        yield return ["symbol-abrupt-and-invalid-conversion-leave-target-unchanged", """
            const symbol = Symbol('abrupt'), later = Symbol('later'), marker = {}, events: string[] = [];
            const target: any = {}, descriptors: any = { first: { value: 1, enumerable: true } };
            Object.defineProperty(descriptors, symbol, { enumerable: true, get() {
                events.push('get:symbol');
                return { get value() { events.push('convert:symbol'); throw marker; } };
            } });
            Object.defineProperty(descriptors, later, { enumerable: true, get() {
                events.push('later'); return { value: 2 };
            } });
            try { Object.defineProperties(target, descriptors); }
            catch (error) { console.log(error === marker, events.join(','), Object.hasOwn(target, 'first'),
                Object.hasOwn(target, symbol), Object.hasOwn(target, later)); }
            const invalid: any = { first: { value: 1 }, [symbol]: 1 }, second: any = {};
            try { Object.defineProperties(second, invalid); }
            catch (error) { console.log(error instanceof TypeError, Object.hasOwn(second, 'first'), Reflect.ownKeys(second).length); }
            """, "true get:symbol,convert:symbol false false false\ntrue false 0\n"];
        yield return ["empty-normalized-descriptor-keeps-omitted-replay-fields", """
            const target: any = {}, empty: any = Object.create(null), prototype: any = Object.prototype;
            const state = { reads: 0 }, marker = {};
            Object.defineProperty(target, 'entry', { value: 7, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(prototype, 'value', { configurable: true, get() { state.reads++; throw marker; } });
            try { Object.defineProperties(target, { entry: empty }); }
            finally { delete prototype.value; }
            const entry = Object.getOwnPropertyDescriptor(target, 'entry')!;
            console.log(target.entry, state.reads, entry.value, entry.writable, entry.enumerable, entry.configurable);
            """, "7 0 7 true true true\n"];
        yield return ["data-accessor-empty-data-sequence", """
            const symbol = Symbol('last'), target: any = {}, events: string[] = [];
            Object.defineProperty(target, 'keep', { value: 7, writable: true, enumerable: true, configurable: true });
            const getter = function(this: any) {
                events.push(this === target ? 'receiver' : 'wrong');
                return this.first + this.keep;
            };
            Object.defineProperties(target, {
                first: { value: 3, writable: true, enumerable: true, configurable: true },
                access: { get: getter, enumerable: true, configurable: true },
                keep: Object.create(null),
                [symbol]: { value: 11, writable: true, enumerable: true, configurable: true }
            });
            console.log(events.length, target.first, target.keep, target[symbol]);
            console.log(target.access, events.join(','));
            const access = Object.getOwnPropertyDescriptor(target, 'access')!;
            const keep = Object.getOwnPropertyDescriptor(target, 'keep')!;
            const last = Object.getOwnPropertyDescriptor(target, symbol)!;
            console.log(access.get === getter, access.set === undefined, 'value' in access,
                keep.value, keep.writable, keep.enumerable, keep.configurable);
            console.log(last.value, last.writable, last.enumerable, last.configurable);
            """, "0 3 7 11\n10 receiver\ntrue true false 7 true true true\n11 true true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DefinePropertiesKeepsOwnSymbolMapKeysAndConversionOrdering(
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
