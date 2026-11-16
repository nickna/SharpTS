using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(RawCallStorageValueTests.Cases), MemberType = typeof(RawCallStorageValueTests))]
    public void Isolated_Issue2531RawCallStorage_VerifyBeforeExecution(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("raw-call-storage-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        if (name == "virtual-parameter-slots")
        {
            var assembly = System.Reflection.Assembly.Load(File.ReadAllBytes(output));
            foreach (string owner in new[] { "NumberBase", "NumberChild", "BooleanBase", "BooleanChild", "GenericBase", "GenericChild" })
            {
                var type = assembly.GetType(owner, throwOnError: true)!;
                if (owner.StartsWith("Generic", StringComparison.Ordinal))
                {
                    Assert.True(type.IsGenericTypeDefinition);
                    Assert.Single(type.GetGenericArguments());
                }
                var method = type.GetMethod("run",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.DeclaredOnly)!;
                Assert.Equal(typeof(object), Assert.Single(method.GetParameters()).ParameterType);
            }
            Assert.Equal(typeof(double), Assert.Single(assembly.GetType("PrimitiveControl", throwOnError: true)!
                .GetMethod("run")!.GetParameters()).ParameterType);
        }
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
