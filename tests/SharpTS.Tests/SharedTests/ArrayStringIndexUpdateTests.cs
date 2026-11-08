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

    [Theory, ModeData]
    public void LogicalAssignmentsShortCircuitAndWriteThroughStringIndices(ExecutionMode mode)
    {
        const string source = """
            const values = [0, , 1];
            console.log(values['0'] ||= 5, values[0], values['1'] ??= 6, values[1], values['2'] &&= 7, values[2]);

            const trace = [];
            const own = [];
            let stored = 0;
            Object.defineProperty(own, '0', {
                get() { trace.push(this === own ? 'get' : 'wrong getter'); return stored; },
                set(value) { trace.push(this === own ? 'set' : 'wrong setter'); stored = value; }
            });
            function key() { trace.push('key'); return '0'; }
            function rhs() { trace.push('rhs'); return 8; }
            console.log(own[key()] ||= rhs(), own[key()] ||= rhs(), trace.join(','));

            const frozen = Object.freeze([0]);
            console.log(frozen['0'] ||= 9, frozen[0]);
            try { (function () { 'use strict'; frozen['0'] ||= 9; })(); }
            catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("5 5 6 6 7 7\n8 8 key,get,rhs,set,key,get\n9 0\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
