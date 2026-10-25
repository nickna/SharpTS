using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ShadowedAssignmentBindingExecutionTests
{
    [Theory, ModeData]
    public void ShadowWritesLeaveTheOuterParametersValueAndTypeIntact(ExecutionMode mode)
    {
        const string source = """
            function read(value: string) {
                try { throw 0; } catch (value) {
                    value = 1;
                    console.log(value);
                }
                for (let value of [1]) {
                    value = 2;
                    console.log(value);
                }
                const outer: string = value;
                console.log(outer);
            }
            read('outer');
            """;
        Assert.Equal("1\n2\nouter\n", TestHarness.Run(source, mode));
    }
}
