using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ClassExplicitDefinitionExtensibilityTests
{
    [Theory, ModeData]
    public void NonextensibleConstructorsRejectNewExplicitDefinitions(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol("new");
            class Owner { static existing = 1; }
            const owner: any = Owner;
            Object.preventExtensions(owner);
            console.log(Reflect.defineProperty(owner, "added", { value: 1 }), Object.hasOwn(owner, "added"));
            console.log(Reflect.defineProperty(owner, key, { value: 2 }), Object.hasOwn(owner, key));
            console.log(Reflect.defineProperty(owner, "existing", { value: 3 }), owner.existing);
            """;
        Assert.Equal("false false\nfalse false\ntrue 3\n", TestHarness.Run(source, mode));
    }
}
