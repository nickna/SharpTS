using System.Diagnostics;
using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class ExternalSignatureRuntimeTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");
    public static IEnumerable<object[]> Controls()
    {
        yield return ["static-string", "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { static Echo(value:string):string; } console.log(Api.Echo(\"ok\"));", "ok\n"];
        yield return ["void-followed-by-output", "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { static Nothing():void; } Api.Nothing(); console.log(\"after\");", "after\n"];
        yield return ["reference-object-result", "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { static ObjectValue():any; } const value=Api.ObjectValue(); console.log(typeof value, value+1);", "number 43\n"];
        yield return ["ctor-field-property", "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { constructor(value:string); Value:string; Property:string; InstanceEcho(value:string):string; } const value=new Api(\"first\"); console.log(value.Value, value.Property, value.InstanceEcho(\"echo\")); value.Value=\"field\"; value.Property=\"property\"; console.log(value.Value,value.Property);", "first first echo\nfield property\n"];
        yield return ["array-return", "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { static ArrayValue():string[]; } console.log(Api.ArrayValue().join(\"|\"));", "a|b\n"];
        yield return ["out-void-tuple", "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { static OutString():[string]; } console.log(Api.OutString()[0]);", "out\n"];
        yield return ["closed-owner", "@DotNetType(\"ExternalSignatureFixture.Box<string>\") declare class Box { constructor(value:string); Value:string; Property:string; Echo(value:string):string; } const value=new Box(\"box\"); console.log(value.Value,value.Property,value.Echo(\"echo\"));", "box box echo\n"];
    }
    [Theory]
    [MemberData(nameof(Controls))]
    public void OrdinaryExplicitReferenceCallsVerifyAndExecute(string control, string source, string expected)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", source);
        var compiled = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --ref-asm --verify -o out.dll -r \"{Fixture}\"", directory.Path);
        Assert.True(compiled.ExitCode == 0, control + ": " + compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("IL verification passed", compiled.StandardOutput);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory.Path,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(directory.GetPath("out.dll"));
        var run = TestProcess.Run(start, TimeSpan.FromSeconds(30), "External signature CLR execution: " + control);
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Equal(expected, run.StandardOutput.Replace("\r\n", "\n"));
    }
}
