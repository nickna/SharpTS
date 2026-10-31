using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2FunctionConstructorPrototypeDescriptorTests
{
    [Theory, ModeData]
    public void FunctionPrototypeIsAnImmutableDataProperty(ExecutionMode mode)
    {
        const string source = """
            const constructor:any=Error.constructor;
            const descriptor:any=Object.getOwnPropertyDescriptor(constructor,'prototype');
            console.log(descriptor.value===Function.prototype,
                descriptor.writable,descriptor.enumerable,descriptor.configurable);
            console.log(Object.prototype.hasOwnProperty.call(descriptor,'get'),
                Object.prototype.hasOwnProperty.call(descriptor,'set'));
            const method:any=Object.getOwnPropertyDescriptor(Function.prototype,'call');
            console.log(method.writable,method.enumerable,method.configurable);
            """;
        Assert.Equal("true false false false\nfalse false\ntrue false true\n", TestHarness.Run(source, mode));
    }
}
