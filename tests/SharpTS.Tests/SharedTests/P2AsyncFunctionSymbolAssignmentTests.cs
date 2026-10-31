using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2AsyncFunctionSymbolAssignmentTests
{
    [Theory, ModeData]
    public void AsyncCallableValuesRetainOwnSymbolAssignments(ExecutionMode mode)
    {
        const string source = """
            async function declared() {}
            const expression = async function() {};
            const arrow = async () => {};
            const key = Symbol("own");
            for (const target of [declared, expression, arrow] as any[]) {
                target[key] = 7;
                console.log(target[key], Object.hasOwn(target, key), Object.getOwnPropertyDescriptor(target, key)!.value);
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("7 true 7\n", 3)), TestHarness.Run(source, mode));
    }
}
