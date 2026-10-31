using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterQueueMicrotaskValidationTests
{
    [Theory]
    [InlineData("undefined")]
    [InlineData("null")]
    [InlineData("3")]
    [InlineData("new Proxy({}, {})")]
    public void InvalidCallbackThrowsGuestTypeErrorSynchronously(string expression)
    {
        var source = "const callback:any=" + expression + ";"
            + "try{queueMicrotask(callback);console.log('miss');}catch(error:any){console.log(error.name);}"
            + "console.log('sync');";
        Assert.Equal("TypeError\nsync\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void MissingCallbackThrowsGuestTypeError()
    {
        const string source = "const enqueue:any=queueMicrotask;"
            + "try{enqueue();console.log('miss');}catch(error:any){console.log(error.name);}";
        Assert.Equal("TypeError\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
