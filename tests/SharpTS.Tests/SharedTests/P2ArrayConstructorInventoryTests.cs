using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ArrayConstructorInventoryTests
{
    [Theory, ModeData]
    public void ArrayOwnNamesIncludeIntrinsicAndCurrentOwnProperties(ExecutionMode mode)
    {
        const string source = """
            const ctor: any = Array;
            console.log(ctor.length, typeof ctor.from);
            let names = Object.getOwnPropertyNames(ctor);
            console.log(names.includes("name"), names.includes("length"), names.includes("prototype"), names.includes("from"));
            delete ctor.from;
            delete ctor.name;
            ctor.extra = 1;
            Object.defineProperty(ctor, "hidden", { value: 2 });
            names = Object.getOwnPropertyNames(ctor);
            console.log(names.includes("from"), names.includes("name"), names.includes("extra"), names.includes("hidden"));
            Object.defineProperty(ctor, "from", { value: 3, configurable: true });
            console.log(Object.getOwnPropertyNames(ctor).filter((name: string) => name === "from").length);
            """;
        Assert.Equal("1 function\ntrue true true true\nfalse false true true\n1\n", TestHarness.Run(source, mode));
    }
}
