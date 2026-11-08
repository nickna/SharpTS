using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BorrowedArrayReverseSearchTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void ReversePredicatesUseSafeIntegerIndicesAndLiveGets(ExecutionMode mode)
    {
        const string source = """
            const ordinary = { length: 1048577 };
            console.log(Array.prototype.findLastIndex.call(ordinary, () => true));

            const trace = [];
            const context = {};
            const target = {
                get length() { trace.push("length getter"); return Infinity; },
                get "9007199254740989"() { trace.push("element getter"); return 42; }
            };
            const source = new Proxy(target, {
                get(target, key, receiver) { trace.push(key); return Reflect.get(target, key, receiver); }
            });
            function predicate() {
                trace.push("argument");
                return function(value, index, receiver) {
                    console.log(value, index, this === context, receiver === source);
                    return value !== undefined;
                };
            }
            console.log([].findLast.call(source, ...[predicate(), context]));
            console.log(trace.join(","));
            """;

        Assert.Equal("1048576\nundefined 9007199254740990 true true\n42 9007199254740989 true true\n42\n"
            + "argument,length,length getter,9007199254740990,9007199254740989,element getter\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void ReversePredicatesValidateAfterCoercingLengthAndBeforeReadingElements(ExecutionMode mode)
    {
        const string source = """
            const trace = [];
            const receiver = {
                get length() {
                    trace.push("length getter");
                    return { valueOf() { trace.push("length valueOf"); return 1; } };
                },
                get "0"() { trace.push("element getter"); return 42; }
            };
            function arg(value) { trace.push(value); return value; }
            try { Array.prototype.findLastIndex.call(receiver, arg("predicate"), arg("thisArg"), arg("extra")); }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(trace.join(","));
            """;

        Assert.Equal("true\npredicate,thisArg,extra,length getter,length valueOf\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
