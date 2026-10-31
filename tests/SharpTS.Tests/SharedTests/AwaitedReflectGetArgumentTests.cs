using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AwaitedReflectGetArgumentTests
{
    [Theory, ModeData]
    public void AwaitedArgumentsAreSpilledBeforeTheReflectCall(ExecutionMode mode)
        => Assert.Equal("7\ntarget,key,receiver\n", TestHarness.Run("""
            const trace: string[] = [];
            async function step(label: string, value: any) { trace.push(label); return value; }
            async function run() {
                const target: any = { value: 0, get x() { return this.value; } }, receiver: any = {value: 7};
                const value = Reflect.get(await step('target', target), await step('key', 'x'), await step('receiver', receiver));
                console.log(value);
                console.log(trace.join(','));
            }
            run();
            """, mode));
}
