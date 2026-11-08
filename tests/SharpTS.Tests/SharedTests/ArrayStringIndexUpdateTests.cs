using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayStringIndexUpdateTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void StringIndicesUseOrdinaryArrayReadsAndWrites(ExecutionMode mode)
    {
        const string source = """
            const values = [0];
            console.log(values['0']++, ++values['0'], values['0']--, --values['0'], values[0]);

            const own = [];
            let stored = 3;
            Object.defineProperty(own, '0', {
              get() { console.log(this === own); return stored; },
              set(value) { console.log(this === own); stored = value; }
            });
            console.log(own['0']++, stored);

            const inherited = [,];
            const proto = Object.create(Array.prototype);
            Object.defineProperty(proto, '0', {
              get() { console.log(this === inherited); return 9; },
              set(value) { console.log(this === inherited, value); }
            });
            Object.setPrototypeOf(inherited, proto);
            console.log(--inherited['0'], Object.hasOwn(inherited, '0'));

            const hole = [,];
            console.log(Number.isNaN(hole['0']++), Object.hasOwn(hole, '0'));
            const frozen = Object.freeze([4]);
            console.log(frozen['0']--, frozen[0]);
            try { (function () { 'use strict'; ++frozen['0']; })(); }
            catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("0 2 2 0 0\ntrue\ntrue\n3 4\ntrue\ntrue 8\n8 false\ntrue true\n4 4\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
