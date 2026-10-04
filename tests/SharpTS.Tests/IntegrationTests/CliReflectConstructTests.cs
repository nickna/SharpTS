using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliReflectConstructTests
{
    [Theory]
    [MemberData(nameof(SharedTests.TypedReflectConstructTests.Cases), MemberType = typeof(SharedTests.TypedReflectConstructTests))]
    public void LoadedDeclarationsAcceptTypedClassTargets(string name, string source, string expected)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile(name + ".ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Theory]
    [InlineData("class Point {} const broad: Function = Point; const callable: (...args:any[])=>any = Point;")]
    [InlineData("Reflect.construct({}, []);")]
    [InlineData("Reflect.construct(1, []);")]
    [InlineData("class Point {} Reflect.construct(new Point(), []);")]
    public void OrdinaryCallSignaturesAndNonFunctionTargetsRemainRejected(string source)
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
