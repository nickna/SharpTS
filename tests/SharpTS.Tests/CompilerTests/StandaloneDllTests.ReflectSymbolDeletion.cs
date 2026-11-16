using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_Issue2160ReflectSymbolDeletion_RejectsNativePrimitiveTargetsBeforeCoercion()
    {
        const string source = """
            import { Environment } from "dotnet:System.Environment";
            const target: any = Environment.currentManagedThreadId;
            const key = Symbol("key");
            try { console.log("direct", Reflect.deleteProperty(target, key)); }
            catch (error) { console.log("direct error", error instanceof TypeError); }
            let reads = 0;
            const coerced: any = {};
            coerced[Symbol.toPrimitive] = function(hint: string) { reads++; return key; };
            try { console.log("coerced", Reflect.deleteProperty(target, coerced)); }
            catch (error) { console.log("coerced error", error instanceof TypeError); }
            console.log("reads", reads);
            """;
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("reflect-delete-native-primitive.dll"),
            "direct error true\ncoerced error true\nreads 0\n");
    }

    [Theory, DeploymentData(nameof(ReflectSymbolDeletionTests.OrdinaryCases), MemberType = typeof(ReflectSymbolDeletionTests))]
    public void Isolated_Issue2160ReflectSymbolDeletion_VerifiesBeforeExecution(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("reflect-symbol-delete-" + name + ".dll"), expected);
    }

    // #2617 remains a compiled-only deletion gap; share the common fixture's eligibility.
    public static IEnumerable<object[]> Issue2160ProxyDeletionCompiledCases()
        => ReflectSymbolDeletionTests.ProxyModeCases()
            .Where(row => (ExecutionMode)row[3] == ExecutionMode.Compiled)
            .Select(row => new object[] { row[0], row[1], row[2] });

    [Theory, DeploymentData(nameof(Issue2160ProxyDeletionCompiledCases))]
    public void Saved_Issue2160ReflectSymbolDeletion_UsesDeclaredProxyRuntime(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("reflect-symbol-delete-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.Contains("Note: output uses features needing the SharpTS runtime (Proxy)",
            compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        var runtime = directory.GetPath("SharpTS.dll");
        Assert.False(File.Exists(runtime));
        // Supply the declared optional Proxy runtime only after complete saved IL verification.
        File.Copy(typeof(TypeChecker).Assembly.Location, runtime);
        Assert.Equal(File.ReadAllBytes(typeof(TypeChecker).Assembly.Location), File.ReadAllBytes(runtime));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
