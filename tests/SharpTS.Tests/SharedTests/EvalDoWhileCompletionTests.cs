using System.Text.Json;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EvalDoWhileCompletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, expected) in new (string, string)[]
        {
            ("1; do {} while(false)", "undefined"),
            ("2; do {3;} while(false)", "3"),
            ("1; do {break;} while(false)", "undefined"),
            ("2; do {3;break;} while(false)", "3"),
            ("4; do {continue;} while(false)", "undefined"),
            ("5; do {6;continue;} while(false)", "6"),
            ("1; do {3;var local;} while(false)", "3"),
            ("1; do {3;var local=9;} while(false)", "3"),
            ("1; do {3;var local=8;var local=9;} while(false)", "3"),
            ("1; do {3;;;} while(false)", "3"),
            ("1; do {3;null;} while(false)", "null"),
            ("1; do {3;undefined;} while(false)", "undefined"),
            ("1; do {null;} while(false)", "null"),
            ("1; do {{3;} {}} while(false)", "3"),
            ("1; do {3;if(false) 4;} while(false)", "undefined"),
            ("1; do {3;if(true) 4;} while(false)", "4"),
            ("1; do {try {3;} finally {4;}} while(false)", "3"),
            ("1; do {try {3;} finally {break;}} while(false)", "undefined"),
            ("1; do {try {throw 9;} catch(error) {6;}} while(false)", "6"),
            ("1; do {try {3;break;} finally {4;}} while(false)", "3"),
            ("1; outer: do {3;do {6;break outer;} while(false);} while(false)", "6"),
            ("var i=0;do {++i;continue;} while(i<3)", "3"),
            ("var i=0;do {++i;try {6;} finally {continue;}} while(i<2)", "undefined"),
            ("1; do {3;switch(0) {case 0: 6;break;}} while(false)", "6"),
            ("1; do {3;while(false){4;}} while(false)", "undefined"),
            ("1; do {3;for(;false;){4;}} while(false)", "undefined"),
            ("1; do {3;for(const item of []){4;}} while(false)", "undefined"),
            ("1; do {3;for(const item in {}){4;}} while(false)", "undefined"),
            ("1; do {} while(false);var tail;", "undefined"),
            ("1; do {3;} while(false);var tail;", "3"),
            ("1; do {3;} while(false);7;", "7"),
        })
            foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }) yield return [source, expected, mode];
    }

    [Theory, MemberData(nameof(Cases))]
    public void EvalReturnsActualNormalAndAbruptLoopCompletions(string body, string expected, ExecutionMode mode)
        => Assert.Equal(expected + "\n", TestHarness.Run("console.log(eval(" + JsonSerializer.Serialize(body) + "));", mode));

    internal const string DeploymentSource = "console.log(eval('1;do{}while(false)'));console.log(eval('2;do{3;break;}while(false)'));console.log(eval('var i=0;do{++i;continue;}while(i<3)'));";

    [Theory, ModeData]
    public void LoopControlEffectsExecuteOnceAndGuestThrowsKeepTheirValue(ExecutionMode mode)
    {
        const string source = "console.log(eval('var count=0;var value;do{value=++count;}while(count<3);value'));try{eval('do{throw 7;}while(false)');}catch(error){console.log(error);}function f(){eval('do{3;}while(false)');}console.log(f());";
        Assert.Equal("3\n7\nundefined\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void EvalLoopBridgeProducesValidIl()
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(DeploymentSource));
}
