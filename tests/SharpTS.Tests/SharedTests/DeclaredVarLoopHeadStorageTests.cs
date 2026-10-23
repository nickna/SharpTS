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
public sealed class DeclaredVarLoopHeadStorageTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["declared-var-for-of-postloop-capture", """
            var item = 0;
            for (var item of [3, 4]) {}
            const read = () => item;
            console.log(item, read());
            export {};
            """ + "\n", "4 4\n"];
        yield return ["declared-var-for-in-postloop-capture", """
            var key = "outer";
            for (var key in { first: 1, last: 2 }) {}
            const read = () => key;
            console.log(key, read());
            export {};
            """ + "\n", "last last\n"];
        yield return ["function-var-heads-share-captured-binding", """
            function run() {
                var item = 0;
                var key = "outer";
                let first = () => item, second = () => item;
                let firstKey = () => key, secondKey = () => key;
                for (var item of [3, 4]) {
                    if (item === 3) first = () => item;
                    else second = () => item;
                }
                for (var key in { first: 1, last: 2 }) {
                    if (key === "first") firstKey = () => key;
                    else secondKey = () => key;
                }
                console.log(item, first(), second(), key, firstKey(), secondKey());
            }
            run();
            export {};
            """ + "\n", "4 4 4 last last last\n"];
        yield return ["numeric-map-var-head-shares-existing-binding", """
            function run() {
                const values = new Map<number, number>();
                values.set(1, 2);
                var entry: [number, number] = [0, 0];
                for (var entry of values) {
                    console.log(entry[0], entry[1]);
                }
                console.log(entry[0], entry[1]);
            }
            run();
            export {};
            """ + "\n", "1 2\n1 2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DeclarationHeadsUpdateTheExistingVarBinding(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var actual = mode == ExecutionMode.Interpreted
            ? TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source },
                "main.ts", ExecutionMode.Interpreted)
            : CompileModuleVerifyAndRun(source);
        Assert.Equal(expected, actual);
    }
    private static string CompileModuleVerifyAndRun(string source)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var assemblyPath = directory.GetPath("declared-var-loop-head-storage.dll");
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
