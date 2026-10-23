using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class IdentifierLoopHeadStorageTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["published-async-assignment-head", """
            async function run() {
                let key: any = "outer";
                const read = () => key;
                for await (key of ["one", "two"]) {}
                console.log(key, read());
            }
            run();
            """ + "\n", "two two\n"];
        var syncCase = ForAwaitIdentifierHeadTests.BindingCases().Single(row => (string)row[0] == "sync");
        yield return ["binding-sync", (string)syncCase[1] + "\r\n", syncCase[2]];
        var asyncCase = ForAwaitIdentifierHeadTests.BindingCases().Single(row => (string)row[0] == "async");
        yield return ["binding-async", (string)asyncCase[1] + "\r\n", asyncCase[2]];
        yield return ["predeclared-assignment-control", """
            let item = 0;
            for (item of [3, 4]) {}
            const read = () => item;
            console.log(read());
            export {};
            """ + "\n", "4\n"];
        yield return ["numeric-map-assignment-head", """
            function run() {
                const values = new Map<number, number>();
                values.set(1, 2);
                let entry: any = [0, 0];
                for (entry of values) {
                    console.log(entry[0], entry[1]);
                }
                console.log(entry[0], entry[1]);
            }
            run();
            """ + "\n", "1 2\n1 2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AssignmentHeadsUpdateTheExistingBinding(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        string actual;
        if (name == "predeclared-assignment-control")
        {
            actual = mode == ExecutionMode.Interpreted
                ? TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source },
                    "main.ts", ExecutionMode.Interpreted)
                : CompileModuleVerifyAndRun(source);
        }
        else if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            actual = output;
        }
        else
        {
            actual = TestHarness.Run(source, ExecutionMode.Interpreted);
        }
        Assert.Equal(expected, actual);
    }
    private static string CompileModuleVerifyAndRun(string source)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var assemblyPath = directory.GetPath("identifier-loop-head-storage.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{sourcePath}\" -o \"{assemblyPath}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Empty(compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("omitted", compile.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("skipped", compile.StandardOutput, StringComparison.OrdinalIgnoreCase);
        using (var stream = File.OpenRead(assemblyPath))
        using (var image = new PEReader(stream))
        {
            var metadata = image.GetMetadataReader();
            var references = metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
            Assert.NotEmpty(references);
            Assert.All(references, reference => Assert.StartsWith("System.", reference));
        }
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        var verifiedHash = SHA256.HashData(File.ReadAllBytes(assemblyPath));
        var (errors, output) = TestHarness.VerifySavedAssemblyAndRun(assemblyPath, () =>
        {
            Assert.Equal(verifiedHash, SHA256.HashData(File.ReadAllBytes(assemblyPath)));
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory.Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add(assemblyPath);
            var run = TestProcess.Run(start, TimeSpan.FromSeconds(20));
            Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
            Assert.Empty(run.StandardError);
            return run.StandardOutput.Replace("\r\n", "\n");
        });
        Assert.Empty(errors);
        Assert.Equal(verifiedHash, SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        return output;
    }

}
