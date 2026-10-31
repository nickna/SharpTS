using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2FunctionConstructorMetadataReadTests
{
    [Theory, ModeData]
    public void IndirectFunctionConstructorReadsRetainIntrinsicMetadata(ExecutionMode mode)
    {
        const string source = """
            const constructor:any=Error.constructor;
            console.log(constructor===Function,constructor.length,Reflect.get(constructor,'length'));
            const descriptor:any=Object.getOwnPropertyDescriptor(constructor,'length');
            console.log(descriptor.value,descriptor.writable,descriptor.enumerable,descriptor.configurable);
            const error:any=Error('message');
            console.log(error.constructor.length);
            """;
        Assert.Equal("true 1 1\n1 false false true\n1\n", TestHarness.Run(source, mode));
    }
}
