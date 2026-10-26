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
            ("1;{}", "1"),
            ("{2;}", "2"),
            ("1;{{};var value;}", "1"),
            ("1;for(1;false;){2;}", "undefined"),
            ("1;for(var index=0;index<2;index++){index+3;}", "4"),
            ("for(var index=0;index<2;index++){index+3;var local;}", "4"),
            ("for(var index=0;index<2;index++){index+3;continue;}", "4"),
            ("for(var index=0;index<2;index++){5;break;}", "5"),
            ("for(var index=0;index<2;index++){7;for(var inner=0;inner<0;inner++){8;}}", "undefined"),
            ("{(()=>{11;return 2;})();}", "2"),
            ("1;for(var index=0;index<2;index++){}", "undefined"),
            ("{eval('2;{}');}", "2"),
            ("{if(true)42;}", "42"),
            ("1;{if(false)42;}", "undefined"),
            ("1;{if(true){}}", "undefined"),
            ("{if(false)1;else 2;}", "2"),
            ("for(var index=0;index<3;index++){if(index===1){8;break;}else{index+2;}}", "8"),
            ("for(var index=0;index<3;index++){if(index===2){8;continue;}else{index+2;}}", "8"),
            ("{if(true){eval('2;{}');}}", "2"),
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

    internal const string BlockAndForCallerSource = "function outer(){let value=0;console.log(eval('{++value;}'),value);console.log(eval('for(var index=0;index<2;index++){++value;}'),value);}outer();";

    [Theory, ModeData]
    public void StaticBlockAndForCompletionsKeepCallerLocalEffects(ExecutionMode mode)
        => Assert.Equal("1 1\n3 3\n", TestHarness.Run(BlockAndForCallerSource, mode));

    [Fact]
    public void StaticBlockAndForCompletionCaptureProducesValidIl()
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(BlockAndForCallerSource));

    internal const string ConditionalCallerSource = "function outer(){let checks=0;let writes=0;const result=eval('{if(++checks===1){++writes;42;}else{writes=9;7;}}');console.log(result,checks,writes);}outer();";

    [Theory, ModeData]
    public void StaticConditionalCompletionChecksAndWritesExecuteOnce(ExecutionMode mode)
        => Assert.Equal("42 1 1\n", TestHarness.Run(ConditionalCallerSource, mode));

    [Fact]
    public void StaticConditionalCompletionCaptureProducesValidIl()
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(ConditionalCallerSource));

    [Fact]
    public void StaticCompletionLocalProducesValidIl()
        => Assert.Empty(TestHarness.CompileAndVerifyOnly("function outer(){let value=0;const completion=eval('++value;;;var marker;');console.log(completion,value);}outer();"));
}
