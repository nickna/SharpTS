using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedZlibRuntimeTests
{
    [Theory]
    [InlineData(nameof(EmittedZlibRuntime.TransformCtor))]
    [InlineData(nameof(EmittedZlibRuntime.GzipSync))]
    [InlineData(nameof(EmittedZlibRuntime.GunzipSync))]
    [InlineData(nameof(EmittedZlibRuntime.DeflateSync))]
    [InlineData(nameof(EmittedZlibRuntime.InflateSync))]
    [InlineData(nameof(EmittedZlibRuntime.DeflateRawSync))]
    [InlineData(nameof(EmittedZlibRuntime.InflateRawSync))]
    [InlineData(nameof(EmittedZlibRuntime.BrotliCompressSync))]
    [InlineData(nameof(EmittedZlibRuntime.BrotliDecompressSync))]
    [InlineData(nameof(EmittedZlibRuntime.ZstdCompressSync))]
    [InlineData(nameof(EmittedZlibRuntime.ZstdDecompressSync))]
    [InlineData(nameof(EmittedZlibRuntime.UnzipSync))]
    [InlineData(nameof(EmittedZlibRuntime.Crc32))]
    [InlineData(nameof(EmittedZlibRuntime.CreateGzip))]
    [InlineData(nameof(EmittedZlibRuntime.CreateGunzip))]
    [InlineData(nameof(EmittedZlibRuntime.CreateDeflate))]
    [InlineData(nameof(EmittedZlibRuntime.CreateInflate))]
    [InlineData(nameof(EmittedZlibRuntime.CreateDeflateRaw))]
    [InlineData(nameof(EmittedZlibRuntime.CreateInflateRaw))]
    [InlineData(nameof(EmittedZlibRuntime.CreateBrotliCompress))]
    [InlineData(nameof(EmittedZlibRuntime.CreateBrotliDecompress))]
    [InlineData(nameof(EmittedZlibRuntime.CreateZstdCompress))]
    [InlineData(nameof(EmittedZlibRuntime.CreateZstdDecompress))]
    [InlineData(nameof(EmittedZlibRuntime.CreateUnzip))]
    public void EveryDeclarationRejectsReplacementAndSupportsCompletionRepair(string missingName)
    {
        var runtime = new EmittedRuntime();
        runtime.BeginZlibEmission();
        var zlib = runtime.RequireZlib();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"zlib_contract_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var properties = typeof(EmittedZlibRuntime).GetProperties()
            .Where(property => property.Name != nameof(EmittedZlibRuntime.IsComplete)).ToArray();
        var declarations = new Dictionary<string, MemberInfo>();
        foreach (var property in properties)
        {
            var type = module.DefineType(property.Name);
            MemberInfo handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder)
                    ? type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes)
                    : type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            declarations.Add(property.Name, handle);
            Assert.Contains(property.Name, Assert.IsType<InvalidOperationException>(
                Assert.Throws<TargetInvocationException>(() => property.GetValue(zlib)).InnerException).Message);
            Assert.IsType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(zlib, null)).InnerException);
            if (property.Name == missingName)
                continue;
            property.SetValue(zlib, handle);
            Assert.Contains(property.Name, Assert.IsType<InvalidOperationException>(
                Assert.Throws<TargetInvocationException>(() => property.SetValue(zlib, handle)).InnerException).Message);
            Assert.Same(handle, property.GetValue(zlib));
        }

        Assert.Contains(missingName, Assert.Throws<InvalidOperationException>(zlib.CompleteEmission).Message);
        Assert.False(zlib.IsComplete);
        var missing = properties.Single(property => property.Name == missingName);
        missing.SetValue(zlib, declarations[missingName]);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(
            () => missing.SetValue(zlib, declarations[missingName])).InnerException);
        zlib.CompleteEmission();
        Assert.True(zlib.IsComplete);
        foreach (var property in properties)
        {
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(
                () => property.SetValue(zlib, declarations[property.Name])).InnerException);
            Assert.Same(declarations[property.Name], property.GetValue(zlib));
        }
    }

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
        Assert.DoesNotContain(runtime.RuntimeClass.Type.GetMethods(), method => method.Name.StartsWith("Zlib", StringComparison.Ordinal));
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
        Assert.Empty(runtime.Deployment.Reasons);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsZlibHelpersAndTransformFieldsWithinEachAssembly(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedZlibRuntime>();
        var saved = new List<(Assembly Assembly, EmittedZlibRuntime Owner, object Stream)>();
        foreach (string? source in new[]
        {
            "import * as zlib from 'zlib';", "console.log(1);", "import * as stream from 'stream';",
            "import * as zlib from 'zlib'; import * as crypto from 'crypto';",
            null, "console.log(1);", "import * as zlib from 'zlib';"
        })
        {
            var builder = new PersistedAssemblyBuilder(new AssemblyName($"zlib_reuse_{Guid.NewGuid():N}"), typeof(object).Assembly);
            var module = builder.DefineDynamicModule("main");
            var features = source is null ? null : new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = features is null ? emitter.EmitAll(module) : emitter.EmitAll(module, features);
            using var bytes = new MemoryStream();
            builder.Save(bytes);
            bytes.Position = 0;
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(bytes));
            var assembly = Assembly.Load(bytes.ToArray());
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
            Assert.Equal(hosted, assembly.GetReferencedAssemblies().Any(reference => reference.Name == "SharpTS.Hosting.Abstractions"));
            if (source is "console.log(1);" or "import * as stream from 'stream';")
            {
                Assert.Null(runtime.Zlib);
                Assert.Null(assembly.GetType("$ZlibTransform"));
                Assert.Null(assembly.GetType("$Runtime")!.GetMethod("GetZlibInputBytes"));
                continue;
            }

            var zlib = runtime.RequireZlib();
            Assert.True(owners.Add(zlib));
            Assert.True(zlib.IsComplete);
            foreach (var property in typeof(EmittedZlibRuntime).GetProperties().Where(p => p.Name != nameof(EmittedZlibRuntime.IsComplete)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(zlib)).Module.Assembly);
            var stream = assembly.ManifestModule.ResolveMethod(zlib.CreateGzip.MetadataToken)!.Invoke(null,
                [new Dictionary<string, object> { ["level"] = 1d }])!;
            Assert.Same(assembly.GetType("$Transform"), stream.GetType().BaseType);
            saved.Add((assembly, zlib, stream));
        }

        // Earlier helpers and live compressors remain valid after later emissions.
        foreach (var (assembly, owner, stream) in saved)
        {
            object Call(string name, params object?[] arguments)
            {
                var handle = (MethodBuilder)typeof(EmittedZlibRuntime).GetProperty(name)!.GetValue(owner)!;
                return assembly.ManifestModule.ResolveMethod(handle.MetadataToken)!.Invoke(null, arguments)!;
            }

            const string text = "construction reuse π";
            foreach (var (compress, decompress) in new[]
            {
                (nameof(owner.GzipSync), nameof(owner.GunzipSync)),
                (nameof(owner.DeflateSync), nameof(owner.InflateSync)),
                (nameof(owner.DeflateRawSync), nameof(owner.InflateRawSync)),
                (nameof(owner.BrotliCompressSync), nameof(owner.BrotliDecompressSync))
            })
            {
                var options = new Dictionary<string, object>
                {
                    ["level"] = 1d, ["strategy"] = 0d,
                    ["params"] = new Dictionary<string, object> { ["1"] = 4d, ["2"] = 20d }
                };
                var compressed = Call(compress, text, options);
                var restored = Call(decompress, compressed, null);
                Assert.Equal(System.Text.Encoding.UTF8.GetBytes(text), ReadZlibBuffer(restored));
            }
            var gzip = Call(nameof(owner.GzipSync), text, null);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes(text), ReadZlibBuffer(Call(nameof(owner.UnzipSync), gzip, null)));
            var limited = Assert.Throws<TargetInvocationException>(() => Call(nameof(owner.GunzipSync), gzip,
                new Dictionary<string, object> { ["maxOutputLength"] = 1d }));
            Assert.Contains("maxOutputLength", limited.InnerException!.Message);

            var type = stream.GetType();
            Assert.Equal(0, ReadZlibField(stream, "_kind"));
            Assert.Equal(1, ReadZlibField(stream, "_level"));
            Assert.Equal(0L, ReadZlibField(stream, "_bytesWritten"));
            type.GetMethod("Write")!.Invoke(stream, ["one", null, null]);
            Assert.Equal(3d, type.GetProperty("BytesWritten")!.GetValue(stream));
            type.GetMethod("Reset")!.Invoke(stream, null);
            Assert.Equal(0d, type.GetProperty("BytesWritten")!.GetValue(stream));
            type.GetMethod("End")!.Invoke(stream, ["two", null, null]);
            Assert.Equal(3d, type.GetProperty("BytesRead")!.GetValue(stream));
            Assert.Null(ReadZlibField(stream, "_compressStream"));
            type.GetMethod("Close")!.Invoke(stream, [null]);
            Assert.Null(ReadZlibField(stream, "_inputMs"));
        }
    }

    private static object? ReadZlibField(object instance, string name)
        => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

    private static byte[] ReadZlibBuffer(object buffer) => Assert.IsType<byte[]>(ReadZlibField(buffer, "_data"));

    private static EmittedRuntime EmitRuntime(bool usesZlib)
    {
        var source = usesZlib ? "import * as zlib from 'zlib';" : "console.log(1);";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"zlib_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        return new RuntimeEmitter(TypeProvider.Runtime).EmitAll(assembly.DefineDynamicModule("main"), features);
    }
}
