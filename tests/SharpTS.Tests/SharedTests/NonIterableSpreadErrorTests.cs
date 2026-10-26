using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NonIterableSpreadErrorTests
{
    [Theory, ModeData]
    public void ArgumentSpreadRejectsNonIterableValuesWithTypeError(ExecutionMode mode)
    {
        const string source = """
            let calls = 0;
            let later = 0;
            function consume(...values: any[]) { calls++; }
            function next() { later++; return 0; }
            const values: any[] = [123, null, undefined];
            for (const value of values) {
                try { consume(...value, next()); }
                catch (error: any) { console.log(error.name, error instanceof TypeError); }
            }
            console.log(calls, later);
            const marker = {};
            const abrupt: any = { get [Symbol.iterator]() { throw marker; } };
            try { consume(...abrupt); } catch (error: any) { console.log(error === marker); }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("TypeError true\n", 3)) + "0 0\ntrue\n",
            TestHarness.Run(source, mode));
    }
}
