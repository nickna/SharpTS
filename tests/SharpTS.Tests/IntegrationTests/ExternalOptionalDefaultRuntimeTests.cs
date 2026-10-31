using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class ExternalOptionalDefaultRuntimeTests
{
    public static IEnumerable<object[]> Controls()
    {
        yield return ["string-default", "static OptionalString(value?:string):string;", "console.log(Api.OptionalString(),Api.OptionalString(\"explicit\"));", "fallback explicit\n"];
        yield return ["boolean-default", "static OptionalBoolean(value?:boolean):boolean;", "console.log(Api.OptionalBoolean());", "true\n"];
        yield return ["integer-default", "static OptionalInteger(value?:number):number;", "console.log(Api.OptionalInteger());", "7\n"];
        yield return ["number-default", "static OptionalNumber(value?:number):number;", "console.log(Api.OptionalNumber());", "3.5\n"];
        yield return ["enum-default", "static OptionalEnum(value?:any):number;", "console.log(Api.OptionalEnum());", "9\n"];
        yield return ["null-default", "static OptionalNull(value?:any):string;", "console.log(Api.OptionalNull());", "null\n"];
        yield return ["constructor-default", "constructor(value?:string); Value:string;", "console.log(new Api().Value);", "constructor-default\n"];
    }

    [Theory]
    [MemberData(nameof(Controls))]
    public void ExternalDefaultsVerifyAndExecute(string control, string declaration, string body, string expected)
    {
        new ExternalSignatureRuntimeTests().OrdinaryExplicitReferenceCallsVerifyAndExecute(control,
            "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { " + declaration + " } " + body, expected);
    }
    [Fact]
    public void MissingConstantRetainsTheExistingNamedOverloadRefusal()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { static OptionalMissing(value?:any):string; } console.log(Api.OptionalMissing());");
        string fixture = Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");
        var compiled = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --ref-asm --verify -o out.dll -r \"{fixture}\"", directory.Path);
        Assert.NotEqual(0, compiled.ExitCode);
        Assert.Contains("No compatible overload found for OptionalMissing", compiled.StandardOutput + compiled.StandardError);
    }
}
