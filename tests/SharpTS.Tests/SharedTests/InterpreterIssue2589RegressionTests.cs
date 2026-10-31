using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2589RegressionTests
{
    [Theory, ModeData]
    public void OriginalIssueSourceRemainsCorrect(ExecutionMode mode)
    {
        const string source = "const marker = {};\nfunction check(label: string, descriptor: any) {\n    try { console.log(label, \"returned\", Reflect.defineProperty({}, \"x\", descriptor)); }\n    catch (error) { console.log(label, \"threw\", error === marker, error instanceof TypeError); }\n}\ncheck(\"value getter\", { get value() { throw marker; } });\ncheck(\"get getter\", { get get() { throw marker; } });\ncheck(\"mixed\", { value: 1, get() { return 2; } });\ncheck(\"noncallable get\", { get: 1 });\ncheck(\"noncallable set\", { set: 1 });\n";
        Assert.Equal("value getter threw true false\nget getter threw true false\nmixed threw false true\nnoncallable get threw false true\nnoncallable set threw false true\n", TestHarness.Run(source, mode));
    }
}