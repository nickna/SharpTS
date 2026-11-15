using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitiveIterableConsumerTests
{
    [Theory, ModeData]
    public void PrimitivePrototypeFactories_AreUsedByOrdinaryConsumers(ExecutionMode mode)
    {
        const string source = """
            Boolean.prototype[Symbol.iterator] = function* () { yield this.valueOf(); };
            Number.prototype[Symbol.iterator] = function* () { yield this.valueOf(); };
            function check(primitive) {
                for (const value of primitive) console.log(value);
                console.log([...primitive].join(','));
                console.log(Array.from(primitive).join(','));
            }
            check(true);
            check(7);
            """;
        Assert.Equal("true\ntrue\ntrue\n7\n7\n7\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void PrimitiveIteratorGetterAndFactory_RetainReceiverAndCloseOnBreak(ExecutionMode mode)
    {
        const string source = """
            function check(prototype, primitive) {
                Object.defineProperty(prototype, Symbol.iterator, { configurable: true, get() {
                    'use strict';
                    console.log('get', this === primitive);
                    return function () {
                        'use strict';
                        console.log('factory', this === primitive, arguments.length);
                        let count = 0;
                        return {
                            next() { return { value: ++count, done: count > 1 }; },
                            return() { console.log('close', arguments.length); return {}; }
                        };
                    };
                } });
                for (const value of primitive) { console.log(value); break; }
                console.log([...primitive].join(','));
                console.log(Array.from(primitive).join(','));
            }
            check(Boolean.prototype, false);
            check(Number.prototype, 7);
            """;
        const string expected = "get true\nfactory true 0\n1\nclose 0\n"
            + "get true\nfactory true 0\n1\nget true\nfactory true 0\n1\n";
        Assert.Equal(expected + expected, TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void PrimitiveIteratorLookup_DistinguishesAbsentInvalidAndThrowingMethods(ExecutionMode mode)
    {
        const string source = """
            function check(prototype, primitive) {
                for (const method of [null, undefined, 7]) {
                    prototype[Symbol.iterator] = method;
                    try { for (const value of primitive) {} } catch (e) { console.log(e instanceof TypeError); }
                    try { console.log([...primitive]); } catch (e) { console.log(e instanceof TypeError); }
                    try { console.log(Array.from(primitive).length); } catch (e) { console.log(e instanceof TypeError); }
                }
                const marker = {};
                Object.defineProperty(prototype, Symbol.iterator, { get() { throw marker; } });
                try { for (const value of primitive) {} } catch (e) { console.log(e === marker); }
                try { console.log([...primitive]); } catch (e) { console.log(e === marker); }
                try { console.log(Array.from(primitive)); } catch (e) { console.log(e === marker); }
            }
            check(Boolean.prototype, false);
            check(Number.prototype, 7);
            """;
        const string expected = "true\ntrue\n0\ntrue\ntrue\n0\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n";
        Assert.Equal(expected + expected, TestHarness.RunUnchecked(source, mode));
    }
}
