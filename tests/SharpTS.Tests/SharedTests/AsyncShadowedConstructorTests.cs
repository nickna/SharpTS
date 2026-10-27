using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncShadowedConstructorTests
{
    [Theory, ModeData]
    public void ConstructorCalleeRetainsItsBlockBindingAcrossAwait(ExecutionMode mode)
    {
        const string source = """
            function Outer(this: any) { this.y = 7; }
            function Inner(this: any) { this.y = 2; }
            async function run() {
                const receiver: any = { y: 90 };
                const bound: any = Outer.bind(receiver);
                {
                    const bound: any = Inner.bind(receiver);
                    await Promise.resolve();
                    const instance: any = new bound();
                    console.log('inner', instance.y, receiver.y, instance === receiver);
                }
                const instance: any = new bound();
                console.log('outer', instance.y, receiver.y, instance === receiver);
            }
            run().then(() => console.log('fulfilled'), (error: any) => console.log('rejected', error.name));
            """;
        Assert.Equal("inner 2 90 false\nouter 7 90 false\nfulfilled\n", TestHarness.Run(source, mode));
    }
}
