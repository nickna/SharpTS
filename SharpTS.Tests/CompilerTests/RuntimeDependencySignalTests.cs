using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Verifies the compile-time signal that drives co-locating SharpTS.dll with compiled output:
/// <see cref="ILCompiler.RequiredSharpTSRuntimeReasons"/>. A program that uses none of the
/// SharpTS-runtime-backed features must stay fully standalone (empty reasons); programs that use
/// eval/Proxy/Intl/etc. must report the corresponding reason so the build copies the runtime.
/// </summary>
public class RuntimeDependencySignalTests
{
    private static IReadOnlyCollection<string> ReasonsFor(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        var statements = new Parser(tokens).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);

        var compiler = new ILCompiler("runtime_signal_test");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return compiler.RequiredSharpTSRuntimeReasons;
    }

    [Fact]
    public void TrivialProgram_RequiresNoRuntime()
    {
        var reasons = ReasonsFor("""
            const xs = [1, 2, 3].map(x => x * 2);
            console.log(xs.join(","));
            """);
        Assert.Empty(reasons);
    }

    [Fact]
    public void Eval_RequiresRuntime()
    {
        var reasons = ReasonsFor("""console.log(eval("1 + 2"));""");
        Assert.Contains("eval()", reasons);
    }

    [Fact]
    public void Proxy_RequiresRuntime()
    {
        var reasons = ReasonsFor("""
            const p: any = new Proxy({ a: 1 }, { get: (t, k) => 42 });
            console.log(p.a);
            """);
        Assert.Contains("Proxy", reasons);
    }

    [Fact]
    public void Intl_RequiresRuntime()
    {
        var reasons = ReasonsFor("""
            const nf = new Intl.NumberFormat("en-US");
            console.log(nf.format(1234.5));
            """);
        Assert.Contains("Intl", reasons);
    }

    [Fact]
    public void Eval_DoesNotFalselyReportOtherFeatures()
    {
        var reasons = ReasonsFor("""console.log(eval("1 + 2"));""");
        Assert.DoesNotContain("Proxy", reasons);
        Assert.DoesNotContain("Intl", reasons);
        Assert.DoesNotContain("vm module", reasons);
    }

    [Fact]
    public void AbortSignalAny_RequiresRuntime()
    {
        var reasons = ReasonsFor("""
            const c1 = new AbortController();
            const c2 = new AbortController();
            const combined = AbortSignal.any([c1.signal, c2.signal]);
            console.log(combined.aborted);
            """);
        Assert.Contains("AbortSignal.any", reasons);
    }

    [Fact]
    public void AbortControllerWithoutAny_StaysStandalone()
    {
        // Plain AbortController usage (incl. fetch-with-signal) compiles to pure IL —
        // it must NOT drag in a SharpTS.dll copy (#116).
        var reasons = ReasonsFor("""
            const c = new AbortController();
            c.signal.addEventListener("abort", () => console.log("aborted"));
            c.abort();
            console.log(c.signal.aborted);
            """);
        Assert.DoesNotContain("AbortSignal.any", reasons);
        Assert.Empty(reasons);
    }

    [Fact]
    public void SourceExecution_RequiresManagedHostAndFullDependencyClosure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_execution_dep_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var entryPath = Path.Combine(tempDir, "main.ts");
            File.WriteAllText(entryPath, """
                import { runSourceJson } from "sharpts:execution";
                console.log(runSourceJson("console.log(1);", "interpret", 1024));
                """);

            var resolver = new ModuleResolver(entryPath);
            var entryModule = resolver.LoadModule(entryPath);
            var modules = resolver.GetModulesInOrder(entryModule);
            var typeMap = TestHarness.CheckModulesOrThrow(new TypeChecker(), modules, resolver);
            var statements = modules.SelectMany(module => module.Statements).ToList();
            var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);

            var compiler = new ILCompiler("source_execution_dependency_test");
            compiler.CompileModules(modules, resolver, typeMap, deadCodeInfo);

            Assert.Contains("sharpts:execution module", compiler.RequiredSharpTSRuntimeReasons);
            Assert.True(compiler.RequiredSharpTSRuntimeRequirements.HasFlag(
                SharpTSRuntimeRequirements.RuntimeAssembly));
            Assert.True(compiler.RequiredSharpTSRuntimeRequirements.HasFlag(
                SharpTSRuntimeRequirements.FullDependencyClosure));
            Assert.True(compiler.RequiredSharpTSRuntimeRequirements.HasFlag(
                SharpTSRuntimeRequirements.ManagedCompilerHost));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
