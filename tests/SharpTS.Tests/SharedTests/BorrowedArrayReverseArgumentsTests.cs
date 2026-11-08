using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BorrowedArrayReverseArgumentsTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void TypedArrayAliasReadsArgumentsLengthWithoutCoercion(ExecutionMode mode)
    {
        const string source = """
            function run() {
                const alias: any[] = arguments as any;
                const length = { valueOf() { console.log("coerce"); return 2; } };
                (alias as any).length = length;
                console.log(alias.length === (length as any), typeof alias.length);
                for (let i = 0; i < 1; i++) console.log(alias.length === (length as any));
                console.log(Array.prototype.findLastIndex.call(alias, () => true));
            }
            run();
            """;

        Assert.Equal("true object\ntrue\ncoerce\n1\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void ReversePredicatesPreserveLargeArgumentsLengths(ExecutionMode mode)
    {
        const string source = """
            function run() {
                arguments.length = 2147483649;
                console.log(Array.prototype.findLastIndex.call(arguments, () => true));
            }
            run();

            function live() {
                const args = arguments;
                const context = {};
                const events = [];
                const length = { valueOf() { events.push("length"); return 2147483650.5; } };
                args.length = length;
                Object.defineProperty(args, "2147483649", {
                    get() { events.push("get"); return 42; },
                    configurable: true
                });
                console.log(args.length === length, events.length);
                console.log(Array.prototype.findLast.apply(args, [function(value, index, receiver) {
                    events.push("callback");
                    console.log(value, index, this === context, receiver === args);
                    delete receiver["2147483649"];
                    return true;
                }, context]));
                console.log(events.join(","));
            }
            live();
            """;

        Assert.Equal("2147483648\ntrue 0\n42 2147483649 true true\n42\nlength,get,callback\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
