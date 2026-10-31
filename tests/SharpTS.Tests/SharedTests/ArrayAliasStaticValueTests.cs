using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ArrayAliasStaticValueTests
{
    [Theory, ModeData]
    public void ArrayAliasesExposeCallableStaticMethods(ExecutionMode mode)
    {
        const string source = """
            const ctor: any = Array;
            console.log(typeof ctor.from, typeof ctor.of);
            console.log(ctor.from([3, 4]).join(","), ctor.of(5, 6).join(","));
            console.log(Object.getOwnPropertyDescriptor(ctor, "from")!.value === ctor.from);
            console.log(Object.getOwnPropertyDescriptor(ctor, "of")!.value === ctor.of);
            """;
        Assert.Equal("function function\n3,4 5,6\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }
}
