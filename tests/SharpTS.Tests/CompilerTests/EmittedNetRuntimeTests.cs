using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedNetRuntimeTests
{
    [Theory]
    [InlineData("const list = new net.BlockList(); list.addAddress('127.0.0.2');")]
    [InlineData("const server = createServer({ highWaterMark: 4 }, (socket: any) => { socket.on('data', (chunk: any) => socket.write(chunk)); });")]
    [InlineData("const client = createConnection({ port: 12345, host: '127.0.0.1' });")]
    [InlineData("const factory = createConnection; const client = factory({ port: 12345, host: '127.0.0.1' });")]
    [InlineData("const socket = new net.Socket({ highWaterMark: 4 });")]
    [InlineData("const unconnected = net.Socket();")]
    public void ModuleConsumersAndDeferredTransportBodiesPassILVerification(string body)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as net from 'net';
                import { createServer, createConnection } from 'net';
                """ + Environment.NewLine + body
        };
        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void DisabledFeatureHasNoMetadataAndReportsAccidentalUse()
    {
        var runtime = EmitRuntime("console.log(1);");
        Assert.Null(runtime.Net);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireNet).Message);
        foreach (var name in new[] { "createServer", "createConnection", "createSocket", "createBlockList" })
            Assert.Null(runtime.GetBuiltInModuleMethod("primitive:net", name));
        Assert.DoesNotContain(runtime.RuntimeType.GetMethods(), method => method.Name.StartsWith("Net", StringComparison.Ordinal));

        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(stream);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        Assert.DoesNotContain("$NetSocket", names);
        Assert.DoesNotContain("$NetServer", names);
        Assert.DoesNotContain("$BlockList", names);
        Assert.DoesNotContain("$TcpAcceptClosure", names);
        Assert.DoesNotContain("$SocketReadDataClosure", names);
    }

    [Fact]
    public void SocketDeclarationSupportsCallsBeforeBodyEmission()
    {
        var runtime = new EmittedRuntime();
        runtime.BeginNetEmission();
        var net = runtime.RequireNet();
        Assert.False(net.IsComplete);
        Assert.Contains("SocketConnect", Assert.Throws<InvalidOperationException>(() => net.SocketConnect).Message);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("net_forward"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var socket = module.DefineType("Socket");
        net.SocketType = socket;
        net.SocketCtor = socket.DefineDefaultConstructor(MethodAttributes.Public);
        var connect = socket.DefineMethod("Connect", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        net.SocketConnect = connect;

        var factory = module.DefineType("Factory");
        net.CreateConnection = factory.DefineMethod("CreateConnection", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = net.CreateConnection.GetILGenerator();
        il.Emit(OpCodes.Newobj, net.SocketCtor);
        il.Emit(OpCodes.Callvirt, net.SocketConnect);
        il.Emit(OpCodes.Ret);
        Assert.Same(connect, net.SocketConnect);
        Assert.False(net.SocketType.IsCreated());
        connect.GetILGenerator().Emit(OpCodes.Ret);
        socket.CreateType();
        factory.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);

        Assert.Throws<InvalidOperationException>(runtime.BeginNetEmission);
        Assert.Contains("CreateServer", Assert.Throws<InvalidOperationException>(net.CompleteEmission).Message);
        Assert.False(net.IsComplete);
        Assert.Throws<ArgumentNullException>(() => net.SocketConnect = null!);
        Assert.Same(connect, net.SocketConnect);
    }

    public static IEnumerable<object[]> HandleNames => typeof(EmittedNetRuntime).GetProperties()
        .Where(property => property.Name != nameof(EmittedNetRuntime.IsComplete))
        .Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void CompletionRejectsEachMissingDeclaration(string missingHandle)
    {
        var net = new EmittedNetRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("net_incomplete"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        foreach (var property in typeof(EmittedNetRuntime).GetProperties())
        {
            if (property.Name == missingHandle || property.Name == nameof(EmittedNetRuntime.IsComplete))
                continue;
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor : method;
            property.SetValue(net, handle);
        }

        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(net.CompleteEmission).Message);
        Assert.False(net.IsComplete);
    }

    [Theory]
    [InlineData("import * as net from 'net';")]
    [InlineData("import * as tls from 'tls';")]
    [InlineData("import * as http from 'http';")]
    [InlineData("fetch('http://127.0.0.1/');")]
    [InlineData(null)]
    public void EnabledAndImpliedFeaturesCompleteHandlesAndRejectFurtherWrites(string? source)
    {
        var runtime = EmitRuntime(source);
        var net = runtime.RequireNet();
        Assert.True(net.IsComplete);
        Assert.Equal("$NetSocket", net.SocketType.Name);
        Assert.Equal("$NetServer", net.ServerType.Name);
        Assert.Equal("$BlockList", net.BlockListType.Name);
        Assert.True(net.SocketType.IsCreated());
        Assert.True(net.ServerType.IsCreated());
        Assert.True(net.BlockListType.IsCreated());
        Assert.Same(net.SocketType, net.SocketCtor.DeclaringType);
        Assert.Same(net.SocketType, net.SocketCtorTcpClient.DeclaringType);
        Assert.Same(net.SocketType, net.SocketCtorStream.DeclaringType);
        Assert.Same(net.SocketType, net.SocketConnect.DeclaringType);
        Assert.Same(net.ServerType, net.ServerCtor.DeclaringType);
        Assert.Same(net.BlockListType, net.BlockListCtor.DeclaringType);
        Assert.Same(net.BlockListType, net.BlockListCheckIp.DeclaringType);
        Assert.Same(net.CreateServer, runtime.GetBuiltInModuleMethod("primitive:net", "createServer"));
        Assert.Same(net.CreateConnection, runtime.GetBuiltInModuleMethod("primitive:net", "createConnection"));
        Assert.Same(net.CreateSocket, runtime.GetBuiltInModuleMethod("primitive:net", "createSocket"));
        Assert.Same(net.CreateBlockList, runtime.GetBuiltInModuleMethod("primitive:net", "createBlockList"));
        foreach (var property in typeof(EmittedNetRuntime).GetProperties()
            .Where(property => property.Name != nameof(EmittedNetRuntime.IsComplete)))
        {
            var value = property.GetValue(net);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var exception = Assert.Throws<TargetInvocationException>(() => property.SetValue(net, value));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }
        Assert.Throws<InvalidOperationException>(net.CompleteEmission);
        if (source == "import * as net from 'net';")
        {
            Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
            Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        }
    }

    private static EmittedRuntime EmitRuntime(string? source)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"net_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
