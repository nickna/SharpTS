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
            const values: any[] = [123, null, undefined, {}, { length: 0 }, { value: 1 }];
            for (const value of values) {
                try { consume(...value, next()); }
                catch (error: any) { console.log(error.name, error instanceof TypeError); }
            }
            console.log(calls, later);
            const marker = {};
            const abrupt: any = { get [Symbol.iterator]() { throw marker; } };
            try { consume(...abrupt); } catch (error: any) { console.log(error === marker); }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("TypeError true\n", 6)) + "0 0\ntrue\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OrdinaryObjectsCannotSpreadIntoArraysOrConstructors(ExecutionMode mode)
    {
        const string source = """
            let calls = 0;
            class Target { constructor(...values: any[]) { calls++; } }
            const value: any = {};
            try { new Target(...value); }
            catch (error: any) { console.log(error instanceof TypeError); }
            try { const result = [...value]; }
            catch (error: any) { console.log(error instanceof TypeError); }
            console.log(calls);
            const iterable: any = { [Symbol.iterator]() {
                let value = 3;
                return { next() { value++; return { value, done: value > 5 }; } };
            } };
            console.log([...iterable].join(","));
            """;
        Assert.Equal("true\ntrue\n0\n4,5\n", TestHarness.Run(source, mode));
    }
}
