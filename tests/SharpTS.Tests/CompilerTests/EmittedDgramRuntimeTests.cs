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
        Assert.Null(runtime.GetBuiltInModuleMethod("dgram", "createSocket"));
        Assert.DoesNotContain(runtime.RuntimeType.GetMethods(), method => method.Name.StartsWith("Dgram", StringComparison.Ordinal));
        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(stream);
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
        Assert.Same(dgram.CreateSocket, runtime.GetBuiltInModuleMethod("dgram", "createSocket"));
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
