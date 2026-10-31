using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ExpiredBlockCaptureTests
{
    [Theory, ModeData]
    public void LaterClosureCapturesVisibleModuleAndAncestorBindings(ExecutionMode mode)
    {
        const string source = """
            const value = 'module';
            function outer() {
                const other = 'ancestor';
                async function run() {
                    {
                        const value = 'block';
                        const other = 'inner';
                        console.log(value, other);
                    }
                    await Promise.resolve();
                    const read = () => value + ' ' + other;
                    console.log(read());
                }
                return run;
            }
            outer()();
            """;
        Assert.Equal("block inner\nmodule ancestor\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExitedBlockDoesNotHideOuterBindingInSynchronousClosure(ExecutionMode mode)
    {
        const string source = """
            let value = 1;
            function outer() {
                let other = 2;
                function run() {
                    { let value = 10; let other = 20; console.log(value, other); }
                    const read = () => value + other;
                    console.log(read());
                    value = 3; other = 4;
                    console.log(read());
                }
                run();
            }
            outer();
            """;
        Assert.Equal("10 20\n3\n7\n", TestHarness.Run(source, mode));
    }
}
