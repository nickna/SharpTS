using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PromisePrimitiveGetterReceiverTests
{
    [Theory, ModeData]
    public void GenericCatchGetterReceivesOriginalSymbol(ExecutionMode mode)
    {
        Assert.Equal("true symbol\n7\n", TestHarness.RunUnchecked("""
            const value = Symbol('value');
            Object.defineProperty(Symbol.prototype, 'then', {
                get: function() {
                    'use strict';
                    console.log(this === value, typeof this);
                    return function() { return 7; };
                }, configurable: true
            });
            console.log(Promise.prototype.catch.call(value));
            """, mode));
    }
}
