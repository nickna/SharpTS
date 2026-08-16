using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Promise.withResolvers().
/// </summary>
public class PromiseWithResolversTests
{
    [Theory, ModeData]
    public void WithResolvers_BasicResolve(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                const { promise, resolve, reject } = Promise.withResolvers();
                resolve(42);
                const result = await promise;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void WithResolvers_BasicReject(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                const { promise, resolve, reject } = Promise.withResolvers();
                reject("error message");
                try {
                    await promise;
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void WithResolvers_ResolveWithString(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                const r = Promise.withResolvers();
                r.resolve("hello");
                const val = await r.promise;
                console.log(val);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void WithResolvers_HasAllProperties(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                const r = Promise.withResolvers();
                console.log(typeof r.promise);
                console.log(typeof r.resolve);
                console.log(typeof r.reject);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\nfunction\nfunction\n", output);
    }

    [Theory, ModeData]
    public void WithResolvers_ResolveWithNumber(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                const { promise, resolve } = Promise.withResolvers();
                resolve(100);
                const val = await promise;
                console.log(val);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }
}
