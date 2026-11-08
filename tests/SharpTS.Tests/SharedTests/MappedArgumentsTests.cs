using System.Reflection;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class MappedArgumentsTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["""
            function visit(first, second) {
                const args = arguments;
                const trace = [];
                Object.defineProperty(args, "0", {
                    get() { trace.push("get0"); return 1; }
                });
                Object.defineProperty(args, "1", {
                    get() { trace.push("get1"); return 2; }
                });
                const result = Array.prototype.some.call(args, (value, index) => {
                    trace.push("callback" + index);
                    return value === 1;
                });
                console.log(result, trace.join(","));
            }
            visit(1, 2);
            function mapLive() {
                "use strict";
                const args = arguments;
                console.log(Array.prototype.map.call(args, (value, index) => {
                    if (index === 0) args[1] = 9;
                    return value;
                }).join(","));
            }
            mapLive(1, 2);
            """, "true get0,callback0\n1,9\n"];
        yield return ["""
            function run(value) {
                console.log(arguments[0]++, value, ++arguments[0], value);
                value = 8;
                console.log(arguments["0"], Object.getOwnPropertyDescriptor(arguments, "0").value,
                    [...arguments].join(","));
            }
            run(3, 15);
            const factory = function (item) {
                return { args: arguments, set: v => item = v, get: () => item };
            };
            const live = factory(1);
            live.set(5);
            console.log(live.args[0]);
            live.args[0] = 8;
            console.log(live.get());
            console.log(Reflect.deleteProperty(live.args, "0"), Object.hasOwn(live.args, "0"));
            live.args[0] = 13;
            live.set(20);
            console.log(live.args[0], live.get());
            """, "3 4 5 5\n8 8 8,15\n5\n8\ntrue false\n13 20\n"];
        yield return ["""
            function descriptors(a, b, c, d) {
                const args = arguments;
                const receiver = {};
                console.log(Reflect.set(args, "0", 10), a);
                console.log(Reflect.set(args, "0", 11, receiver), a, receiver[0]);
                args["00"] = 17;
                console.log(args["00"], args[0], a);
                Object.defineProperty(args, "0", { enumerable: false });
                a = 12;
                console.log(args[0], Object.keys(args).join(","));
                Object.defineProperty(args, "0", { writable: false });
                a = 13;
                args[0] = 14;
                console.log(args[0], a);
                Object.defineProperty(args, "1", { value: 20, writable: false });
                console.log(args[1], b);
                b = 21;
                console.log(args[1], b);
                Object.defineProperty(args, "2", { get() { return 30; } });
                c = 31;
                console.log(args[2], c);
                Object.seal(args);
                console.log(delete args[3], Object.getOwnPropertyDescriptor(args, "3").configurable);
                d = 40;
                console.log(args[3], d);
                Object.freeze(args);
                d = 41;
                const frozen = Object.getOwnPropertyDescriptor(args, "3");
                console.log(frozen.value, frozen.writable, args[3], d);
            }
            descriptors(1, 2, 3, 4);
            """, "true 10\ntrue 10 11\n17 10 10\n12 1,2,3,00\n12 13\n20 20\n20 21\n30 31\nfalse false\n40 40\n40 false 40 41\n"];
        yield return ["""
            function duplicate(a, a) {
                a = 2;
                console.log(arguments[0], arguments[1], arguments.length, a);
            }
            duplicate(3, 4);
            duplicate(3);
            function missing(a) {
                arguments[0] = 2;
                console.log(a, arguments[0], arguments.length);
            }
            missing();
            function strict(a) { "use strict"; arguments[0] = 2; console.log(a, arguments[0]); }
            strict(1);
            function enclosing() {
                "use strict";
                const strictExpression = function (a) { arguments[0] = 2; console.log(a, arguments[0]); };
                strictExpression(1);
            }
            enclosing();
            function defaulted(a = 1) { arguments[0] = 2; console.log(a, arguments[0]); }
            defaulted();
            function withRest(a, ...rest) { arguments[0] = 2; console.log(a, arguments[0]); }
            withRest(1, 3);
            function destructured([a]) { arguments[0] = [2]; console.log(a, arguments[0][0]); }
            destructured([1]);
            """, "3 2 2 2\n3 undefined 1 2\nundefined 2 0\n1 2\n1 2\n1 2\n1 2\n1 2\n"];
        yield return ["""
            async function declared(value) {
                const alias = arguments;
                const read = () => value;
                const change = async () => {
                    await Promise.resolve();
                    value++;
                    return () => value;
                };
                alias[0]++;
                console.log(value, read());
                await Promise.resolve();
                value += 2;
                const nestedRead = await change();
                console.log(alias[0], read(), nestedRead());
            }
            const expressed = async function(value) {
                await Promise.resolve();
                arguments[0]++;
                const read = () => value;
                console.log(value, read());
                value = 9;
                console.log(arguments[0], read());
            };
            function* generated(value) {
                const read = () => value;
                yield arguments;
                console.log(value, read());
                value += 2;
                yield arguments[0];
            }
            async function* asyncGenerated(value) {
                yield arguments;
                await Promise.resolve();
                value += 2;
                yield arguments[0];
            }
            const generatedExpression = function* (value) {
                yield arguments;
                value++;
                yield arguments[0];
            };
            async function strictParent() {
                "use strict";
                const nested = function (value) {
                    arguments[0] = 2;
                    console.log(value, arguments[0]);
                };
                nested(1);
            }
            async function main() {
                await declared(1);
                await expressed(3);
                const iterator = generated(4);
                iterator.next().value[0] = 8;
                console.log(iterator.next().value);
                const asyncIterator = asyncGenerated(4);
                (await asyncIterator.next()).value[0] = 8;
                console.log((await asyncIterator.next()).value);
                const expressionIterator = generatedExpression(1);
                expressionIterator.next().value[0] = 4;
                console.log(expressionIterator.next().value);
                await strictParent();
            }
            main();
            """, "2 2\n5 5 5\n4 4\n9 9\n8 8\n10\n10\n5\n1 2\n"];
        yield return ["""
            namespace Sloppy {
                export function run(value: number) {
                    const read = () => value;
                    arguments[0] = 8;
                    console.log(value, read(), arguments[0]);
                }
            }
            class Strict {
                run(value: number) {
                    arguments[0] = 8;
                    console.log(value, arguments[0]);
                }
            }
            Sloppy.run(1);
            new Strict().run(1);
            """, "8 8 8\n1 8\n"];
        yield return ["""
            function check(value) {
                var value = 3;
                const read = () => value;
                console.log(value, read(), arguments[0]);
                arguments[0] = 7;
                var value;
                console.log(value, read(), arguments[0]);
            }
            async function suspended(value) {
                var value = 3;
                await Promise.resolve();
                const read = () => value;
                console.log(value, read(), arguments[0]);
                arguments[0] = 7;
                var value;
                console.log(value, read(), arguments[0]);
            }
            check(1);
            suspended(1);
            """, "3 3 3\n7 7 7\n3 3 3\n7 7 7\n"];
        yield return ["""
            function installPrototype(args) {
                delete args[0];
                Object.setPrototypeOf(args, {
                    get 0() { return 10; },
                    set 0(value) { console.log("setter", value); }
                });
                Object.preventExtensions(args);
            }
            function sloppy(value) {
                installPrototype(arguments);
                console.log(arguments["0"]++, Object.hasOwn(arguments, "0"), value);
            }
            function strict(value) {
                "use strict";
                installPrototype(arguments);
                console.log(++arguments["0"], Object.hasOwn(arguments, "0"), value);
            }
            sloppy(1);
            strict(2);
            """, "setter 11\n10 false 1\nsetter 11\n11 false 2\n"];
        yield return ["""
            function sort(a, b) {
                a = 2;
                b = 1;
                Array.prototype.sort.call(arguments);
                console.log(a, b, arguments[0], arguments[1]);
            }
            sort(7, 8);
            """, "1 2 1 2\n"];
    }

    [Fact]
    public void CompiledArgumentsIterationKeepsTheIndependentLength()
    {
        const string source = """
            function visit(value) {
                const args = arguments;
                args.length = 1048577;
                console.log(Array.prototype.some.call(args, v => v === value));
                const some = Array.prototype.some;
                console.log(some.apply(args, [v => v === value]));
                console.log(Array.prototype.findLastIndex.call(args, () => true));
            }
            visit(1);
            """;
        Assert.Equal("true\ntrue\n1048576\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void ShortCircuitArgumentsSnapshotDoesNotAllocateIndexedStorage()
    {
        var assembly = Assembly.Load(TestHarness.CompileUncheckedAndVerify(
            "function args() { return arguments; } console.log(args(1).length);"));
        var argumentsType = assembly.GetType("$Arguments", throwOnError: true)!;
        var length = argumentsType.GetField("_length")!;
        var receiver = Activator.CreateInstance(argumentsType);
        length.SetValue(receiver, 65_536);

        var runtime = assembly.GetType("$Runtime", throwOnError: true)!;
        var snapshot = runtime.GetMethod("ArrayLikeMaterializeForIteration")!
            .Invoke(null, [receiver, true, false]);
        Assert.Empty(Assert.IsAssignableFrom<List<object>>(snapshot));
        Assert.Equal(65_536, length.GetValue(snapshot));
    }

    [Fact]
    public void CompiledArgumentsWithValidatesTheIndexBeforeCopying()
    {
        const string source = """
            function visit(first, second) {
                const args = arguments;
                const trace = [];
                Object.defineProperty(args, "length", {
                    get() { trace.push("length"); return 3; }
                });
                Object.defineProperty(args, "0", {
                    get() { trace.push("get0"); return 7; }
                });
                Object.defineProperty(args, "1", {
                    get() { trace.push("get1"); return 8; }
                });
                const invalid = { valueOf() { trace.push("index"); return 3; } };
                const valid = { valueOf() { trace.push("index"); return 0; } };
                try { Array.prototype.with.call(args, invalid, 99); }
                catch (error) { trace.push(error instanceof RangeError ? "range" : "error"); }
                console.log(trace.join(","));
                trace.length = 0;
                console.log(Array.prototype.with.call(args, valid, 99).join(","), trace.join(","));
                trace.length = 0;
                const copyWith = Array.prototype.with;
                console.log(copyWith.apply(args, [2, 99]).join(","), trace.join(","));
            }
            visit(1, 2);

            function invalidLength(first) {
                Object.defineProperty(arguments, "length", { value: 4294967296 });
                const index = { valueOf() { throw "index threw"; } };
                try { Array.prototype.with.call(arguments, index, 99); }
                catch (error) { console.log(error); }
            }
            invalidLength(1);
            """;
        Assert.Equal(
            "length,index,range\n99,8, length,index,get1\n7,8,99 length,get0,get1\nindex threw\n",
            TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void CompiledArgumentsCopyingMethodsReadLengthOnce()
    {
        const string source = """
            function visit(first, second) {
                const args = arguments;
                let reads = 0;
                Object.defineProperty(args, "length", { get() { reads++; return 2; } });
                console.log(Array.prototype.toReversed.call(args).join(","), reads);
                reads = 0;
                const reverse = Array.prototype.toReversed;
                console.log(reverse.apply(args, []).join(","), reads);
                reads = 0;
                console.log(Array.prototype.map.call(args, v => v + 1).join(","), reads);
                reads = 0;
                console.log(Array.prototype.filter.call(args, v => v > 1).join(","), reads);
                reads = 0;
                console.log(Array.prototype.flat.call(args).join(","), reads);
            }
            visit(2, 1);
            function mapOversized(first) {
                let reads = 0;
                Object.defineProperty(arguments, "length", { get() { reads++; return 4294967296; } });
                try { Array.prototype.map.call(arguments, v => v); }
                catch (error) { console.log(error instanceof RangeError, reads); }
            }
            mapOversized(1);
            function nestedFailure(first) {
                const args = arguments;
                Object.defineProperty(args, "length", { get() { throw "bad length"; } });
                const values = [4, 5];
                const result = Array.prototype.map.call(values, (value, index) => {
                    if (index === 0) {
                        try { Array.prototype.filter.call(args, () => true); }
                        catch (error) { console.log(error); }
                    }
                    return value;
                });
                console.log(result.join(","));
            }
            nestedFailure(1);
            """;
        Assert.Equal("1,2 1\n1,2 1\n3,2 1\n2 1\n2,1 1\ntrue 1\nbad length\n4,5\n",
            TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SimpleSloppyParametersShareOnlyLiveArgumentIndices(
        string source, string expected, ExecutionMode mode)
        => Assert.Equal(expected, TestHarness.RunUnchecked(source, mode));

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void EsModuleArgumentsRemainUnmapped(ExecutionMode mode)
    {
        const string entry = "main.mjs";
        var files = new Dictionary<string, string>
        {
            [entry] = """
                export const marker = 1;
                function run(value) {
                    arguments[0] = 2;
                    console.log(value, arguments[0]);
                }
                run(1);
                """
        };
        Assert.Equal("1 2\n", TestHarness.RunModules(files, entry, mode, allowTypeErrors: true));
    }
}
