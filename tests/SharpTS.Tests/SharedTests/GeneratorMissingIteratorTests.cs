using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GeneratorMissingIteratorTests
{
    [Theory, ModeData]
    public void SpreadRejectsMissingInheritedGeneratorIterator(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const prototype = Object.getPrototypeOf(values());
            for (const missing of [undefined, null]) {
                Object.defineProperty(prototype, Symbol.iterator, { value: missing, configurable: true });
                try { console.log([...values()].join(',')); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            let reads = 0;
            Object.defineProperty(prototype, Symbol.iterator, {
                get() { reads++; return undefined; }, configurable: true
            });
            try { console.log([...values()].join(',')); }
            catch (error) { console.log(error instanceof TypeError, reads); }
            """;
        Assert.Equal("true\ntrue\ntrue 1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void MissingFactoryRejectsRequiredConsumersAndAllowsArrayLikeFallback(ExecutionMode mode)
    {
        const string source = """
            function* values(){yield 1;}
            const iterator = values();
            let reads = 0;
            Object.defineProperty(Object.getPrototypeOf(iterator), Symbol.iterator, {
                get(){reads++; return null;}
            });
            function consume(){return arguments.length;}
            try {consume(...iterator);} catch(e){console.log(e instanceof TypeError);}
            try {for(const value of iterator){console.log('unexpected');}} catch(e){console.log(e instanceof TypeError);}
            console.log(Array.from(iterator).length, reads);
            console.log(iterator.next().value);
            """;
        Assert.Equal("true\ntrue\n0 3\n1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void InheritedFactoryRunsOnceWithGeneratorReceiver(ExecutionMode mode)
    {
        const string source = """
            function* values(){yield 1;}
            const iterator = values();
            let reads = 0;
            Object.defineProperty(Object.getPrototypeOf(iterator), Symbol.iterator, {
                get(){reads++; console.log(this === iterator); return function(){
                    console.log(this === iterator); return [7, 8][Symbol.iterator]();
                };}
            });
            console.log([...iterator].join(','), reads);
            """;
        Assert.Equal("true\ntrue\n7,8 1\n", TestHarness.RunUnchecked(source, mode));
    }
}
