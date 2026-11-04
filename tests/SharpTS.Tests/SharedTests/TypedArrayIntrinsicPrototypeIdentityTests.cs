using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class TypedArrayIntrinsicPrototypeIdentityTests
{
    [Theory, ModeData]
    public void ImplicitIntrinsicIdentityRoundTripsAfterPrevention(ExecutionMode mode)
    {
        Assert.Equal("true true true true\ntrue false false true\ntrue false true\ntrue false true\n", TestHarness.Run("""
            const first: any = new Uint8Array(1);
            const second: any = new Uint8Array(2);
            const other: any = new Int8Array(1);
            const before = Object.getPrototypeOf(first);
            console.log(before === Uint8Array.prototype, before.constructor === Uint8Array,
                before === Object.getPrototypeOf(second), before !== Object.getPrototypeOf(other));
            Object.preventExtensions(first);
            console.log(Reflect.setPrototypeOf(first, before), Reflect.setPrototypeOf(first, {}),
                Reflect.setPrototypeOf(first, Object.prototype), Object.getPrototypeOf(first) === before);
            const explicit: any = {};
            Object.setPrototypeOf(second, explicit);
            Object.preventExtensions(second);
            console.log(Reflect.setPrototypeOf(second, explicit), Reflect.setPrototypeOf(second, before), Object.getPrototypeOf(second) === explicit);
            Object.setPrototypeOf(other, null);
            Object.preventExtensions(other);
            console.log(Reflect.setPrototypeOf(other, null), Reflect.setPrototypeOf(other, before), Object.getPrototypeOf(other) === null);
            """, mode));
    }
}
