using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedMessageChannelRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles(Type type) => type.GetProperties()
        .Where(property => property.PropertyType != typeof(bool) && property.PropertyType != typeof(EmittedMessagePortRuntime));

    public static IEnumerable<object[]> HandleNames => new[] { typeof(EmittedMessageChannelRuntime), typeof(EmittedMessagePortRuntime) }
        .SelectMany(type => Handles(type).Select(property => new object[] { type, property.Name }));

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationPreventsBothOwnersFromFreezingAndAllowsRepair(Type type, string missingHandle)
    {
        var channels = CreateDeclarations(type, missingHandle);
        object component = type == typeof(EmittedMessageChannelRuntime) ? channels : channels.Port;
        var property = type.GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(component));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(channels.CompleteEmission).Message);
        Assert.False(channels.IsComplete);
        Assert.False(channels.Port.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = CreateDeclarations();
        property.SetValue(component, property.GetValue(type == typeof(EmittedMessageChannelRuntime) ? declarations : declarations.Port));
        channels.CompleteEmission();
        AssertFrozen(channels);
    }

    [Fact]
    public void RequiredOwnersAreIndependentAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.MessageChannels, second.MessageChannels);
        Assert.NotSame(first.MessageChannels.Port, second.MessageChannels.Port);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.MessageChannels))!.SetMethod);
        Assert.Null(typeof(EmittedMessageChannelRuntime).GetProperty(nameof(EmittedMessageChannelRuntime.Port))!.SetMethod);
        Assert.Throws<InvalidOperationException>(first.MessageChannels.CompleteEmission);
        Assert.False(first.MessageChannels.Port.IsComplete);
    }

    [Fact]
    public void ConstructorAndFactoryBodiesCanUseDeclarationsBeforeTypeCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("message_channel_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, Detect("const value=1;"));
        var portType = module.DefineType("StagedPort", TypeAttributes.Public, runtime.EventEmitter.Type);
        var channels = new EmittedMessageChannelRuntime();
        channels.Port.Type = portType;
        channels.Port.Pending = portType.DefineField("_pending", typeof(ConcurrentQueue<object>), FieldAttributes.Assembly);
        typeof(RuntimeEmitter).GetMethod("EmitMessagePortConstructorIl", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [portType, channels.Port, runtime.EventEmitter]);
        Assert.True(channels.Port.Ctor.GetILGenerator().ILOffset > 0);
        Assert.False(portType.IsCreated());
        Assert.False(channels.Port.IsComplete);
        // A partially emitted port must remain repairable when the parent is incomplete.
        Assert.Throws<InvalidOperationException>(channels.CompleteEmission);
        Assert.False(channels.Port.IsComplete);
        portType.CreateType();

        var channelType = module.DefineType("StagedChannel", TypeAttributes.Public);
        channels.Type = channelType;
        channels.Ctor = channelType.DefineDefaultConstructor(MethodAttributes.Public);
        var factory = module.DefineType("StagedFactory", TypeAttributes.Public);
        typeof(RuntimeEmitter).GetMethod("EmitCreateMessageChannelHelper", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [factory, channels]);
        Assert.Equal("CreateMessageChannel", channels.Create.Name);
        Assert.True(channels.Create.GetILGenerator().ILOffset > 0);
        Assert.False(factory.IsCreated());
        channelType.CreateType();
        factory.CreateType();
        using var bytes = Save(runtime);
        Verify(bytes);
        var loaded = Assembly.Load(bytes.ToArray());
        Assert.Equal("StagedChannel", loaded.GetType("StagedFactory")!.GetMethod("CreateMessageChannel")!.Invoke(null, null)!.GetType().Name);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value=1;", true)]
    [InlineData("new MessageChannel();", false)]
    [InlineData("new MessageChannel();", true)]
    [InlineData("import {MessageChannel} from 'worker_threads';", false)]
    [InlineData("import * as workers from 'node:worker_threads';", false)]
    [InlineData("const workers=require('worker_threads');", false)]
    [InlineData("new BroadcastChannel('name');", false)]
    [InlineData("new Uint8Array(2);", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void RequiredDeclarationsRemainPresentAcrossFeatureSets(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.MessageChannels);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        Assert.Contains("$MessagePort", types);
        Assert.Contains("$MessageChannel", types);
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        Assert.Contains("CreateMessageChannel", methods);
        Assert.Contains("WorkerThreadsReceiveMessageOnPort", methods);
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedPortsPreservePeerFieldsCloningNotificationAndReceive(bool hosted)
    {
        using var bytes = Save(EmitRuntime("new MessageChannel();", hosted));
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$MessagePort")!;
        foreach (var name in new[] { "_partner", "_pending", "_started", "_closed", "_refed", "_crossThread", "_onEnqueue", "_cloneError" })
            Assert.True(type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!.IsAssembly);
        var sentinel = type.GetField("_cloneError", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(sentinel.IsInitOnly);
        Assert.Same(sentinel.GetValue(null), sentinel.GetValue(null));
        var post = type.GetMethod("PostMessage")!;
        Assert.Equal("$DataCloneError", Assert.Single(post.GetMethodBody()!.ExceptionHandlingClauses).CatchType!.Name);
        Assert.Equal("$EventEmitter", type.GetMethod("OnListenerAdded")!.GetBaseDefinition().DeclaringType!.Name);
        var (first, second) = NewPair(assembly);
        Assert.Same(second, Field(type, "_partner").GetValue(first));
        Assert.Same(first, Field(type, "_partner").GetValue(second));
        int notified = 0;
        Field(type, "_onEnqueue").SetValue(second, (Action)(() => notified++));
        var original = new Dictionary<string, object> { ["value"] = 7d };
        post.Invoke(first, [original]);
        original["value"] = 9d;
        Assert.Equal(1, notified);
        var received = Assert.IsType<Dictionary<string, object>>(Receive(assembly, second));
        var copy = Assert.IsType<Dictionary<string, object>>(received["message"]);
        Assert.Equal(7d, copy["value"]);
        Assert.NotSame(original, copy);
        var undefined = Receive(assembly, second);
        Assert.Equal("$Undefined", undefined!.GetType().Name);
        Assert.Same(undefined, Receive(assembly, null));
        Assert.Same(undefined, Receive(assembly, new object()));
        var queue = Assert.IsType<ConcurrentQueue<object>>(Field(type, "_pending").GetValue(second));
        queue.Enqueue(sentinel.GetValue(null)!);
        var marker = Assert.IsType<Dictionary<string, object>>(Receive(assembly, second));
        Assert.Same(undefined, marker["message"]);
        type.GetMethod("Close")!.Invoke(second, null);
        post.Invoke(first, [10d]);
        Assert.Same(undefined, Receive(assembly, second));
        Assert.Equal(1, notified);
        type.GetMethod("Close")!.Invoke(first, null);
    }

    [Fact]
    public void TransferMarksBothPeersAndBalancesOnlyStartedCrossThreadReferences()
    {
        var runtime = EmitRuntime("new MessageChannel();", false);
        using var bytes = Save(runtime);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$MessagePort")!;
        var (first, second) = NewPair(assembly);
        var loop = assembly.GetType("$EventLoop")!;
        var instance = loop.GetMethod("GetInstance")!.Invoke(null, null);
        var active = Field(loop, runtime.EventLoop.ActiveHandlesField.Name);
        int Count() => Assert.IsType<int>(active.GetValue(instance));
        Assert.Equal(0, Count());
        type.GetMethod("Start")!.Invoke(first, null);
        Assert.Equal(0, Count());
        type.GetMethod("MarkTransferredAcrossThreads")!.Invoke(second, null);
        Assert.Equal(true, Field(type, "_crossThread").GetValue(first));
        Assert.Equal(true, Field(type, "_crossThread").GetValue(second));
        Assert.Equal(1, Count());
        type.GetMethod("Start")!.Invoke(second, null);
        type.GetMethod("Start")!.Invoke(second, null);
        type.GetMethod("Ref")!.Invoke(first, null);
        Assert.Equal(2, Count());
        type.GetMethod("Close")!.Invoke(first, null);
        type.GetMethod("Close")!.Invoke(first, null);
        Assert.Equal(1, Count());
        type.GetMethod("Close")!.Invoke(second, null);
        type.GetMethod("Unref")!.Invoke(second, null);
        Assert.Equal(0, Count());
    }

    [Fact]
    public void ReusingEmitterKeepsOwnersQueuesAndCloneSentinelsIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("new MessageChannel();", false, emitter);
        var second = EmitRuntime("const value=1;", false, emitter);
        Assert.NotSame(first.MessageChannels, second.MessageChannels);
        Assert.NotSame(first.MessageChannels.Port, second.MessageChannels.Port);
        Assert.NotSame(first.MessageChannels.Port.Type, second.MessageChannels.Port.Type);
        Assert.NotSame(first.MessageChannels.Create, second.MessageChannels.Create);
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        var firstAssembly = Assembly.Load(firstBytes.ToArray());
        var secondAssembly = Assembly.Load(secondBytes.ToArray());
        Assert.NotSame(Field(firstAssembly.GetType("$MessagePort")!, "_cloneError").GetValue(null),
            Field(secondAssembly.GetType("$MessagePort")!, "_cloneError").GetValue(null));
        var (sender, receiver) = NewPair(firstAssembly);
        var (_, otherReceiver) = NewPair(secondAssembly);
        sender.GetType().GetMethod("PostMessage")!.Invoke(sender, [5d]);
        Assert.Equal(5d, Assert.IsType<Dictionary<string, object>>(Receive(firstAssembly, receiver))["message"]);
        Assert.Equal("$Undefined", Receive(secondAssembly, otherReceiver)!.GetType().Name);
        AssertFrozen(first.MessageChannels);
        AssertFrozen(second.MessageChannels);
    }

    private static FieldInfo Field(Type type, string name) => type.GetField(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;

    private static (object First, object Second) NewPair(Assembly assembly)
    {
        var channel = assembly.GetType("$Runtime")!.GetMethod("CreateMessageChannel")!.Invoke(null, null)!;
        return (channel.GetType().GetProperty("Port1")!.GetValue(channel)!, channel.GetType().GetProperty("Port2")!.GetValue(channel)!);
    }

    private static object? Receive(Assembly assembly, object? port) => assembly.GetType("$Runtime")!
        .GetMethod("WorkerThreadsReceiveMessageOnPort")!.Invoke(null, [port]);

    private static EmittedMessageChannelRuntime CreateDeclarations(Type? omittedType = null, string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"message_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var channels = new EmittedMessageChannelRuntime();
        foreach (var component in new object[] { channels, channels.Port })
        {
            var type = module.DefineType(component.GetType().Name, TypeAttributes.Public);
            var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
            foreach (var property in Handles(component.GetType()))
            {
                if (component.GetType() == omittedType && property.Name == omitted) continue;
                object value;
                if (property.PropertyType == typeof(TypeBuilder)) value = type;
                else if (property.PropertyType == typeof(ConstructorBuilder)) value = ctor;
                else if (property.PropertyType == typeof(FieldBuilder)) value = type.DefineField(property.Name, typeof(object), FieldAttributes.Public);
                else
                {
                    var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                    method.GetILGenerator().Emit(OpCodes.Ret);
                    value = method;
                }
                property.SetValue(component, value);
            }
        }
        return channels;
    }

    private static void AssertFrozen(EmittedMessageChannelRuntime channels)
    {
        Assert.True(channels.IsComplete);
        Assert.True(channels.Port.IsComplete);
        Assert.Throws<InvalidOperationException>(channels.CompleteEmission);
        Assert.Throws<InvalidOperationException>(channels.Port.CompleteEmission);
        foreach (var component in new object[] { channels, channels.Port })
        foreach (var property in Handles(component.GetType()))
        {
            var value = property.GetValue(component);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static void Verify(MemoryStream bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"message_channels_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
