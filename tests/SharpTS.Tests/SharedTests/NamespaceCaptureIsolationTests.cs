using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceCaptureIsolationTests
{
    [Theory, ModeData]
    public void PrivateNamespaceVarDoesNotBecomeOuterClosureCapture(ExecutionMode mode)
    {
        const string source = """
            namespace Hidden { var Number = 1; }
            const read = () => typeof Number;
            console.log(read());
            """;
        Assert.Equal("function\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NamespaceMembersDoNotHideExistingOuterBindings(ExecutionMode mode)
    {
        const string source = """
            const value = 'outer';
            namespace Hidden {
                var value = 'private';
                export function read() { return value; }
            }
            const read = () => value;
            console.log(read(), Hidden.read());
            """;
        Assert.Equal("outer private\n", TestHarness.Run(source, mode));
    }
}
