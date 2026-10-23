using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(UndefinedClassFieldValueTests.Cases), MemberType = typeof(UndefinedClassFieldValueTests))]
    public void Isolated_Issue2555UndefinedClassFields_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("undefined-class-field-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        if (name is "original-string-field" or "null-only-typed-control")
        {
            var assembly = System.Reflection.Assembly.Load(File.ReadAllBytes(output));
            var fieldType = assembly.GetType("Field", throwOnError: true)!;
            Type expectedType = name == "original-string-field" ? typeof(object) : typeof(string);
            Assert.Equal(expectedType, fieldType.GetProperty("Value")!.PropertyType);
            Assert.Equal(expectedType, fieldType.GetField("__Value",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.FieldType);
        }
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
