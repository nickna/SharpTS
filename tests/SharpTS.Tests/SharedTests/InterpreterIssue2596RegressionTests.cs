using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2596RegressionTests
{
    [Fact]
    public void OriginalIssueSourceRemainsCorrect()
    {
        const string source = "const constructor: any = ArrayBuffer;\nconst descriptor: any = Object.getOwnPropertyDescriptor(globalThis, \"ArrayBuffer\");\nconsole.log(descriptor === undefined ? \"absent\" : \"own\",\n    descriptor !== undefined && descriptor.value === constructor,\n    Reflect.get(globalThis, \"ArrayBuffer\") === constructor,\n    globalThis.ArrayBuffer === constructor);\n";
        Assert.Equal("own true true true\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}