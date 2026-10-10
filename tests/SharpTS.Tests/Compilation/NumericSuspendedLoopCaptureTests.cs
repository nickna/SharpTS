using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Numeric closure snapshots must match their field representation after a real
/// suspension. A proven numeric Promise callback can have a double capture field
/// even when its source is stored in an object field on the async state machine.
/// </summary>
public sealed class NumericSuspendedLoopCaptureTests
{
    private const string TimerDelaySource =
        "const delay = () => new Promise<number>((resolve): void => { setTimeout(() => resolve(0), 1); });\n";

    [Theory]
    [InlineData(true, "for")]
    [InlineData(false, "for")]
    [InlineData(true, "for-of")]
    [InlineData(false, "for-of")]
    public void AsyncFunction_ReadOnlyNumericSnapshotsAfterTimerSuspension(
        bool emitDebugSymbols, string loopKind)
    {
        string source = LoopSource(asyncGenerator: false, loopKind);
        const string expected = "result 3 7\n";

        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, CompileVerifyAndRun(source, emitDebugSymbols));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AsyncFunction_MixedNumericAndStringSnapshotsKeepEachCaptureRepresentation(
        bool emitDebugSymbols)
    {
        string source = TimerDelaySource + """
            async function work(limit: number): Promise<void> {
                let item: number = 7;
                await delay();
                let chain: Promise<number> = Promise.resolve(0);
                {
                    for (let item: number = 0; item < limit; item++) {
                        const label = "row" + item;
                        chain = chain.then((sum: number): number => {
                            console.log("capture", item, label);
                            return sum + item;
                        });
                    }
                }
                const sum = await chain;
                console.log("result", sum, item);
            }
            work(3);
            """;
        const string expected = "capture 0 row0\ncapture 1 row1\ncapture 2 row2\nresult 3 7\n";

        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, CompileVerifyAndRun(source, emitDebugSymbols));
    }

    [Theory]
    [InlineData(true, "for")]
    [InlineData(false, "for")]
    [InlineData(true, "for-of")]
    [InlineData(false, "for-of")]
    public void AsyncGenerator_ReadOnlyNumericSnapshotsAfterTimerSuspension(
        bool emitDebugSymbols, string loopKind)
    {
        // Async generators and for-of loops currently keep object capture fields.
        // These controls exercise the same capture-population hook without numeric promotion.
        string source = LoopSource(asyncGenerator: true, loopKind);
        const string expected = "result 3 7\nyield 3\ndone true\n";

        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, CompileVerifyAndRun(source, emitDebugSymbols));
    }

    private static string LoopSource(bool asyncGenerator, string loopKind)
    {
        // For-of is a nonspecialized representation control; use a distinct binding
        // to keep unrelated same-name iteration lowering outside this regression.
        string loop = loopKind switch
        {
            "for" => "for (let item: number = 0; item < limit; item++)",
            "for-of" => "for (const entry of [0, 1, 2])",
            _ => throw new ArgumentOutOfRangeException(nameof(loopKind)),
        };
        string capturedName = loopKind == "for-of" ? "entry" : "item";
        string declaration = asyncGenerator
            ? "async function* work(limit: number): AsyncGenerator<number>"
            : "async function work(limit: number): Promise<void>";
        string launch = asyncGenerator ? """
            async function main(): Promise<void> {
                const values = work(3);
                const first = await values.next();
                console.log("yield", first.value);
                const second = await values.next();
                console.log("done", second.done);
            }
            main();
            """ : "work(3);";

        return TimerDelaySource + """
            DECLARATION {
                let item: number = 7;
                await delay();
                let chain: Promise<number> = Promise.resolve(0);
                {
                    LOOP {
                        chain = chain.then((sum: number): number => sum + CAPTURE);
                    }
                }
                const sum = await chain;
                console.log("result", sum, item);
                YIELD
            }
            LAUNCH
            """
            .Replace("DECLARATION", declaration, StringComparison.Ordinal)
            .Replace("LOOP", loop, StringComparison.Ordinal)
            .Replace("CAPTURE", capturedName, StringComparison.Ordinal)
            .Replace("YIELD", asyncGenerator ? "yield sum;" : "", StringComparison.Ordinal)
            .Replace("LAUNCH", launch, StringComparison.Ordinal);
    }

    private static string CompileVerifyAndRun(string source, bool emitDebugSymbols)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts_numeric_suspension_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var document = new SourceDocument(Path.Combine(directory, "main.ts"), source);
            var statements = new Parser(new Lexer(source).ScanTokens())
                .WithSourceDocument(document)
                .ParseOrThrow();
            var typeMap = new TypeChecker().Check(statements);
            var deadCode = new DeadCodeAnalyzer(typeMap).Analyze(statements);
            var compiler = new ILCompiler(
                "output", preserveConstEnums: false, useReferenceAssemblies: true,
                sdkPath: SdkResolver.FindReferenceAssembliesPath())
            {
                EmitDebugSymbols = emitDebugSymbols,
            };
            compiler.SetSourceDocument(document);
            compiler.Compile(statements, typeMap, deadCode);
            var artifacts = compiler.SaveArtifacts(emitDebugSymbols ? "output.pdb" : null);
            string assemblyPath = Path.Combine(directory, "output.dll");
            File.WriteAllBytes(assemblyPath, artifacts.Assembly);
            if (artifacts.Pdb is not null)
                File.WriteAllBytes(Path.Combine(directory, "output.pdb"), artifacts.Pdb);

            Assert.DoesNotContain(TestHarness.VerifyIL(assemblyPath),
                error => !error.Contains("Failed to load assembly", StringComparison.Ordinal));

            string runtime = typeof(ILCompiler).Assembly.Location;
            File.Copy(runtime, Path.Combine(directory, "SharpTS.dll"));
            string compression = Path.Combine(Path.GetDirectoryName(runtime)!, "ZstdSharp.dll");
            if (File.Exists(compression))
                File.Copy(compression, Path.Combine(directory, "ZstdSharp.dll"));
            File.WriteAllText(Path.Combine(directory, "output.runtimeconfig.json"), """
                { "runtimeOptions": { "tfm": "net10.0", "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" } } }
                """);
            return TestHarness.ExecuteCompiledDll(assemblyPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
