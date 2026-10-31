using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StaticAsyncGetterCaptureTests
{
    [Theory, ModeData]
    public void Issue2537Regression(ExecutionMode mode)
    {
        const string source = """
            const events: string[] = [];
            class C {
                static async make(label: string): Promise<any> {
                    return { get value() { events.push(label); return 47; } };
                }
            }
            async function run() {
                const value: any = await C.make('first');
                console.log(value.value);
                console.log(events.join(','));
            }
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """;
        Assert.Equal("47\nfirst\n", TestHarness.Run(source, mode));
    }
}
