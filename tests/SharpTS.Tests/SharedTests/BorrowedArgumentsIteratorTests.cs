using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BorrowedArgumentsIteratorTests
{
    [Theory, ModeData]
    public void BorrowedArrayIteratorsRetainArgumentsWithoutReadingLength(ExecutionMode mode)
    {
        const string source = """
            function inspect() {
                const args = arguments;
                args.length = 2147483649;
                const keys = Array.prototype.keys.call(args);
                const values = Array.prototype.values.call(args);
                const entries = Array.prototype.entries.call(args);
                let reads = 0;
                Object.defineProperty(args, "length", { get() { reads++; return 1; } });
                args[0] = 42;
                console.log(reads);
                console.log(keys.next().value, values.next().value, entries.next().value.join(","));
                console.log(reads);
            }
            inspect(1);
            """;

        Assert.Equal("0\n0 42 0,42\n3\n", TestHarness.RunUnchecked(source, mode));
    }
}
