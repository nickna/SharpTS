using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class StrictIntrinsicWritesTests
{
    [Theory, ModeData]
    public void NamedArrayGetterOnlyWritesThrowInStrictCode(ExecutionMode mode)
    {
        Assert.Equal("true 7 false\n", TestHarness.RunUnchecked("""
            Object.defineProperty(Array.prototype, 'getter', { get() { return 7; }, configurable: true });
            const array = [];
            function write() {
                'use strict';
                try { array.getter = 3; } catch (error) {
                    console.log(error instanceof TypeError, array.getter, Object.hasOwn(array, 'getter'));
                }
            }
            write();
            """, mode));
    }

    [Theory, ModeData]
    public void RegExpGetterOnlyWritesThrowInStrictCode(ExecutionMode mode)
    {
        Assert.Equal("true true false\n", TestHarness.RunUnchecked("""
            const re = /a/g;
            function write() {
                'use strict';
                try { re.global = false; } catch (error) {
                    console.log(error instanceof TypeError, re.global, Object.hasOwn(re, 'global'));
                }
            }
            write();
            """, mode));
    }
}
