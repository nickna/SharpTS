using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class MappedArgumentsTests
{
    public static IEnumerable<object[]> Cases()
    {
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
            delete live.args[0];
            live.args[0] = 13;
            live.set(20);
            console.log(live.args[0], live.get());
            """, "3 4 5 5\n8 8 8,15\n5\n8\n13 20\n"];
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
