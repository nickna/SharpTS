using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitiveSymbolReadTests
{
    [Theory, ModeData]
    public void PrimitiveSymbolReads_UseMatchingPrototypeAndInheritedDescriptors(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol('probe');
            const parent = { [key]: 11 };
            Object.setPrototypeOf(Boolean.prototype, parent);
            Object.setPrototypeOf(Number.prototype, parent);
            console.log(true[key], (7)[key]);
            Boolean.prototype[key] = 17;
            Number.prototype[key] = 23;
            console.log(true[key], (7)[key]);
            delete Boolean.prototype[key];
            delete Number.prototype[key];
            console.log(true[key], (7)[key]);
            delete parent[key];
            console.log(true[key] === undefined, (7)[key] === undefined);
            """;
        Assert.Equal("11 11\n17 23\n11 11\ntrue true\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void PrimitiveSymbolGettersAndCalls_KeepPrimitiveReceiver(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol('probe');
            function check(prototype, primitive) {
                Object.defineProperty(prototype, key, { get() {
                    'use strict';
                    console.log('get', this === primitive, typeof this);
                    return function () { 'use strict'; console.log('call', this === primitive); };
                } });
                primitive[key]();
            }
            check(Boolean.prototype, true);
            check(Number.prototype, 7);
            """;
        Assert.Equal("get true boolean\ncall true\nget true number\ncall true\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void CoercedSymbolKeys_AreConvertedOnceAndPropagateGetterFailures(ExecutionMode mode)
    {
        const string source = """
            const symbol = Symbol('probe');
            let conversions = 0;
            const key = { [Symbol.toPrimitive](hint) { conversions++; console.log(hint); return symbol; } };
            Boolean.prototype[symbol] = 17;
            console.log(true[key], conversions);
            const marker = {};
            Object.defineProperty(Number.prototype, symbol, { get() { throw marker; } });
            try { console.log((7)[key]); } catch (error) { console.log(error === marker, conversions); }
            console.log((7)?.[Symbol('missing')] === undefined);
            """;
        Assert.Equal("string\n17 1\nstring\ntrue 2\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
