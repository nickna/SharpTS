using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedTlsRuntimeTests
{
    [Fact]
    public void ModuleConsumersAndDeferredBodiesPassILVerification()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as tls from 'tls';
                import { createServer, connect, createSecureContext, getCiphers } from 'tls';
                console.log(tls.DEFAULT_MIN_VERSION, tls.DEFAULT_MAX_VERSION);
                console.log(tls.rootCertificates.length, getCiphers().length);
                const server = createServer();
                const socket = connect({ port: 12345, host: '127.0.0.1' });
                const context = createSecureContext();
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Fact]
    public void DisabledFeatureHasNoMetadataAndReportsAccidentalUse()
    {
        var runtime = EmitRuntime(false);
        Assert.Null(runtime.Tls);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireTls).Message);
        Assert.DoesNotContain(runtime.RuntimeType.GetMethods(), method => method.Name.StartsWith("Tls", StringComparison.Ordinal));
    }

    [Fact]
    public void ConnectDeclarationSupportsForwardReferencesAndIncompleteEmissionFails()
    {
        var runtime = new EmittedRuntime();
        runtime.BeginTlsEmission();
        var tls = runtime.RequireTls();
        Assert.False(tls.IsComplete);
        Assert.Contains("Connect", Assert.Throws<InvalidOperationException>(() => tls.Connect).Message);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("tls_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Runtime");
        var method = type.DefineMethod("Connect", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        tls.Connect = method;
        Assert.Same(method, tls.Connect);
        Assert.Throws<InvalidOperationException>(runtime.BeginTlsEmission);
        Assert.Contains("CreateServer", Assert.Throws<InvalidOperationException>(tls.CompleteEmission).Message);
        Assert.False(tls.IsComplete);
        Assert.Throws<ArgumentNullException>(() => tls.Connect = null!);
        Assert.Same(method, tls.Connect);
    }

    [Fact]
    public void EnabledFeatureCompletesAllHandlesAndRejectsFurtherWrites()
    {
        var runtime = EmitRuntime(true);
        var tls = runtime.RequireTls();
        Assert.True(tls.IsComplete);
        Assert.Equal("TlsConnect", tls.Connect.Name);
        Assert.Equal("$TlsSocket", tls.SocketType.Name);
        Assert.NotNull(tls.SocketCtor);
        Assert.NotNull(tls.ServerCtor);
        Assert.All(typeof(EmittedTlsRuntime).GetProperties()
            .Where(property => property.PropertyType == typeof(MethodBuilder)),
            property => Assert.NotNull(property.GetValue(tls)));
        Assert.Same(tls.Connect, runtime.GetBuiltInModuleMethod("tls", "connect"));
        Assert.Same(tls.CreateServer, runtime.GetBuiltInModuleMethod("tls", "Server"));
        Assert.Throws<InvalidOperationException>(() => tls.Connect = tls.Connect);
        Assert.Throws<InvalidOperationException>(() => tls.SocketCtor = tls.SocketCtor);
        Assert.Throws<InvalidOperationException>(() => tls.SocketType = tls.SocketType);
        Assert.Throws<InvalidOperationException>(tls.CompleteEmission);
        Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
    }

    private static EmittedRuntime EmitRuntime(bool usesTls)
    {
        var source = usesTls ? "import * as tls from 'tls';" : "console.log(1);";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"tls_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        return new RuntimeEmitter(TypeProvider.Runtime).EmitAll(assembly.DefineDynamicModule("main"), features);
    }
}
