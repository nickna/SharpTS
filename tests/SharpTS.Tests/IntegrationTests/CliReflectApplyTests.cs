using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliReflectApplyTests
{
    [Theory]
    [MemberData(nameof(SharedTests.TypedReflectApplyTests.Cases), MemberType = typeof(SharedTests.TypedReflectApplyTests))]
    public void LoadedDeclarationsPreserveGenericApplyResults(string name, string source, string expected)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile(name + ".ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Theory]
    [InlineData("function label(a:number,b:number):string{return 'ok';} const args:[number,number]=[2,3]; const invalid:number=Reflect.apply(label,undefined,args);")]
    [InlineData("function label(a:number,b:number):string{return 'ok';} const args:[number,number]=[2,3]; console.log(Reflect.apply(label,undefined,args) as number);")]
    [InlineData("function label(a:number,b:number):string{return 'ok';} const args:readonly [number,number]=[2,3]; const invalid:number=Reflect.apply(label,undefined,args);")]
    [InlineData("function label(a:number,b?:number):string{return 'ok';} const args:[number]=[2]; const invalid:number=Reflect.apply(label,undefined,args);")]
    [InlineData("function label(a:number,...rest:number[]):string{return 'ok';} const args:[number,number]=[2,3]; const invalid:number=Reflect.apply(label,undefined,args);")]
    [InlineData("function label(...pair:[number,number]):string{return 'ok';} const args:[number,number]=[2,3]; const invalid:number=Reflect.apply(label,undefined,args);")]
    [InlineData("Reflect.apply({},undefined,[]);")]
    [InlineData("Reflect.apply(1,undefined,[]);")]
    [InlineData("function sum(a:number):number{return a;} Reflect.apply<undefined,number,number>(sum,undefined,1);")]
    public void InvalidTypedResultsTargetsAndTypeArgumentsRemainRejected(string source)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("rejected.dll");
        var result = CliTestHelper.RunCli($"--no-tsconfig --compile \"{path}\" -o \"{output}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Type Error", result.StandardOutput + result.StandardError);
        Assert.False(File.Exists(output));
    }
}
