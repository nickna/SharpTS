using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ClassConstructorLanguageEnumerationTests
{
    // Compiled constructor rest and inherited for-in use separate runtime paths.
    [Theory, InterpretedOnlyData]
    public void ClassConstructorsParticipateInSpreadRestAndSyncAsyncForIn(ExecutionMode mode)
    {
        const string source = """
            class Parent { static inherited = 2; static hidden() {} }
            class Owner extends Parent { static value = 1; static method() {} }
            const owner: any = Owner;
            const copy: any = { ...owner };
            const { ...rest } = owner;
            console.log(Object.keys(owner).join(","), copy.value, rest.value, copy.inherited === undefined);
            let keys = "";
            for (const key in owner) keys += key + ",";
            console.log(keys);
            async function run() {
                await Promise.resolve(0);
                let keys = "";
                for (const key in owner) keys += key + ",";
                console.log(keys);
            }
            run();
            """;
        Assert.Equal("value 1 1 true\nvalue,inherited,\nvalue,inherited,\n", TestHarness.Run(source, mode));
    }
}
