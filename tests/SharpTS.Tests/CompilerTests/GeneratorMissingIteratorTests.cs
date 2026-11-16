using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GeneratorMissingIteratorTests
{
    // Interpreter spread still bypasses inherited missing @@iterator overrides
    // (#3707). Keep the emitted IteratorCollection regression covered independently.
    [Fact]
    public void SpreadRejectsMissingInheritedGeneratorIterator()
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
        Assert.Equal("true\ntrue\ntrue 1\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }
}
