using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedEventLoopRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames =>
        new[] { typeof(EmittedEventLoopRuntime), typeof(EmittedHostedEventLoopRuntime) }.SelectMany(type =>
            Handles(type).Select(property => new object[] { type, property.Name }));

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(Type type, string missingHandle)
    {
        var eventLoop = new EmittedEventLoopRuntime();
        eventLoop.BeginHostedEmission();
        object component = type == typeof(EmittedEventLoopRuntime) ? eventLoop : eventLoop.RequireHosted();
        FillDeclarations(eventLoop, type == typeof(EmittedEventLoopRuntime) ? missingHandle : null);
        FillDeclarations(eventLoop.RequireHosted(), type == typeof(EmittedHostedEventLoopRuntime) ? missingHandle : null);
        var property = type.GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(component));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(eventLoop.CompleteEmission).Message);
        Assert.False(eventLoop.IsComplete);
        Assert.False(eventLoop.RequireHosted().IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = Activator.CreateInstance(type, nonPublic: true)!;
        FillDeclarations(declarations);
        property.SetValue(component, property.GetValue(declarations));
        eventLoop.CompleteEmission();
        AssertFrozen(eventLoop);
        AssertFrozen(eventLoop.RequireHosted());
    }

    [Fact]
    public void RequiredComponentAndOptionalHostedHooksCannotBeReplacedOrEnabledAfterCompletion()
    {
        var runtime = new EmittedRuntime();
        var eventLoop = runtime.EventLoop;
        Assert.Same(eventLoop, runtime.EventLoop);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.EventLoop))!.SetMethod);
        Assert.Throws<InvalidOperationException>(() => eventLoop.Type);
        Assert.Null(eventLoop.Hosted);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(eventLoop.RequireHosted).Message);
        FillDeclarations(eventLoop);
        eventLoop.CompleteEmission();
        AssertFrozen(eventLoop);
        Assert.Throws<InvalidOperationException>(eventLoop.BeginHostedEmission);

        eventLoop = new EmittedEventLoopRuntime();
        eventLoop.BeginHostedEmission();
        Assert.Same(eventLoop.Hosted, eventLoop.RequireHosted());
        Assert.Throws<InvalidOperationException>(eventLoop.BeginHostedEmission);
        FillDeclarations(eventLoop);
        FillDeclarations(eventLoop.RequireHosted());
        eventLoop.CompleteEmission();
        AssertFrozen(eventLoop);
        AssertFrozen(eventLoop.RequireHosted());
        Assert.Throws<InvalidOperationException>(eventLoop.BeginHostedEmission);
        Assert.True(typeof(EmittedEventLoopRuntime).GetProperty(nameof(EmittedEventLoopRuntime.Hosted))!.SetMethod!.IsPrivate);
    }

    [Fact]
    public void EarlyEventLoopTypeCanUseATimerDelegateInstalledByALaterType()
    {
        var eventLoop = new EmittedEventLoopRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("eventloop_forward"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        eventLoop.Type = module.DefineType("Loop", TypeAttributes.Public);
        eventLoop.TimerProcessorField = eventLoop.Type.DefineField("TimerProcessor", typeof(Func<int>), FieldAttributes.Public | FieldAttributes.Static);
        eventLoop.PumpOnce = eventLoop.Type.DefineMethod("PumpOnce", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var il = eventLoop.PumpOnce.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, eventLoop.TimerProcessorField);
        il.Emit(OpCodes.Callvirt, typeof(Func<int>).GetMethod("Invoke")!);
        il.Emit(OpCodes.Ret);
        eventLoop.Type.CreateType();
        Assert.False(eventLoop.IsComplete);

        var timers = module.DefineType("Timers", TypeAttributes.Public);
        var tick = timers.DefineMethod("Tick", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var install = timers.DefineMethod("Install", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        il = install.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, tick);
        il.Emit(OpCodes.Newobj, typeof(Func<int>).GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Stsfld, eventLoop.TimerProcessorField);
        il.Emit(OpCodes.Ret);
        tick.GetILGenerator().Emit(OpCodes.Ldc_I4_7);
        tick.GetILGenerator().Emit(OpCodes.Ret);
        timers.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        var loaded = Assembly.Load(stream.ToArray());
        loaded.GetType("Timers")!.GetMethod("Install")!.Invoke(null, null);
        Assert.Equal(7, loaded.GetType("Loop")!.GetMethod("PumpOnce")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("Promise.resolve(1);", false)]
    [InlineData("setTimeout(() => {}, 0);", false)]
    [InlineData("import * as net from 'net';", false)]
    [InlineData("import * as worker from 'worker_threads';", false)]
    [InlineData(null, false)]
    [InlineData("console.log(1);", true)]
    [InlineData("Promise.resolve(1);", true)]
    [InlineData("setTimeout(() => {}, 0);", true)]
    [InlineData("async function* f() { yield await Promise.resolve(1); } f();", true)]
    [InlineData(null, true)]
    public void MinimalFeatureEnabledAndFullEmissionCompleteOnlyTheSelectedHostedSurface(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var eventLoop = runtime.EventLoop;
        AssertFrozen(eventLoop);
        Assert.Equal(hosted, eventLoop.Hosted is not null);
        Assert.Equal("$EventLoop", eventLoop.Type.Name);
        Assert.True(eventLoop.Type.IsSealed);
        Assert.Equal("$EventLoopSyncContext", eventLoop.SyncContextCtor.DeclaringType!.Name);
        Assert.True(eventLoop.TimerProcessorField.IsStatic);
        Assert.True(eventLoop.TimerProcessorField.IsPublic);
        Assert.True(eventLoop.ActiveHandlesField.IsPrivate);
        Assert.True(eventLoop.QueueField.IsPrivate);
        Assert.True(eventLoop.WakeField.IsPrivate);
        AssertCreated(eventLoop);
        if (hosted)
        {
            AssertFrozen(eventLoop.RequireHosted());
            AssertCreated(eventLoop.RequireHosted());
            Assert.True(eventLoop.RequireHosted().RuntimeField.IsPrivate);
            Assert.True(eventLoop.RequireHosted().RuntimeField.IsStatic);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(eventLoop.RequireHosted);
        }

        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var loop = reader.TypeDefinitions.Select(reader.GetTypeDefinition)
            .Single(type => reader.GetString(type.Name) == "$EventLoop");
        var methods = loop.GetMethods().Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "ConfigureHosted", "GetHostedRuntime", "PrepareHostedAwait", "TryRunOne", "HasQueuedCallbacks", "RejectHosted", "ClearHosted" })
            Assert.Equal(hosted, methods.Contains(name));
        var fields = loop.GetFields().Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name)).ToArray();
        Assert.Equal(hosted, fields.Contains("_hostedRuntime"));
        Assert.Equal(hosted, fields.Contains("_hostedAccepting"));
        Assert.Single(fields, name => name == "_timerProcessor");
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynchronizationContextAndLoopShareTheQueueAndPreserveRefAccounting(bool hosted)
    {
        using var stream = Save(EmitRuntime("console.log(1);", hosted));
        var assembly = Assembly.Load(stream.ToArray());
        var type = assembly.GetType("$EventLoop")!;
        var loop = type.GetMethod("GetInstance")!.Invoke(null, null)!;
        object? Call(string name, params object[] args) => type.GetMethod(name)!.Invoke(loop, args);
        Assert.Same(loop, type.GetMethod("GetInstance")!.Invoke(null, null));
        Assert.Equal(false, Call("HasPendingWork"));
        Call("Ref");
        Assert.Equal(true, Call("HasPendingWork"));
        Call("Unref");
        Assert.Equal(false, Call("HasPendingWork"));

        var context = Assert.IsAssignableFrom<SynchronizationContext>(Activator.CreateInstance(assembly.GetType("$EventLoopSyncContext")!));
        Assert.Same(context, context.CreateCopy());
        var seen = new List<string>();
        context.Post(state => seen.Add((string)state!), "post");
        context.Send(state => seen.Add((string)state!), "send");
        Assert.Empty(seen);
        Assert.Equal(true, Call("HasPendingWork"));
        Assert.Equal(0, Call("PumpOnce"));
        Assert.Equal(new[] { "post", "send" }, seen);
        Assert.Equal(false, Call("HasPendingWork"));

        var completion = new TaskCompletionSource();
        Call("Schedule", (Action)(() => completion.SetResult()));
        Assert.Equal(true, Call("WaitForTask", completion.Task));
        Assert.True(completion.Task.IsCompletedSuccessfully);
        Call("Schedule", (Action)(() => seen.Add("run")));
        Call("Run");
        Assert.Equal(new[] { "post", "send", "run" }, seen);
        Assert.Equal(false, Call("HasPendingWork"));
    }

    [Fact]
    public void HostedHooksKeepFallbackQueueDrainAndClearSemantics()
    {
        using var stream = Save(EmitRuntime("console.log(1);", hosted: true));
        var type = Assembly.Load(stream.ToArray()).GetType("$EventLoop")!;
        var loop = type.GetMethod("GetInstance")!.Invoke(null, null)!;
        object? Call(string name, params object?[] args) => type.GetMethod(name)!.Invoke(loop, args);
        Assert.Null(Call("GetHostedRuntime"));
        var task = Task.FromResult<object>(17);
        Assert.Same(task, Call("PrepareHostedAwait", task));
        Call("ConfigureHosted", new object?[] { null });
        var accepting = type.GetField("_hostedAccepting", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(true, accepting.GetValue(null));
        Call("RejectHosted");
        Assert.Equal(false, accepting.GetValue(null));
        int count = 0;
        Call("Schedule", (Action)(() => count++));
        Call("Schedule", (Action)(() => count++));
        Assert.Equal(true, Call("HasQueuedCallbacks"));
        Assert.Equal(true, Call("TryRunOne"));
        Assert.Equal(1, count);
        Call("Ref");
        Call("ClearHosted");
        Assert.Equal(false, Call("HasQueuedCallbacks"));
        Assert.Equal(false, Call("TryRunOne"));
        Assert.Equal(false, Call("HasPendingWork"));
        Assert.Equal(1, count);
    }

    private static void FillDeclarations(object component, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"eventloop_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles(component.GetType()).Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(component, handle);
        }
    }

    private static void AssertFrozen(object component)
    {
        if (component is EmittedEventLoopRuntime eventLoop)
        {
            Assert.True(eventLoop.IsComplete);
            Assert.Throws<InvalidOperationException>(eventLoop.CompleteEmission);
        }
        else
        {
            var hosted = Assert.IsType<EmittedHostedEventLoopRuntime>(component);
            Assert.True(hosted.IsComplete);
            Assert.Throws<InvalidOperationException>(hosted.CompleteEmission);
        }
        foreach (var property in Handles(component.GetType()))
        {
            var value = property.GetValue(component);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static void AssertCreated(object component)
    {
        foreach (var property in Handles(component.GetType()))
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(component));
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
        }
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(stream);
        stream.Position = 0;
        return stream;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"eventloop_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
