using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2587RegressionTests
{
    [Fact]
    public void OriginalIssueSourceRemainsCorrect()
    {
        const string source = "function check(label: string, target: any) {\n    try { console.log(\"get\", label, Reflect.get(target, \"marker\")); }\n    catch (error) { console.log(\"get\", label, \"threw\", error instanceof TypeError); }\n    try { console.log(\"set\", label, Reflect.set(target, \"marker\", 7)); }\n    catch (error) { console.log(\"set\", label, \"threw\", error instanceof TypeError); }\n}\ncheck(\"null\", null);\ncheck(\"undefined\", undefined);\ncheck(\"boolean\", true);\ncheck(\"number\", 1);\ncheck(\"string\", \"text\");\ncheck(\"bigint\", 1n);\ncheck(\"symbol\", Symbol(\"target\"));\n";
        Assert.Equal("get null threw true\nset null threw true\nget undefined threw true\nset undefined threw true\nget boolean threw true\nset boolean threw true\nget number threw true\nset number threw true\nget string threw true\nset string threw true\nget bigint threw true\nset bigint threw true\nget symbol threw true\nset symbol threw true\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}