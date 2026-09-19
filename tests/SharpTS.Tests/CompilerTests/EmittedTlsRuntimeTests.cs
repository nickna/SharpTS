using System.Reflection;
using System.Reflection.Emit;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedTlsRuntimeTests
{
    [Theory]
    [InlineData(nameof(EmittedTlsRuntime.CreateServer))]
    [InlineData(nameof(EmittedTlsRuntime.Connect))]
    [InlineData(nameof(EmittedTlsRuntime.CreateSecureContext))]
    [InlineData(nameof(EmittedTlsRuntime.GetDefaultMinVersion))]
    [InlineData(nameof(EmittedTlsRuntime.GetDefaultMaxVersion))]
    [InlineData(nameof(EmittedTlsRuntime.GetCiphers))]
    [InlineData(nameof(EmittedTlsRuntime.RootCertificates))]
    [InlineData(nameof(EmittedTlsRuntime.CreateSocket))]
    [InlineData(nameof(EmittedTlsRuntime.SocketType))]
    [InlineData(nameof(EmittedTlsRuntime.SocketCtor))]
    [InlineData(nameof(EmittedTlsRuntime.ServerCtor))]
    public void EveryDeclarationRejectsReplacementAndSupportsCompletionRepair(string missingName)
    {
        var runtime = new EmittedRuntime();
        runtime.BeginTlsEmission();
        var tls = runtime.RequireTls();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"tls_contract_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var properties = typeof(EmittedTlsRuntime).GetProperties()
            .Where(property => property.Name != nameof(EmittedTlsRuntime.IsComplete)).ToArray();
        var declarations = new Dictionary<string, MemberInfo>();
        foreach (var property in properties)
        {
            var type = module.DefineType(property.Name);
            MemberInfo handle = property.PropertyType == typeof(Type) ? type
                : property.PropertyType == typeof(ConstructorBuilder)
                    ? type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes)
                    : type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            declarations.Add(property.Name, handle);
            Assert.Contains(property.Name, Assert.IsType<InvalidOperationException>(
                Assert.Throws<TargetInvocationException>(() => property.GetValue(tls)).InnerException).Message);
            Assert.IsType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(tls, null)).InnerException);
            if (property.Name == missingName)
                continue;
            property.SetValue(tls, handle);
            Assert.Contains(property.Name, Assert.IsType<InvalidOperationException>(
                Assert.Throws<TargetInvocationException>(() => property.SetValue(tls, handle)).InnerException).Message);
            Assert.Same(handle, property.GetValue(tls));
        }

        Assert.Contains(missingName, Assert.Throws<InvalidOperationException>(tls.CompleteEmission).Message);
        Assert.False(tls.IsComplete);
        var missing = properties.Single(property => property.Name == missingName);
        missing.SetValue(tls, declarations[missingName]);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(
            () => missing.SetValue(tls, declarations[missingName])).InnerException);
        tls.CompleteEmission();
        Assert.True(tls.IsComplete);
        foreach (var property in properties)
        {
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(
                () => property.SetValue(tls, declarations[property.Name])).InnerException);
            Assert.Same(declarations[property.Name], property.GetValue(tls));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsTlsConstructionAndCertificateHelpersWithinEachAssembly(bool hosted)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=reuse.example", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("reuse.example");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        string certPem = certificate.ExportCertificatePem();
        string keyPem = rsa.ExportPkcs8PrivateKeyPem();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedTlsRuntime>();
        var saved = new List<(Assembly Assembly, object Socket, object Server)>();
        foreach (string? source in new[]
        {
            "import * as tls from 'tls';", "console.log(1);", "import * as net from 'net';",
            "import * as tls from 'tls'; import * as http from 'http';",
            null, "console.log(1);", "import * as tls from 'tls';"
        })
        {
            var builder = new PersistedAssemblyBuilder(new AssemblyName($"tls_reuse_{Guid.NewGuid():N}"), typeof(object).Assembly);
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
            if (source is "console.log(1);" or "import * as net from 'net';")
            {
                Assert.Null(runtime.Tls);
                Assert.Null(assembly.GetType("$TlsSocket"));
                Assert.Null(assembly.GetType("$TlsServer"));
                Assert.Null(assembly.GetType("$TlsAcceptClosure"));
                Assert.Null(assembly.GetType("$TlsConnectClosure"));
                Assert.Null(assembly.GetType("$Runtime")!.GetMethod("TlsCheckServerIdentity"));
                continue;
            }

            var tls = runtime.RequireTls();
            Assert.True(owners.Add(tls));
            Assert.True(tls.IsComplete);
            foreach (var property in typeof(EmittedTlsRuntime).GetProperties().Where(p => p.Name != nameof(EmittedTlsRuntime.IsComplete)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(tls)).Module.Assembly);
            var socketType = assembly.GetType("$TlsSocket")!;
            var serverType = assembly.GetType("$TlsServer")!;
            Assert.Same(assembly.GetType("$NetSocket"), socketType.BaseType);
            foreach (string name in new[] { "$TlsConnectClosure", "$TlsConnectOkClosure", "$TlsConnectErrClosure" })
                Assert.Same(socketType, assembly.GetType(name)!.GetConstructors().Single().GetParameters()[0].ParameterType);
            Assert.Equal(new[] { serverType, socketType }, assembly.GetType("$TlsAcceptClosure")!.GetConstructors().Single().GetParameters().Select(p => p.ParameterType));
            Assert.Same(serverType, assembly.GetType("$TlsAcceptErrorClosure")!.GetConstructors().Single().GetParameters()[0].ParameterType);
            var socket = assembly.ManifestModule.ResolveMethod(tls.CreateSocket.MetadataToken)!.Invoke(null, null)!;
            var context = assembly.ManifestModule.ResolveMethod(tls.CreateSecureContext.MetadataToken)!.Invoke(null,
                [new Dictionary<string, object> { ["cert"] = certPem, ["key"] = keyPem }])!;
            var server = assembly.ManifestModule.ResolveMethod(tls.CreateServer.MetadataToken)!.Invoke(null,
                [new Dictionary<string, object> { ["secureContext"] = context, ["requestCert"] = true, ["ALPNProtocols"] = new List<object> { "h2" } }, null])!;
            saved.Add((assembly, socket, server));
        }

        // Exercise earlier assemblies after all later emissions have finished.
        foreach (var (assembly, socket, server) in saved)
        {
            var socketType = socket.GetType();
            Assert.Equal(certPem, ReadTlsField(server, "_cert"));
            Assert.Equal(keyPem, ReadTlsField(server, "_key"));
            Assert.Equal(true, ReadTlsField(server, "_requestCert"));
            Assert.Equal(new[] { "h2" }, Assert.IsType<string[]>(ReadTlsField(server, "_alpn")));
            Assert.Equal(false, server.GetType().GetProperty("Listening")!.GetValue(server));
            Assert.Same(server, server.GetType().GetMethod("Close")!.Invoke(server, [null]));
            Assert.Equal("TLSv1.2", InvokeTlsHelper(socketType, "_ProtoString", [SslProtocols.Tls12]));
            Assert.Equal("TLSv1.3", InvokeTlsHelper(socketType, "_ProtoString", [SslProtocols.Tls13]));
            Assert.Equal("self-signed certificate", InvokeTlsHelper(socketType, "_DescribePolicyErrors", [SslPolicyErrors.RemoteCertificateChainErrors]));
            var protocols = Assert.IsType<List<SslApplicationProtocol>>(InvokeTlsHelper(socketType, "_BuildAlpnList", [new[] { "h2", "http/1.1" }]));
            Assert.Equal(new[] { SslApplicationProtocol.Http2, SslApplicationProtocol.Http11 }, protocols);
            using var loadedCert = Assert.IsType<X509Certificate2>(InvokeTlsHelper(socketType, "_LoadCert", [certPem, keyPem]));
            Assert.True(loadedCert.HasPrivateKey);
            socketType.GetField("_peerCert", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(socket, loadedCert);
            var peer = Assert.IsType<Dictionary<string, object>>(socketType.GetMethod("GetPeerCertificate")!.Invoke(socket, [null]));
            Assert.Equal("DNS:reuse.example, IP Address:127.0.0.1", peer["subjectaltname"]);
            var check = assembly.GetType("$Runtime")!.GetMethod("TlsCheckServerIdentity")!;
            Assert.Same(assembly.GetType("$Undefined"), check.Invoke(null, ["reuse.example", peer])!.GetType());
            Assert.Same(assembly.GetType("$Error"), check.Invoke(null, ["missing.example", peer])!.GetType());

            var connectType = assembly.GetType("$TlsConnectClosure")!;
            foreach (bool reject in new[] { false, true })
            {
                var connect = Activator.CreateInstance(connectType, socket, 443, "reuse.example", reject, new[] { "h2" })!;
                var validate = connectType.GetMethod("_Validate")!;
                Assert.Equal(true, validate.Invoke(connect, [null, loadedCert, null, SslPolicyErrors.None]));
                Assert.Equal(!reject, validate.Invoke(connect, [null, loadedCert, null, SslPolicyErrors.RemoteCertificateChainErrors]));
                Assert.Equal(SslPolicyErrors.RemoteCertificateChainErrors, ReadTlsField(connect, "_policyErrors"));
            }
        }
    }

    private static object? InvokeTlsHelper(Type type, string name, object?[] arguments)
        => type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, arguments);

    private static object? ReadTlsField(object instance, string name)
        => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

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
        Assert.DoesNotContain(runtime.RuntimeClass.Type.GetMethods(), method => method.Name.StartsWith("Tls", StringComparison.Ordinal));
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
