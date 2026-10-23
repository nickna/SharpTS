using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using SharpTS.Parsing;
using SharpTS.Testing;
using SharpTS.Tests.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ModuleVarHeadScopeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["for-of-postloop-closure", """
            for (var item of [3, 4]) {}
            const read = () => item;
            console.log(read());
            export {};
            """ + "\n", "4\n"];
        yield return ["for-in-postloop-closure", """
            for (var key in { first: 1, last: 2 }) {}
            const read = () => key;
            console.log(read());
            export {};
            """ + "\n", "last\n"];
        yield return ["predeclared-assignment-control", """
            let item = 0;
            for (item of [3, 4]) {}
            const read = () => item;
            console.log(read());
            export {};
            """ + "\n", "4\n"];
        var fullModule = ClassicGlobalLoopVarBindingTests.ModuleCases()
            .Single(row => (string)row[0] == "module-var-head-slots-closures-and-empty-loop-nonpublication");
        yield return [fullModule[0], (string)fullModule[1] + "\r\n", fullModule[2]];
        yield return ["function-owner-and-lexical-loop-controls", """
            const item = "outer-item", key = "outer-key";
            function local(seed: number) {
                const readItem = () => item, readKey = () => key;
                {
                    for (var item of [seed, seed + 1]) {}
                }
                for (var key in { first: 1, last: 2 }) {}
                console.log("function", readItem(), readKey());
                var item: number;
                const reportSame = () => console.log("same", readItem() === item, readKey() === key);
                reportSame();
            }
            local(7);
            for (let item of [1, 2]) {
                if (item === 2) console.log("lexical-of", item);
            }
            for (const key in { only: 1 }) console.log("lexical-in", key);
            console.log("outer", item, key);
            export {};
            """ + "\n", "function 8 last\nsame true true\nlexical-of 2\nlexical-in only\nouter outer-item outer-key\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DeclarationHeadVarsRemainVisibleInTheirModuleOwner(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var output = mode == ExecutionMode.Interpreted
            ? TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source },
                "main.ts", ExecutionMode.Interpreted)
            : CompileModuleVerifyAndRun(source);
        Assert.Equal(expected, output);
    }

    private static string CompileModuleVerifyAndRun(string source)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var assemblyPath = directory.GetPath("module-var-head-scope.dll");
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

    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["loop-head-types-remain-precise-rejected", """
            for (var item of [1]) {}
            for (var key in { last: 1 }) {}
            const readItem = () => item, readKey = () => key;
            const wrongItem: string = readItem();
            const wrongKey: number = readKey();
            export {};
            """ + "\n", new string[] { "TS2322", "TS2322" }, new int[] { 4, 5 }];
        yield return ["lexical-head-names-stay-local-rejected-v2", """
            for (let hiddenItem of [1]) {}
            for (const hiddenKey in { last: 1 }) {}
            console.log(hiddenItem);
            console.log(hiddenKey);
            export {};
            """ + "\n", new string[] { "TS2304", "TS2304" }, new int[] { 3, 4 }];
        yield return ["empty-var-head-loop-does-not-definitely-assign-rejected-v2", """
            for (var item of [] as number[]) {}
            console.log(item);
            export {};
            """ + "\n", new string[] { "TS2454" }, new int[] { 2 }];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void LoopHeadsKeepTheirTypesLexicalScopeAndDefiniteAssignment(
        string name, string source, string[] expectedCodes, int[] expectedLines)
    {
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(statements, document).Diagnostics;
        var expected = expectedCodes.Zip(expectedLines, (code, line) => (Code: code, Line: line))
            .OrderBy(item => item.Line).ThenBy(item => item.Code).ToArray();
        var actual = diagnostics.Select(diagnostic => (Code: Assert.IsType<string>(diagnostic.TsCode), Line: diagnostic.Line))
            .OrderBy(item => item.Line).ThenBy(item => item.Code).ToArray();
        Assert.Equal(expected, actual);
    }
}
