using System.Text.Json;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EvalPrimitiveThrowTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (value, expected) in new (string, string)[]
        {
            ("7", "number 7"),
            ("'sentinel'", "string sentinel"),
            ("'TypeError:sentinel'", "string TypeError:sentinel"),
            ("true", "boolean true"),
            ("false", "boolean false"),
            ("null", "object null"),
            ("undefined", "undefined undefined"),
            ("9n", "bigint 9"),
        })
            foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }) yield return [value, expected, mode];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DynamicEvalKeepsThePrimitiveThrownValue(string value, string expected, ExecutionMode mode)
    {
        string source = "try{eval(" + JsonSerializer.Serialize("do{throw " + value + ";}while(false)") + ");}catch(error:any){console.log(typeof error,String(error));}";
        Assert.Equal(expected + "\n", TestHarness.Run(source, mode));
    }

    internal const string DeploymentSource = "try{eval('do{throw 7;}while(false)');}catch(error:any){console.log(error);}try{eval('do{throw undefined;}while(false)');}catch(error:any){console.log(typeof error);}try{eval('do{throw null;}while(false)');}catch(error:any){console.log(error===null);}";

    [Theory, ModeData]
    public void TypedRuntimeErrorsKeepTheirExistingErrorTranslation(ExecutionMode mode)
        => Assert.Equal("true TypeError\n", TestHarness.Run("try{eval('do{throw new TypeError(\"sentinel\");}while(false)');}catch(error:any){console.log(error instanceof TypeError,error.name);}", mode));

    [Fact]
    public void PrimitiveThrowBridgeProducesValidIl()
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(DeploymentSource));
}
