using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedZlibRuntimeTests
{
    [Fact]
    public void CompressionAndStreamingConsumersPassILVerification()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { gzipSync, gunzipSync, createGzip, crc32 } from 'zlib';
                console.log(gunzipSync(gzipSync('hello')).toString());
                console.log(crc32('hello'));
                const stream = createGzip();
                stream.end('hello');
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Fact]
    public void DisabledFeatureHasNoMetadataAndReportsAccidentalUse()
    {
        var runtime = EmitRuntime(false);
        Assert.Null(runtime.Zlib);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireZlib).Message);
        Assert.DoesNotContain(runtime.RuntimeType.GetMethods(), method => method.Name.StartsWith("Zlib", StringComparison.Ordinal));
    }

    [Fact]
    public void DeclaredHandleCanBeUsedBeforeBodyEmission()
    {
        var runtime = new EmittedRuntime();
        runtime.BeginZlibEmission();
        var zlib = runtime.RequireZlib();
        Assert.False(zlib.IsComplete);
        Assert.Contains("GzipSync", Assert.Throws<InvalidOperationException>(() => zlib.GzipSync).Message);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("zlib_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Runtime");
        var method = type.DefineMethod("GzipSync", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        zlib.GzipSync = method;
        Assert.Same(method, zlib.GzipSync);
        Assert.Throws<InvalidOperationException>(runtime.BeginZlibEmission);
        Assert.Contains("GunzipSync", Assert.Throws<InvalidOperationException>(zlib.CompleteEmission).Message);
        Assert.False(zlib.IsComplete);
        Assert.Throws<ArgumentNullException>(() => zlib.GzipSync = null!);
        Assert.Same(method, zlib.GzipSync);
    }

    [Fact]
    public void EnabledFeatureCompletesAllHandlesAndRejectsFurtherWrites()
    {
        var runtime = EmitRuntime(true);
        var zlib = runtime.RequireZlib();
        Assert.True(zlib.IsComplete);
        Assert.Equal("ZlibGzipSync", zlib.GzipSync.Name);
        Assert.NotNull(zlib.TransformCtor);
        Assert.All(typeof(EmittedZlibRuntime).GetProperties()
            .Where(property => property.PropertyType == typeof(MethodBuilder)),
            property => Assert.NotNull(property.GetValue(zlib)));
        Assert.Throws<InvalidOperationException>(() => zlib.GzipSync = zlib.GzipSync);
        Assert.Throws<InvalidOperationException>(() => zlib.TransformCtor = zlib.TransformCtor);
        Assert.Throws<InvalidOperationException>(zlib.CompleteEmission);
        Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
    }

    private static EmittedRuntime EmitRuntime(bool usesZlib)
    {
        var source = usesZlib ? "import * as zlib from 'zlib';" : "console.log(1);";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"zlib_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        return new RuntimeEmitter(TypeProvider.Runtime).EmitAll(assembly.DefineDynamicModule("main"), features);
    }
}
