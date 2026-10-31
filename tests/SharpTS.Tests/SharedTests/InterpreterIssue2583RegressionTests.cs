using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2583RegressionTests
{
    [Theory, ModeData]
    public void OriginalIssueSourceRemainsCorrect(ExecutionMode mode)
    {
        const string source = "const f = function named() {\n    const read = () => named;\n    try { console.log(read() === f); }\n    catch (error: any) { console.log(error.name); }\n    let named: any = 1;\n    console.log(read());\n};\nf();\n";
        Assert.Equal("ReferenceError\n1\n", TestHarness.Run(source, mode));
    }
}