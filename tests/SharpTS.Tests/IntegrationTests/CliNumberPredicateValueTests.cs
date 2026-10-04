using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliNumberPredicateValueTests
{
    [Theory]
    [MemberData(nameof(SharedTests.NumberPredicateValueTests.LoadedLibraryCases), MemberType = typeof(SharedTests.NumberPredicateValueTests))]
    public void LoadedMergedNumberConstructorIncludesPredicateValues(string name, string source, string expected)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile(name + ".ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
    }

    [Theory]
    [InlineData("const invalid=Number.isImaginary;")]
    [InlineData("const N=Number; const invalid=N.isImaginary;")]
    [InlineData("const invalid:number=Number.isFinite;")]
    [InlineData("const invalid:(value:unknown)=>number=Number.isNaN;")]
    public void InvalidNumberMemberValuesAndSignaturesRemainRejected(string source)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("rejected.dll");
        var result = CliTestHelper.RunCli($"--no-tsconfig --compile \"{path}\" -o \"{output}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Type Error", result.StandardOutput + result.StandardError);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Es5LibrarySelectionStillRejectsEs2015PredicateValues()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", "const predicate=Number.isNaN;");
        var output = directory.GetPath("rejected.dll");
        var result = CliTestHelper.RunCli($"--no-tsconfig --lib es5 --compile \"{path}\" -o \"{output}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Property 'isNaN' does not exist on type 'NumberConstructor'", result.StandardOutput + result.StandardError);
        Assert.False(File.Exists(output));
    }
}
