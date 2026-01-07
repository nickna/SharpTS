using System.Diagnostics;
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
    /// Verifies that a --ref-asm compiled DLL can be used as a compile-time reference
    /// by another C# project.
    /// </summary>
    [Fact]
    public void RefAsm_CanBeUsedAsCompileTimeReference()
    {
        // Compile TypeScript with exported class
        var source = """
            export class Calculator {
                add(a: number, b: number): number {
                    return a + b;
                }
            }

            const calc = new Calculator();
            console.log(calc.add(2, 3));
            """;

        var (tempDir, dllPath) = TestHarness.CompileWithRefAsm(source);
        try
        {
            // Create a C# project that references the compiled DLL
            var consumerDir = Path.Combine(tempDir, "Consumer");
            Directory.CreateDirectory(consumerDir);

            // Write a C# project file
            var csprojContent = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="test">
                      <HintPath>{dllPath}</HintPath>
                    </Reference>
                  </ItemGroup>
                </Project>
                """;
            File.WriteAllText(Path.Combine(consumerDir, "Consumer.csproj"), csprojContent);

            // Write minimal C# code that just references the assembly
            // (We can't actually use the types easily since they're dynamically generated,
            // but the build succeeding proves the reference is valid)
            var csContent = """
                // This project references the SharpTS-compiled assembly.
                // The fact that this builds proves the assembly has proper SDK references.
                Console.WriteLine("Consumer builds successfully!");
                """;
            File.WriteAllText(Path.Combine(consumerDir, "Program.cs"), csContent);

            // Try to build the consumer project
            var psi = new ProcessStartInfo("dotnet", "build --no-restore")
            {
                WorkingDirectory = consumerDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            // First restore
            var restorePsi = new ProcessStartInfo("dotnet", "restore")
            {
                WorkingDirectory = consumerDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using (var restoreProcess = Process.Start(restorePsi)!)
            {
                restoreProcess.WaitForExit();
            }

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // The build should succeed (exit code 0)
            // If it fails with "System.Private.CoreLib not found", the ref-asm rewriting didn't work
            Assert.True(process.ExitCode == 0,
                $"Consumer project failed to build. This likely means the DLL still references System.Private.CoreLib.\nOutput: {output}\nError: {error}");
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
