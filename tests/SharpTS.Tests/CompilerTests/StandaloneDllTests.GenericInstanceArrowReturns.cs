using SharpTS.Tests.Infrastructure;
using System.Reflection;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(GenericInstanceArrowReturnTests.Cases),
        MemberType = typeof(GenericInstanceArrowReturnTests))]
    public void Isolated_Issue2266GenericInstanceArrowReturns_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("generic-instance-arrow-return-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));

        if (name == "return-controls")
        {
            var assembly = Assembly.Load(File.ReadAllBytes(output));
            var holder = Assert.Single(assembly.GetTypes(), type => type.Name == "Holder");
            Assert.True(holder.IsGenericTypeDefinition);
            var getter = Assert.IsAssignableFrom<MethodInfo>(holder.GetMethod("get_Value"));
            Assert.True(getter.ReturnType.IsGenericParameter);
            Assert.Equal(holder, getter.ReturnType.DeclaringType);
        }

        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
