using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2654RegressionTests
{
    [Theory, ModeData]
    public void OriginalIssueSourceRemainsCorrect(ExecutionMode mode)
    {
        const string source = "class P extends Promise<any> {}\nconst intrinsic: any = Promise, method: any = intrinsic.withResolvers;\nconst record: any = method.call(P);\nconsole.log(record.promise instanceof P, record.promise instanceof Promise,\n    typeof record.resolve, typeof record.reject);\nrecord.resolve(7);\nrecord.promise.then((value: any) => console.log(value));\n";
        Assert.Equal("true true function function\n7\n", TestHarness.Run(source, mode));
    }
}