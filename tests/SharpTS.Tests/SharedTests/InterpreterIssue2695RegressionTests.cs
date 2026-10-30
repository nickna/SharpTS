using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2695RegressionTests
{
    [Theory, ModeData]
    public void OriginalIssueSourceRemainsCorrect(ExecutionMode mode)
    {
        const string source = "console.log(\"null\", isNaN(null as any), globalThis.isNaN(null as any));\nconsole.log(\"empty\", isNaN(\"\" as any), globalThis.isNaN(\"\" as any));\n";
        Assert.Equal("null false false\nempty false false\n", TestHarness.Run(source, mode));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["primitive-coercion", "for(const value of [null,\"\",\" \",\"0x10\",\"0b10\",true,false,undefined,\"bad\",NaN,Infinity] as any[]){console.log(isNaN(value));}", "false\nfalse\nfalse\nfalse\nfalse\nfalse\nfalse\ntrue\ntrue\ntrue\nfalse\n"];
        yield return ["ordered-object-coercion", "let trace=\"\";const marker={};const value:any={get [Symbol.toPrimitive](){trace+=\"g\";return(hint:string)=>{trace+=hint;return\"bad\";};}};console.log(isNaN(value),trace);try{isNaN({valueOf(){throw marker;}} as any);}catch(error){console.log(error===marker);}", "true gnumber\ntrue\n"];
        yield return ["symbol-bigint-refusal", "for(const value of [Symbol(\"x\"),1n] as any[]){try{isNaN(value);console.log(\"accepted\");}catch(error){console.log(error instanceof TypeError);}}console.log(Number.isNaN(\"bad\" as any),Number.isNaN(NaN));", "true\ntrue\nfalse true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OrderedProtocolControls(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
