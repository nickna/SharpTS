using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2582RegressionTests
{
    [Theory, ModeData]
    public void OriginalIssueSourceRemainsCorrect(ExecutionMode mode)
    {
        const string source = "async function run() {\n    let value: any;\n    console.log(typeof value, value === undefined);\n}\nrun();\n";
        Assert.Equal("undefined true\n", TestHarness.Run(source, mode));
    }
}