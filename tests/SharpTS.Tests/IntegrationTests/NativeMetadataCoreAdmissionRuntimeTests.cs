using SharpTS.Compilation;
using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class NativeMetadataCoreAdmissionRuntimeTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");
    private const string Header = "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { static Integer(v:number):number; static Single(v:number):number; static Long(v:number):number; static Boolean(v:boolean):boolean; static Number(v:number):number; static Integers(v:number[]):number[]; static Jagged(v:number[][]):number[][]; static Booleans(v:boolean[]):boolean[]; static ObjectValue():any; } ";

    private const string Body = "console.log(Api.Integer(7),Api.Single(1.25),Api.Long(9),Api.Boolean(false),Api.Number(3.5)); console.log(Api.Integers([1,2]).join(','),Api.Jagged([[3],[4,5]])[1].join(','),Api.Booleans([true,false]).join(','));";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactScalarAndRecursiveArrayResultsExportAndVerifyInBothPipelines(bool modules)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", modules ? "import './lib';" : Header + Body);
        if (modules) directory.CreateFile("lib.ts", Header + Body);
        var compiled = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --emit-native-project native --runtime {NativeAotProjectOptions.HostRuntimeIdentifier} --verify -r \"{Fixture}\"", directory.Path);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("IL verification passed", compiled.StandardOutput);
        Assert.True(File.Exists(directory.GetPath("native/guest/Guest.dll")));
    }

    [Fact]
    public void ExportedCallableWithTheCompleteScalarArrayBodyRetainsExceptionalEffectsRefusal()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "import {run} from './lib'; run();");
        directory.CreateFile("lib.ts", Header + "export function run(){" + Body + "}");
        var compiled = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --emit-native-project native --runtime {NativeAotProjectOptions.HostRuntimeIdentifier} --verify -r \"{Fixture}\"", directory.Path);
        Assert.NotEqual(0, compiled.ExitCode);
        Assert.Contains("exported source callable carries CLR exceptional effects", compiled.StandardOutput + compiled.StandardError);
        Assert.False(File.Exists(directory.GetPath("native/guest/Guest.dll")));
    }

    [Theory]
    [InlineData("console.log(Api.ObjectValue());")]
    [InlineData("const values:any[]=[Api.ObjectValue()]; console.log(values);")]
    [InlineData("function escape():any{return Api.ObjectValue();} console.log(escape());")]
    public void ObjectAndHeapEscapesRemainNamedRefusals(string body)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", Header + body);
        var compiled = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --emit-native-project native --runtime {NativeAotProjectOptions.HostRuntimeIdentifier} --verify -r \"{Fixture}\"", directory.Path);
        Assert.NotEqual(0, compiled.ExitCode);
        Assert.Contains("native guest CLR admission refused", compiled.StandardOutput + compiled.StandardError);
        Assert.False(File.Exists(directory.GetPath("native/guest/Guest.dll")));
    }
}
