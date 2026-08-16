using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for the --ref-asm flag functionality.
/// Verifies that compiled assemblies reference System.Runtime (not System.Private.CoreLib)
/// and can be used as compile-time references by other C# projects.
/// </summary>
public class ReferenceAssemblyTests
{
    /// <summary>
    /// #738: a function expression invoked as a value must not shift its arguments under --ref-asm.
    /// The reference-assembly rewrite strips parameter names, which used to break the runtime
    /// <c>params[0].Name == "__this"</c> receiver-slot detection — so a value-call mapped the first
    /// real argument onto the synthetic <c>__this</c> slot, shifting everything by one (e.g.
    /// <c>f(4)</c> returned the default <c>3</c> instead of <c>7</c>). The <c>$ExpectsThis</c> marker
    /// attribute survives the rewrite and restores correct detection.
    /// </summary>
    [Fact]
    public void RefAsm_FunctionExpressionValueCall_DoesNotShiftArguments()
    {
        var source = """
            const f = function (x: number, y: number = 3) { return x + y; };
            console.log(f(4));
            console.log(f(4, 10));
            """;

        var (tempDir, dllPath) = TestHarness.CompileWithRefAsm(source);
        try
        {
            var output = TestHarness.ExecuteCompiledDll(dllPath);
            Assert.Equal("7\n14\n", output.Replace("\r\n", "\n"));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Verifies that an async function compiled with --ref-asm references System.Runtime.
    /// </summary>
    [Fact]
    public void RefAsm_AsyncFunction_ReferencesSystemRuntime()
    {
        var source = """
            async function getData(): Promise<string> {
                return "hello";
            }

            async function main() {
                const result = await getData();
                console.log(result);
            }

            main();
            """;

        var (tempDir, dllPath) = TestHarness.CompileWithRefAsm(source);
        try
        {
            var refs = GetAssemblyReferences(dllPath);
            Assert.Contains(refs, r => r == "System.Runtime");
            Assert.DoesNotContain(refs, r => r == "System.Private.CoreLib");
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Verifies that a generator function compiled with --ref-asm references System.Runtime.
    /// </summary>
    [Fact]
    public void RefAsm_Generator_ReferencesSystemRuntime()
    {
        var source = """
            function* generateNumbers(): Generator<number> {
                yield 1;
                yield 2;
                yield 3;
            }

            for (const n of generateNumbers()) {
                console.log(n);
            }
            """;

        var (tempDir, dllPath) = TestHarness.CompileWithRefAsm(source);
        try
        {
            var refs = GetAssemblyReferences(dllPath);
            Assert.Contains(refs, r => r == "System.Runtime");
            Assert.DoesNotContain(refs, r => r == "System.Private.CoreLib");
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Verifies that combined async+generator code compiled with --ref-asm references System.Runtime.
    /// </summary>
    [Fact]
    public void RefAsm_AsyncGenerator_ReferencesSystemRuntime()
    {
        var source = """
            async function delay(ms: number): Promise<void> {
                return;
            }

            function* range(start: number, end: number): Generator<number> {
                for (let i = start; i < end; i++) {
                    yield i;
                }
            }

            async function main() {
                for (const n of range(1, 4)) {
                    await delay(0);
                    console.log(n);
                }
            }

            main();
            """;

        var (tempDir, dllPath) = TestHarness.CompileWithRefAsm(source);
        try
        {
            var refs = GetAssemblyReferences(dllPath);
            Assert.Contains(refs, r => r == "System.Runtime");
            Assert.DoesNotContain(refs, r => r == "System.Private.CoreLib");
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Verifies that --ref-asm compiled code still executes correctly at runtime.
    /// </summary>
    [Fact]
    public void RefAsm_RuntimeExecution_Works()
    {
        var source = """
            async function getData(): Promise<string> {
                return "async works";
            }

            function* genNumbers(): Generator<number> {
                yield 1;
                yield 2;
            }

            async function main() {
                const data = await getData();
                console.log(data);

                for (const n of genNumbers()) {
                    console.log(n);
                }
            }

            main();
            """;

        var (tempDir, dllPath) = TestHarness.CompileWithRefAsm(source);
        try
        {
            var output = TestHarness.ExecuteCompiledDll(dllPath);
            Assert.Equal("async works\n1\n2\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Helper to get assembly reference names from a PE file.
    /// </summary>
    private static List<string> GetAssemblyReferences(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();

        var refs = new List<string>();
        foreach (var refHandle in metadataReader.AssemblyReferences)
        {
            var reference = metadataReader.GetAssemblyReference(refHandle);
            var name = metadataReader.GetString(reference.Name);
            refs.Add(name);
        }
        return refs;
    }

    private static void CleanupTempDir(string tempDir)
    {
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
