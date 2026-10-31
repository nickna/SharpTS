using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ReadableStreamRejectionIdentityTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted, "({ token: 1 })")]
    [InlineData(ExecutionMode.Compiled, "({ token: 1 })")]
    [InlineData(ExecutionMode.Interpreted, "\"failure\"")]
    [InlineData(ExecutionMode.Compiled, "\"failure\"")]
    [InlineData(ExecutionMode.Interpreted, "42")]
    [InlineData(ExecutionMode.Compiled, "42")]
    [InlineData(ExecutionMode.Interpreted, "null")]
    [InlineData(ExecutionMode.Compiled, "null")]
    [InlineData(ExecutionMode.Interpreted, "undefined")]
    [InlineData(ExecutionMode.Compiled, "undefined")]
    public void PendingAndFutureReadsPreservePullRejectionReason(ExecutionMode mode, string reason)
    {
        var source = $$"""
            async function run() {
                    const reason = {{reason}};
                    const stream = new ReadableStream({
                        async pull(controller: any) { await Promise.resolve(); throw reason; }
                    });
                    const reader = stream.getReader();
                    try { await reader.read(); }
                    catch (error: any) { console.log(error === reason); }
                    try { await reader.read(); }
                    catch (error: any) { console.log(error === reason); }
            }
            run();
            """;
        Assert.Equal("true\ntrue\n", TestHarness.Run(source, mode));
    }
}
