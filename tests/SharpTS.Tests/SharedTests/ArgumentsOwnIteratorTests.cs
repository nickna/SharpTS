using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ArgumentsOwnIteratorTests
{
    [Theory, ModeData]
    public void ArgumentsIteratorReadsLengthGetterOnEachAdvance(ExecutionMode mode)
    {
        const string source = """
            function inspect() {
                const args = arguments;
                let reads = 0;
                Object.defineProperty(args, "length", { get() { reads++; return 1; } });
                const iterator = args[Symbol.iterator]();
                console.log(reads);
                const first = iterator.next();
                console.log(first.value, first.done, reads);
                console.log(iterator.next().done, reads);
            }
            inspect();
            """;

        Assert.Equal("0\nundefined false 1\ntrue 2\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ArgumentsOwnIteratorIsTheCanonicalArrayValuesFunction(ExecutionMode mode)
    {
        const string source = """
            function inspect(first: any, second: any) {
                const args: any = arguments;
                const descriptor: any = Object.getOwnPropertyDescriptor(args, Symbol.iterator);
                console.log(Object.hasOwn(args, Symbol.iterator), descriptor.value === Array.prototype.values);
                console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);
                console.log([...args].join(","));
                delete args[Symbol.iterator];
                console.log(Object.hasOwn(args, Symbol.iterator));
            }
            inspect(3, 4);
            """;
        Assert.Equal("true true\ntrue false true\n3,4\nfalse\n", TestHarness.Run(source, mode));
    }
}
