using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalCompoundLogicalAssignmentTests
{
    [Theory, ModeData]
    public void RenamedCapturedBlockBindingsRemainLocal(ExecutionMode mode)
    {
        const string source = """
            globalThis.value = 1000;
            function* values() {
                const value = 100;
                {
                    let value = 5;
                    const update = () => { value += 2; value &&= 9; return value; };
                    yield update();
                    yield value;
                }
                yield value;
            }
            console.log([...values()].join(','), globalThis.value);
            delete globalThis.value;
            """;
        Assert.Equal("9,9,100 1000\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void CompoundAssignmentsStoreTheirResultsInTheGlobalBinding(ExecutionMode mode)
    {
        const string source = """
            globalThis.counter = 1;
            console.log(counter += 2, globalThis.counter);
            console.log(counter -= 1, counter *= 3, counter /= 2, globalThis.counter);
            console.log(counter %= 2, counter **= 3, counter |= 8, counter ^= 3, counter &= 7);
            console.log(counter <<= 2, counter >>= 1, counter >>>= 1, globalThis.counter);
            console.log(counter += 'x', globalThis.counter);
            delete globalThis.counter;
            """;
        Assert.Equal("3 3\n2 6 3 3\n1 1 9 10 2\n8 4 2 2\n2x 2x\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void LogicalAssignmentsShortCircuitAndWriteThroughAccessorsOnce(ExecutionMode mode)
    {
        const string source = """
            let value = 1;
            const events = [];
            Object.defineProperty(globalThis, 'counter', {
                configurable: true,
                get() { events.push('get'); return value; },
                set(next) { events.push('set:' + next); value = next; }
            });
            function rhs() { events.push('rhs'); return 2; }
            console.log(counter ||= rhs(), events.join(',')); events.length = 0;
            console.log(counter &&= rhs(), events.join(',')); events.length = 0;
            console.log(counter ??= rhs(), events.join(',')); events.length = 0;
            value = null;
            console.log(counter ??= rhs(), events.join(',')); events.length = 0;
            console.log(counter += rhs(), value, events.join(','));
            delete globalThis.counter;
            """;
        Assert.Equal("1 get\n2 get,rhs,set:2\n2 get\n2 get,rhs,set:2\n4 4 get,rhs,set:4\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void StrictGlobalWritesRespectDescriptorsAndMissingReadsPrecedeTheRhs(ExecutionMode mode)
    {
        const string source = """
            Object.defineProperty(globalThis, 'counter', { value: 1, writable: false, configurable: true });
            console.log(counter += 2, counter);
            function strict() {
                'use strict';
                console.log(counter ||= 2);
                try { counter += 2; } catch (error) { console.log(error instanceof TypeError); }
                try { counter &&= 2; } catch (error) { console.log(error instanceof TypeError); }
            }
            strict();
            delete globalThis.counter;
            let effects = 0;
            try { counter += ++effects; } catch (error) { console.log(error instanceof ReferenceError, effects); }
            try { counter ||= ++effects; } catch (error) { console.log(error instanceof ReferenceError, effects); }
            """;
        Assert.Equal("3 1\n1\ntrue\ntrue\ntrue 0\ntrue 0\n", TestHarness.RunUnchecked(source, mode));
    }
}
