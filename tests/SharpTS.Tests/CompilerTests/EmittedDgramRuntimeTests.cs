using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedDgramRuntimeTests
{
    [Theory]
    [InlineData(nameof(EmittedDgramRuntime.CreateSocket))]
    [InlineData(nameof(EmittedDgramRuntime.SocketType))]
    [InlineData(nameof(EmittedDgramRuntime.SocketCtor))]
    [InlineData(nameof(EmittedDgramRuntime.ReceiveWorker))]
    [InlineData(nameof(EmittedDgramRuntime.MessageClosureCtor))]
    [InlineData(nameof(EmittedDgramRuntime.MessageClosureRun))]
    public void EveryDeclarationRejectsReplacementAndSupportsCompletionRepair(string missingName)
    {
        var runtime = new EmittedRuntime();
        runtime.BeginDgramEmission();
        var dgram = runtime.RequireDgram();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"dgram_contract_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var properties = typeof(EmittedDgramRuntime).GetProperties()
            .Where(property => property.Name != nameof(EmittedDgramRuntime.IsComplete)).ToArray();
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
                Assert.Throws<TargetInvocationException>(() => property.GetValue(dgram)).InnerException).Message);
            Assert.IsType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(dgram, null)).InnerException);
            if (property.Name == missingName)
                continue;
            property.SetValue(dgram, handle);
            Assert.Contains(property.Name, Assert.IsType<InvalidOperationException>(
                Assert.Throws<TargetInvocationException>(() => property.SetValue(dgram, handle)).InnerException).Message);
            Assert.Same(handle, property.GetValue(dgram));
        }

        Assert.Contains(missingName, Assert.Throws<InvalidOperationException>(dgram.CompleteEmission).Message);
        Assert.False(dgram.IsComplete);
        var missing = properties.Single(property => property.Name == missingName);
        missing.SetValue(dgram, declarations[missingName]);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(
            () => missing.SetValue(dgram, declarations[missingName])).InnerException);
        dgram.CompleteEmission();
        Assert.True(dgram.IsComplete);
        foreach (var property in properties)
        {
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(
                () => property.SetValue(dgram, declarations[property.Name])).InnerException);
            Assert.Same(declarations[property.Name], property.GetValue(dgram));
        }
    }

    [Fact]
    public void ModuleConsumersAndDeferredReceiveBodyPassILVerification()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                import { createSocket } from 'dgram';
                const receiver = dgram.createSocket('udp4');
                const sender = createSocket('udp4');
                const factory = createSocket;
                const unbound = factory('udp4');
                receiver.on('message', (msg: any) => {
                    console.log(msg.toString());
                    receiver.close();
                });
                receiver.bind(0, '127.0.0.1', () => {
                    sender.send('hello', receiver.address().port, '127.0.0.1', () => sender.close());
                });
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Fact]
    public void DisabledFeatureHasNoMetadataAndReportsAccidentalUse()
    {
        var runtime = EmitRuntime(false);
        Assert.Null(runtime.Dgram);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireDgram).Message);
        Assert.Null(runtime.BuiltInModules.GetOptional("dgram", "createSocket"));
        Assert.DoesNotContain(runtime.RuntimeClass.Type.GetMethods(), method => method.Name.StartsWith("Dgram", StringComparison.Ordinal));
        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(stream);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var typeNames = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        Assert.DoesNotContain("$DatagramSocket", typeNames);
        Assert.DoesNotContain("$DgramMessageClosure", typeNames);
    }

    [Fact]
    public void ReceiveWorkerDeclarationSupportsForwardReferencesAndIncompleteEmissionFails()
    {
        var runtime = new EmittedRuntime();
        runtime.BeginDgramEmission();
        var dgram = runtime.RequireDgram();
        Assert.False(dgram.IsComplete);
        Assert.Contains("ReceiveWorker", Assert.Throws<InvalidOperationException>(() => dgram.ReceiveWorker).Message);
        Assert.Contains("SocketCtor", Assert.Throws<InvalidOperationException>(() => dgram.SocketCtor).Message);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("dgram_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Socket");
        var worker = type.DefineMethod("ReceiveWorker", MethodAttributes.Private, typeof(void), [typeof(object)]);
        dgram.SocketType = type;
        dgram.ReceiveWorker = worker;
        var caller = type.DefineMethod("Bind", MethodAttributes.Public, typeof(void), Type.EmptyTypes).GetILGenerator();
        caller.Emit(OpCodes.Ldarg_0);
        caller.Emit(OpCodes.Ldnull);
        caller.Emit(OpCodes.Call, dgram.ReceiveWorker);
        caller.Emit(OpCodes.Ret);
        Assert.Same(worker, dgram.ReceiveWorker);
        Assert.False(dgram.SocketType.IsCreated());
        worker.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();

        Assert.Throws<InvalidOperationException>(runtime.BeginDgramEmission);
        Assert.Contains("CreateSocket", Assert.Throws<InvalidOperationException>(dgram.CompleteEmission).Message);
        Assert.False(dgram.IsComplete);
        Assert.Throws<ArgumentNullException>(() => dgram.ReceiveWorker = null!);
        Assert.Same(worker, dgram.ReceiveWorker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnabledFeatureCompletesAllHandlesAndRejectsFurtherWrites(bool emitEverything)
    {
        var runtime = EmitRuntime(true, emitEverything);
        var dgram = runtime.RequireDgram();
        Assert.True(dgram.IsComplete);
        Assert.Equal("DgramCreateSocket", dgram.CreateSocket.Name);
        Assert.Equal("$DatagramSocket", dgram.SocketType.Name);
        Assert.True(dgram.SocketType.IsCreated());
        Assert.Same(dgram.SocketType, dgram.SocketCtor.DeclaringType);
        Assert.Same(dgram.SocketType, dgram.ReceiveWorker.DeclaringType);
        Assert.Equal("_DgramReceiveWorker", dgram.ReceiveWorker.Name);
        Assert.Equal("$DgramMessageClosure", dgram.MessageClosureCtor.DeclaringType!.Name);
        Assert.Same(dgram.MessageClosureCtor.DeclaringType, dgram.MessageClosureRun.DeclaringType);
        Assert.True(((TypeBuilder)dgram.MessageClosureRun.DeclaringType!).IsCreated());
        Assert.Same(dgram.CreateSocket, runtime.BuiltInModules.GetOptional("dgram", "createSocket"));
        Assert.Throws<InvalidOperationException>(() => dgram.CreateSocket = dgram.CreateSocket);
        Assert.Throws<InvalidOperationException>(() => dgram.SocketType = dgram.SocketType);
        Assert.Throws<InvalidOperationException>(() => dgram.SocketCtor = dgram.SocketCtor);
        Assert.Throws<InvalidOperationException>(() => dgram.ReceiveWorker = dgram.ReceiveWorker);
        Assert.Throws<InvalidOperationException>(() => dgram.MessageClosureCtor = dgram.MessageClosureCtor);
        Assert.Throws<InvalidOperationException>(() => dgram.MessageClosureRun = dgram.MessageClosureRun);
        Assert.Throws<InvalidOperationException>(dgram.CompleteEmission);
        if (!emitEverything)
        {
            Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
            Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsDatagramConstructionWithinEachAssembly(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedDgramRuntime>();
        var saved = new List<(Assembly Assembly, object Socket)>();
        foreach (string? source in new[]
        {
            "import * as dgram from 'dgram';", "console.log(1);", "import * as net from 'net';",
            "import * as dgram from 'dgram'; import * as tls from 'tls'; import * as http from 'http';",
            null, "console.log(1);", "import * as dgram from 'dgram';"
        })
        {
            var builder = new PersistedAssemblyBuilder(new AssemblyName($"dgram_reuse_{Guid.NewGuid():N}"), typeof(object).Assembly);
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
                Assert.Null(runtime.Dgram);
                Assert.Null(assembly.GetType("$DatagramSocket"));
                Assert.Null(assembly.GetType("$DgramMessageClosure"));
                continue;
            }

            var dgram = runtime.RequireDgram();
            Assert.True(owners.Add(dgram));
            Assert.True(dgram.IsComplete);
            foreach (var property in typeof(EmittedDgramRuntime).GetProperties().Where(p => p.Name != nameof(EmittedDgramRuntime.IsComplete)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(dgram)).Module.Assembly);
            var socketType = assembly.GetType("$DatagramSocket")!;
            Assert.Same(assembly.GetType("$EventEmitter"), socketType.BaseType);
            Assert.Same(socketType, assembly.GetType("$DgramMessageClosure")!.GetConstructors().Single().GetParameters()[0].ParameterType);
            foreach (string family in new[] { "udp4", "udp6" })
            {
                var socket = assembly.ManifestModule.ResolveMethod(dgram.CreateSocket.MetadataToken)!.Invoke(null, [family, null])!;
                Assert.Equal(family == "udp4" ? 2 : 23, ReadDatagramField(socket, "_family"));
                saved.Add((assembly, socket));
            }
        }

        // Use the earlier sockets, helper bodies and closures after later emissions finish.
        foreach (var (assembly, socket) in saved)
        {
            var type = socket.GetType();
            Assert.Null(ReadDatagramField(socket, "_client"));
            Assert.Equal(false, ReadDatagramField(socket, "_bound"));
            Assert.Equal(false, ReadDatagramField(socket, "_closed"));
            Assert.Equal(false, ReadDatagramField(socket, "_connected"));
            Assert.Equal("", ReadDatagramField(socket, "_connectedAddress"));
            Assert.Equal(0, ReadDatagramField(socket, "_connectedPort"));
            Assert.Empty(Assert.IsType<Dictionary<string, object>>(type.GetMethod("Address")!.Invoke(socket, null)));
            foreach (string helper in new[] { "_EmitListening", "_EmitConnect", "_EmitClose", "_FireCloseCallback", "_FireBindError" })
                type.GetMethod(helper, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(socket, null);

            var closure = Activator.CreateInstance(assembly.GetType("$DgramMessageClosure")!,
                socket, new byte[] { 65 }, "127.0.0.1", "IPv4", 123d, 1d)!;
            Assert.Same(socket, ReadDatagramField(closure, "_socket"));
            Assert.Equal(123d, ReadDatagramField(closure, "_port"));
            closure.GetType().GetMethod("Run")!.Invoke(closure, null);

            using var cancellation = new CancellationTokenSource();
            type.GetField("_receiveCts", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(socket, cancellation);
            var marker = new object();
            type.GetMethod("Close")!.Invoke(socket, [marker]);
            Assert.Equal(true, ReadDatagramField(socket, "_closed"));
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Same(marker, ReadDatagramField(socket, "_pendingCloseCallback"));
            type.GetMethod("_DgramReceiveWorker", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(socket, [null]);
            var loopType = assembly.GetType("$EventLoop")!;
            var loop = loopType.GetMethod("GetInstance")!.Invoke(null, null)!;
            loopType.GetMethod("PumpOnce")!.Invoke(loop, null);
            Assert.Null(ReadDatagramField(socket, "_pendingCloseCallback"));
            Assert.Null(ReadDatagramField(socket, "_pendingError"));
            type.GetMethod("Close")!.Invoke(socket, [null]);
            Assert.Equal(false, loopType.GetMethod("HasPendingWork")!.Invoke(loop, null));
        }
    }

    private static object? ReadDatagramField(object instance, string name)
        => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);

    private static EmittedRuntime EmitRuntime(bool usesDgram, bool emitEverything = false)
    {
        var source = usesDgram ? "import * as dgram from 'dgram';" : "console.log(1);";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"dgram_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        return emitEverything ? emitter.EmitAll(module) : emitter.EmitAll(module, features);
    }
}
