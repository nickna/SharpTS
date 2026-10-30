using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2905RegressionTests
{
    [Theory, ModeData]
    public void OriginalIssueSourceRemainsCorrect(ExecutionMode mode)
    {
        const string source = "console.log(isNaN(undefined as any), globalThis.isNaN(undefined as any));\nconsole.log(isNaN(\"abc\" as any), globalThis.isNaN(\"abc\" as any));\n";
        Assert.Equal("true true\ntrue true\n", TestHarness.Run(source, mode));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["function-identity", "const bare=isNaN,qualified=globalThis.isNaN;console.log(bare===qualified,qualified!==Number.isNaN,qualified(undefined as any),qualified(\"bad\" as any));let calls=0;console.log(qualified({valueOf(){calls++;return\"bad\";}} as any),calls);", "true true true true\ntrue 1\n"];
        yield return ["global-mutation", "const original=isNaN;const object:any=globalThis;object.isNaN=Number.isNaN;console.log(object.isNaN===Number.isNaN,object.isNaN(\"bad\"));object.isNaN=original;console.log(object.isNaN===original,object.isNaN(\"bad\"));console.log(delete object.isNaN,object.isNaN===undefined);object.isNaN=original;console.log(object.isNaN===original);", "true false\ntrue true\ntrue true\ntrue\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OrderedProtocolControls(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
