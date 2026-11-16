using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(OptionalConstructorParameterValueTests.Cases), MemberType = typeof(OptionalConstructorParameterValueTests))]
    public void Isolated_Issue2532OptionalConstructorParameters_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("optional-constructor-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        if (name == "original-optional-string")
        {
            var assembly = System.Reflection.Assembly.Load(File.ReadAllBytes(output));
            var constructor = assembly.GetType("C", throwOnError: true)!.GetConstructor([typeof(object)]);
            Assert.NotNull(constructor);
            Assert.Equal(typeof(object), Assert.Single(constructor.GetParameters()).ParameterType);
        }
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
