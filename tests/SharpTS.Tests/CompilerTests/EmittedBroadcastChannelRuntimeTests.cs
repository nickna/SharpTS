using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedBroadcastChannelRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedBroadcastChannelRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRepair(string missingHandle)
    {
        var channel = CreateDeclarations(missingHandle);
        var property = typeof(EmittedBroadcastChannelRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(channel));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(channel.CompleteEmission).Message);
        Assert.False(channel.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(channel, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(channel, property.GetValue(CreateDeclarations()));
        channel.CompleteEmission();
        AssertFrozen(channel);
    }

    [Fact]
    public void OptionalOwnerHasCheckedAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.BroadcastChannel);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireBroadcastChannel).Message);
        runtime.BeginBroadcastChannelEmission();
        Assert.Same(runtime.BroadcastChannel, runtime.RequireBroadcastChannel());
        Assert.Throws<InvalidOperationException>(runtime.BeginBroadcastChannelEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.BroadcastChannel))!.SetMethod!.IsPrivate);
        var declarations = CreateDeclarations();
        foreach (var property in Handles) property.SetValue(runtime.RequireBroadcastChannel(), property.GetValue(declarations));
        runtime.RequireBroadcastChannel().CompleteEmission();
        AssertFrozen(runtime.RequireBroadcastChannel());
        Assert.Throws<InvalidOperationException>(runtime.BeginBroadcastChannelEmission);
    }

    [Fact]
    public void ConstructorCanReadForwardDeclarationsBeforeOwnerAndTypeCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("broadcast_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, Detect("const value=1;"));
        var type = module.DefineType("StagedBroadcast", TypeAttributes.Public, runtime.EventEmitter.Type);
        var channel = new EmittedBroadcastChannelRuntime
        {
            Type = type,
            Registry = type.DefineField("_registry", typeof(ConcurrentDictionary<string, object>), FieldAttributes.Private | FieldAttributes.Static),
            NextId = type.DefineField("_nextId", typeof(long), FieldAttributes.Private | FieldAttributes.Static),
            Name = type.DefineField("_name", typeof(string), FieldAttributes.Private),
            Id = type.DefineField("_id", typeof(long), FieldAttributes.Private),
            Pending = type.DefineField("_pending", typeof(ConcurrentQueue<object>), FieldAttributes.Private),
            Refed = type.DefineField("_refed", typeof(bool), FieldAttributes.Private)
        };
        typeof(RuntimeEmitter).GetMethod("EmitBroadcastChannelConstructor", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [type, channel, runtime.EventEmitter, runtime.EventLoop]);
        Assert.True(channel.Ctor.GetILGenerator().ILOffset > 0);
        Assert.Same(type, channel.Ctor.DeclaringType);
        Assert.False(type.IsCreated());
        Assert.False(channel.IsComplete);
        Assert.Contains("CloneError", Assert.Throws<InvalidOperationException>(channel.CompleteEmission).Message);
        Assert.False(channel.IsComplete);
        type.CreateType();
        using var bytes = Save(runtime);
        Verify(bytes);
    }

    [Theory]
    [InlineData("const value=1;", false, false)]
    [InlineData("const value=1;", true, false)]
    [InlineData("new BroadcastChannel('name');", false, true)]
    [InlineData("new BroadcastChannel('name');", true, true)]
    [InlineData("import {BroadcastChannel} from 'worker_threads';", false, true)]
    [InlineData("import * as workers from 'node:worker_threads';", false, true)]
    [InlineData("const workers=require('worker_threads');", false, true)]
    [InlineData("import {Worker} from 'worker_threads';", false, true)]
    [InlineData("new MessageChannel();", false, false)]
    [InlineData("new Uint8Array(2);", false, false)]
    [InlineData(null, false, true)]
    [InlineData(null, true, true)]
    public void AvailabilityPreservesTheExistingFeatureGateAndAssemblyDependencies(string? source, bool hosted, bool enabled)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.BroadcastChannel is not null);
        if (enabled) AssertFrozen(runtime.RequireBroadcastChannel());
        else Assert.Throws<InvalidOperationException>(runtime.RequireBroadcastChannel);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var names = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        Assert.Equal(enabled, names.Contains("$BroadcastChannel"));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        if (!enabled) return;
        var type = Assembly.Load(bytes.ToArray()).GetType("$BroadcastChannel")!;
        Assert.Equal("$EventEmitter", type.BaseType!.Name);
        Assert.NotNull(type.GetConstructor([typeof(string)]));
        Assert.Equal(typeof(void), type.GetMethod("PostMessage")!.ReturnType);
        Assert.Equal(typeof(object), Assert.Single(type.GetMethod("PostMessage")!.GetParameters()).ParameterType);
        Assert.Equal(typeof(string), type.GetProperty("Name")!.PropertyType);
        Assert.Equal(typeof(ConcurrentDictionary<string, object>), Field(type, "_registry").FieldType);
        Assert.True(Field(type, "_registry").IsStatic && Field(type, "_registry").IsPrivate);
        Assert.True(Field(type, "_cloneError").IsStatic && Field(type, "_cloneError").IsInitOnly);
        foreach (var name in new[] { "Close", "Ref", "Unref", "Drain" })
            Assert.Empty(type.GetMethod(name)!.GetParameters());
    }

    [Fact]
    public void SavedRuntimePreservesOrdinalRegistryClonesReferencesAndQueuedDeliveryAfterClose()
    {
        var runtime = EmitRuntime("new BroadcastChannel('name');", false);
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$BroadcastChannel")!;
        var registry = Assert.IsType<ConcurrentDictionary<string, object>>(Field(type, "_registry").GetValue(null));
        Assert.Same(StringComparer.Ordinal, registry.Comparer);
        Assert.Empty(registry);
        var a = Activator.CreateInstance(type, ["topic"])!;
        var b = Activator.CreateInstance(type, ["topic"])!;
        var c = Activator.CreateInstance(type, ["topic"])!;
        var other = Activator.CreateInstance(type, ["Topic"])!;
        var peers = Assert.IsType<ConcurrentDictionary<long, object>>(registry["topic"]);
        Assert.Equal(3, peers.Count);
        Assert.Equal(2, registry.Count);
        Assert.Equal(4L, Field(type, "_nextId").GetValue(null));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, new[] { a, b, c, other }.Select(value => (long)Field(type, "_id").GetValue(value)!));
        var loopType = assembly.GetType("$EventLoop")!;
        var loop = loopType.GetMethod("GetInstance")!.Invoke(null, null);
        int References() => (int)Field(loopType, runtime.EventLoop.ActiveHandlesField.Name).GetValue(loop)!;
        Assert.Equal(4, References());
        Call(b, "Unref"); Call(b, "Unref");
        Assert.Equal(3, References());
        Call(b, "Ref"); Call(b, "Ref");
        Assert.Equal(4, References());
        var payload = new Dictionary<string, object> { ["value"] = 7d };
        Call(a, "PostMessage", payload);
        payload["value"] = 9d;
        Assert.Empty(Queue(a));
        Assert.Empty(Queue(other));
        Assert.True(Queue(b).TryDequeue(out var first));
        Assert.True(Queue(c).TryDequeue(out var second));
        var firstCopy = Assert.IsType<Dictionary<string, object>>(first);
        var secondCopy = Assert.IsType<Dictionary<string, object>>(second);
        Assert.NotSame(firstCopy, secondCopy);
        firstCopy["value"] = 11d;
        Assert.Equal(7d, secondCopy["value"]);
        Call(a, "PostMessage", "queued");
        Call(b, "Close"); Call(b, "Close");
        Assert.Equal(3, References());
        Assert.Equal(2, peers.Count);
        Assert.Single(Queue(b));
        Call(a, "PostMessage", "late");
        Assert.Single(Queue(b));
        Assert.Equal(2, Queue(c).Count);
        var error = Assert.Throws<TargetInvocationException>(() => Call(b, "PostMessage", "invalid"));
        Assert.Equal("InvalidStateError: BroadcastChannel is closed", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        // Close preserves already queued deliveries, and the clone-failure sentinel remains drainable.
        Queue(b).Enqueue(Field(type, "_cloneError").GetValue(null)!);
        Call(b, "Drain");
        Assert.Empty(Queue(b));
        foreach (var channel in new[] { a, c, other }) Call(channel, "Close");
        Assert.Equal(0, References());
        Assert.Empty(peers);
        Call(c, "Drain");
        Assert.Empty(Queue(c));
    }

    [Fact]
    public void ReusingEmitterAcrossEnabledAndDisabledFeaturesKeepsRegistriesAndSentinelsIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("new BroadcastChannel('name');", false, emitter);
        var absent = EmitRuntime("const value=1;", false, emitter);
        var second = EmitRuntime("new BroadcastChannel('name');", false, emitter);
        Assert.Null(absent.BroadcastChannel);
        Assert.NotSame(first.RequireBroadcastChannel(), second.RequireBroadcastChannel());
        foreach (var property in Handles)
            Assert.NotSame(property.GetValue(first.RequireBroadcastChannel()), property.GetValue(second.RequireBroadcastChannel()));
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        var firstType = Assembly.Load(firstBytes.ToArray()).GetType("$BroadcastChannel")!;
        var secondType = Assembly.Load(secondBytes.ToArray()).GetType("$BroadcastChannel")!;
        Assert.NotSame(Field(firstType, "_registry").GetValue(null), Field(secondType, "_registry").GetValue(null));
        Assert.NotSame(Field(firstType, "_cloneError").GetValue(null), Field(secondType, "_cloneError").GetValue(null));
        var sender = Activator.CreateInstance(firstType, ["same-name"])!;
        var unrelated = Activator.CreateInstance(secondType, ["same-name"])!;
        Call(sender, "PostMessage", "value");
        Assert.Empty(Queue(unrelated));
        Assert.Equal(1L, Field(firstType, "_id").GetValue(sender));
        Assert.Equal(1L, Field(secondType, "_id").GetValue(unrelated));
        Call(sender, "Close"); Call(unrelated, "Close");
    }

    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name)!.Invoke(target, args);

    private static FieldInfo Field(Type type, string name) => type.GetField(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!;

    private static ConcurrentQueue<object> Queue(object channel) =>
        Assert.IsType<ConcurrentQueue<object>>(Field(channel.GetType(), "_pending").GetValue(channel));

    private static EmittedBroadcastChannelRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"broadcast_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("BroadcastDeclarations", TypeAttributes.Public);
        var channel = new EmittedBroadcastChannelRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omitted) continue;
            object member;
            if (property.PropertyType == typeof(TypeBuilder)) member = type;
            else if (property.PropertyType == typeof(ConstructorBuilder)) member = type.DefineDefaultConstructor(MethodAttributes.Public);
            else if (property.PropertyType == typeof(FieldBuilder)) member = type.DefineField(property.Name, typeof(object), FieldAttributes.Public);
            else
            {
                var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                method.GetILGenerator().Emit(OpCodes.Ret);
                member = method;
            }
            property.SetValue(channel, member);
        }
        return channel;
    }

    private static void AssertFrozen(EmittedBroadcastChannelRuntime channel)
    {
        Assert.True(channel.IsComplete);
        Assert.Throws<InvalidOperationException>(channel.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(channel);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(channel, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"broadcast_channels_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
