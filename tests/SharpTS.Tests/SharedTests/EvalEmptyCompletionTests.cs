using System.Text.Json;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EvalEmptyCompletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (body, expected) in new (string, string)[]
        {
            ("1;;;", "1"),
            ("1;var value;", "1"),
            ("1;var value=9;", "1"),
            ("1;var value=8;var value=9;", "1"),
            ("1;function f(){}", "1"),
            ("null;var value;", "null"),
            ("undefined;var value;", "undefined"),
            ("'use strict';", "use strict"),
            (";;;", "undefined"),
            ("", "undefined"),
            ("var value=0;++value;var value;value;", "1"),
        })
            foreach (bool dynamic in new[] { false, true })
                foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
                    yield return [body, expected, dynamic, mode];
    }

    [Theory, MemberData(nameof(Cases))]
    public void EmptyCompletionsKeepTheLastExpressionInStaticAndDynamicEval(string body, string expected, bool dynamic, ExecutionMode mode)
    {
        string argument = JsonSerializer.Serialize(body);
        if (dynamic) argument = "String(" + argument + ")";
        Assert.Equal(expected + "\n", TestHarness.Run("console.log(eval(" + argument + "));", mode));
    }

    [Theory, ModeData]
    public void StaticCompletionTrackingKeepsCallerLocalEffectsAndEvaluatesOnce(ExecutionMode mode)
    {
        const string source = "function outer(){let value=0;const completion=eval('++value;;;var marker;');console.log(completion,value);}outer();";
        Assert.Equal("1 1\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void StaticCompletionLocalProducesValidIl()
        => Assert.Empty(TestHarness.CompileAndVerifyOnly("function outer(){let value=0;const completion=eval('++value;;;var marker;');console.log(completion,value);}outer();"));
}
