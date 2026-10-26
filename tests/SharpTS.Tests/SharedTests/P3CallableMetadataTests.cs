using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3CallableMetadataTests
{
    [Theory, ModeData]
    public void NumberConstructorAliasHasItsOwnInitialLength(ExecutionMode mode)
    {
        const string source = """
            const constructor: any = Number;
            const descriptor: any = Object.getOwnPropertyDescriptor(constructor, "length");
            console.log(constructor.length, Reflect.get(constructor, "length"), descriptor.value);
            console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);
            Object.defineProperty(constructor, "length", { value: 5, configurable: true });
            console.log(constructor.length, Reflect.get(constructor, "length"));
            console.log(delete constructor.length, Object.hasOwn(constructor, "length"));
            """;
        Assert.Equal("1 1 1\nfalse false true\n5 5\ntrue false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DeclaredFunctionLengthReadsObserveDescriptorChanges(ExecutionMode mode)
    {
        const string source = """
            function fn(first: any) { return first; }
            Object.defineProperty(fn, "length", { value: 4, configurable: true });
            console.log(fn.length, fn(17));
            Object.defineProperty(fn, "length", { get() { return 6; }, configurable: true });
            console.log(fn.length);
            console.log(delete fn.length, fn.length);
            """;
        Assert.Equal("4 17\n6\ntrue 0\n", TestHarness.Run(source, mode));
    }
}
