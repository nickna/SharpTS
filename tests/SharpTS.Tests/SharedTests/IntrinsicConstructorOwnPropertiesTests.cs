using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class IntrinsicConstructorOwnPropertiesTests
{
    [Theory, ModeData]
    public void IntrinsicTokensDoNotExposeTheirClrImplementationFields(ExecutionMode mode)
    {
        Assert.Equal("false true\nfalse false\ntrue true true\n", TestHarness.Run("""
            console.log(Object.hasOwn(Number, 'MaxValue'), Object.hasOwn(Number, 'MAX_VALUE'));
            console.log(Object.hasOwn(Boolean, 'TrueString'), Object.hasOwn(String, 'Empty'));
            class Source { static Value: number = 7; static method(): number { return 9; } }
            console.log(Object.hasOwn(Source, 'Value'), Object.hasOwn(Source, 'method'),
                Object.getOwnPropertyDescriptor(Source, 'Value')!.value === 7);
            """, mode));
    }
}
