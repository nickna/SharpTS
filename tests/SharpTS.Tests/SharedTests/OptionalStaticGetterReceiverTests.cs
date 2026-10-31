using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class OptionalStaticGetterReceiverTests
{
    [Theory, ModeData]
    public void DetachedStaticReadsShortCircuitBothNullishReceivers(ExecutionMode mode)
        => Assert.Equal("true true 1\ntrue true 2\n", TestHarness.Run("""
            class Base {
                static get value(): number { return 1; }
                static field: number = 2;
                static read() { return this?.value; }
                static readField() { return this?.field; }
            }
            const read: any = Base.read;
            console.log(read.call(null) === undefined, read.call(undefined) === undefined, read.call(Base));
            const readField: any = Base.readField;
            console.log(readField.call(null) === undefined, readField.call(undefined) === undefined, readField.call(Base));
            """, mode));
}
