using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;
using static SharpTS.Tests.CompilerTests.RuntimeEmissionTestHelpers;

namespace SharpTS.Tests.CompilerTests;

public class EmittedEventEmitterRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedEventEmitterRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Fact]
    public void ComponentIsRequiredAndHasNoReplacementSetter()
    {
        var runtime = new EmittedRuntime();
        Assert.NotNull(runtime.EventEmitter);
        Assert.Same(runtime.EventEmitter, runtime.EventEmitter);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.EventEmitter))!.SetMethod);
        Assert.False(runtime.EventEmitter.IsComplete);
        Assert.Contains("'Type'", Assert.Throws<InvalidOperationException>(() => runtime.EventEmitter.Type).Message);
        Assert.Contains("'Type'", Assert.Throws<InvalidOperationException>(runtime.EventEmitter.CompleteEmission).Message);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var events = CreateDeclarations(missingHandle);
        var property = typeof(EmittedEventEmitterRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(events));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(events.CompleteEmission).Message);
        Assert.False(events.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(events, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declaration = property.GetValue(CreateDeclarations());
        property.SetValue(events, declaration);
        foreach (var declared in Handles)
        {
            var value = declared.GetValue(events);
            var duplicate = Assert.Throws<TargetInvocationException>(() => declared.SetValue(events, value));
            Assert.Contains($"'{declared.Name}'", Assert.IsType<InvalidOperationException>(duplicate.InnerException).Message);
            Assert.Same(value, declared.GetValue(events));
        }
        Assert.Same(declaration, property.GetValue(events));
        events.CompleteEmission();
        AssertFrozen(events);
    }

    [Fact]
    public void RejectionRoutingAndEmitSupportMutualForwardCalls()
    {
        var events = new EmittedEventEmitterRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("eventemitter_forward"), typeof(object).Assembly);
        events.Type = assembly.DefineDynamicModule("main").DefineType("Emitter", TypeAttributes.Public);
        events.Ctor = events.Type.DefineDefaultConstructor(MethodAttributes.Public);
        events.RouteCaptureRejection = events.Type.DefineMethod("RouteCaptureRejection", MethodAttributes.Private,
            typeof(void), [typeof(object)]);
        events.Emit = events.Type.DefineMethod("Emit", MethodAttributes.Public, typeof(bool), [typeof(string), typeof(object[])]);

        var il = events.Emit.GetILGenerator();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "start");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, events.RouteCaptureRejection);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        Assert.False(events.Type.IsCreated());
        Assert.False(events.IsComplete);

        il = events.RouteCaptureRejection.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Callvirt, events.Emit);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        events.Type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        var loaded = Assembly.Load(stream.ToArray()).GetType("Emitter")!;
        Assert.Equal(true, loaded.GetMethod("Emit")!.Invoke(Activator.CreateInstance(loaded), ["start", Array.Empty<object>()]));
    }

    [Theory]
    [InlineData("console.log(1);", false, false)]
    [InlineData("console.log(1);", true, true)]
    [InlineData("import { EventEmitter } from 'events'; new EventEmitter();", false, false)]
    [InlineData("import { EventEmitter } from 'events'; new EventEmitter({ captureRejections: true });", false, false)]
    [InlineData("Promise.resolve(1);", false, true)]
    [InlineData("async function f() { return 1; } f();", false, true)]
    [InlineData("Promise.resolve(1);", true, true)]
    [InlineData(null, false, true)]
    public void MinimalEnabledHostedAndFullEmissionCompleteEveryDeclaration(string? source, bool hosted, bool hasPromise)
    {
        var runtime = EmitRuntime(source, hosted);
        var events = runtime.EventEmitter;
        AssertFrozen(events);
        Assert.Equal(hasPromise, runtime.Promise is not null);
        Assert.Equal("$EventEmitter", events.Type.Name);
        Assert.Equal("$ListenerWrapper", events.ListenerWrapperType.Name);
        Assert.False(events.Type.IsSealed);
        Assert.True(events.ListenerWrapperType.IsSealed);
        Assert.True(events.OnListenerAdded.IsVirtual);
        Assert.True(events.RouteCaptureRejection.IsPrivate);
        Assert.True(events.EventsField.IsPrivate);
        Assert.True(events.CaptureRejectionsField.IsPrivate);
        Assert.True(events.MaxListenersField.IsPrivate);
        Assert.True(events.DefaultMaxListeners.IsStatic);
        foreach (var property in Handles)
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(events));
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
        }
        using var stream = Save(runtime);
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var reader = pe.GetMetadataReader();
        Assert.Equal(hasPromise, reader.TypeDefinitions.Any(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "$Promise"));
        var loaded = Assembly.Load(stream.ToArray());
        Assert.Equal(10, loaded.GetType("$EventEmitter")!.GetField("DefaultMaxListeners")!.GetValue(null));
    }

    [Theory]
    [InlineData("import * as stream from 'stream';", "$Readable")]
    [InlineData("import * as stream from 'stream';", "$Writable")]
    [InlineData("import * as net from 'net';", "$NetSocket")]
    [InlineData("import * as net from 'net';", "$NetServer")]
    [InlineData("import * as dgram from 'dgram';", "$DatagramSocket")]
    [InlineData("import * as http from 'http';", "$HttpServer")]
    [InlineData("import * as tls from 'tls';", "$TlsSocket")]
    [InlineData("import * as worker from 'worker_threads';", "$MessagePort")]
    [InlineData("new BroadcastChannel('events');", "$BroadcastChannel")]
    public void DependentTypesKeepTheOwnedEventEmitterBase(string source, string typeName)
    {
        var runtime = EmitRuntime(source);
        using var stream = Save(runtime);
        var loaded = Assembly.Load(stream.ToArray());
        var baseType = loaded.GetType(runtime.EventEmitter.Type.Name)!;
        var derived = loaded.GetType(typeName, throwOnError: true)!;
        Assert.True(derived.IsSubclassOf(baseType));
        Assert.Same(baseType, derived.GetMethod("On")!.DeclaringType);
        Assert.Same(baseType, derived.GetMethod("Emit")!.DeclaringType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsListenerCollectionsAndDispatchWithinEachAssembly(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedEventEmitterRuntime>();
        var saved = new List<(Assembly Assembly, object Instance, object Listener, object Once, List<object> Seen, List<object> OnceSeen, int Max)>();
        foreach (string? source in new[]
        {
            "import * as events from 'events';", "console.log(1);", "import * as stream from 'stream';",
            "import * as net from 'net'; import * as tls from 'tls';",
            null, "console.log(1);", "import * as events from 'events'; Promise.resolve(1);"
        })
        {
            var builder = new PersistedAssemblyBuilder(new AssemblyName($"eventemitter_reuse_{Guid.NewGuid():N}"), typeof(object).Assembly);
            var module = builder.DefineDynamicModule("main");
            var features = source is null ? null : new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = features is null ? emitter.EmitAll(module) : emitter.EmitAll(module, features);
            using var bytes = new MemoryStream();
            builder.Save(bytes);
            bytes.Position = 0;
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(bytes));
            var assembly = Assembly.Load(bytes.ToArray());
            var references = assembly.GetReferencedAssemblies();
            Assert.DoesNotContain(references, reference => reference.Name == "SharpTS");
            Assert.DoesNotContain(references, reference => saved.Any(previous => previous.Assembly.GetName().Name == reference.Name));
            Assert.Equal(hosted, references.Any(reference => reference.Name == "SharpTS.Hosting.Abstractions"));
            Assert.True(owners.Add(runtime.EventEmitter));
            Assert.True(runtime.EventEmitter.IsComplete);
            foreach (var property in Handles)
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.EventEmitter)).Module.Assembly);
            if (source == "console.log(1);")
                Assert.Null(assembly.GetType("$Readable"));

            var type = assembly.GetType("$EventEmitter")!;
            var instance = Activator.CreateInstance(type)!;
            var seen = new List<object>();
            var onceSeen = new List<object>();
            object Listener(List<object> values) => Activator.CreateInstance(assembly.GetType("$TSFunction")!,
                [values, typeof(List<object>).GetMethod(nameof(List<object>.Add))!])!;
            var listener = Listener(seen);
            var once = Listener(onceSeen);
            Assert.Same(instance, type.GetMethod("On")!.Invoke(instance, ["tick", listener]));
            Assert.Same(instance, type.GetMethod("PrependOnceListener")!.Invoke(instance, ["tick", once]));
            var defaultMax = type.GetField("DefaultMaxListeners")!;
            Assert.Equal(10, defaultMax.GetValue(null));
            int max = 20 + saved.Count;
            defaultMax.SetValue(null, max);
            saved.Add((assembly, instance, listener, once, seen, onceSeen, max));
        }

        // Earlier listener collections, callbacks and static defaults survive later emissions.
        foreach (var (assembly, instance, listener, once, seen, onceSeen, max) in saved)
        {
            var type = assembly.GetType("$EventEmitter")!;
            object? Call(string method, params object?[] arguments) => type.GetMethod(method)!.Invoke(instance, arguments);
            List<object> Values(string method, params object?[] arguments)
                => Assert.IsAssignableFrom<List<object>>(Call(method, arguments));
            Assert.Equal((double)max, Call("GetMaxListeners"));
            Assert.Equal(new[] { once, listener }, Values("Listeners", "tick"));
            Assert.Equal(new object[] { "tick" }, Values("EventNames"));
            Assert.Equal(2d, Call("ListenerCount", "tick"));
            Assert.Equal(true, Call("Emit", "tick", new object[] { 3d }));
            Assert.Equal(true, Call("Emit", "tick", new object[] { 4d }));
            Assert.Equal(new object[] { 3d, 4d }, seen);
            Assert.Equal(new object[] { 3d }, onceSeen);
            Assert.Equal(new[] { listener }, Values("Listeners", "tick"));
            Assert.Equal(1d, Call("ListenerCount", "tick"));
            Call("On", "other", listener);
            Assert.Equal(new object[] { "tick", "other" }, Values("EventNames"));
            Call("Off", "tick", listener);
            Assert.Equal(0d, Call("ListenerCount", "tick"));
            Assert.Equal(false, Call("Emit", "tick", new object[] { 5d }));
            Assert.Empty(Values("Listeners", "tick"));
            Call("RemoveAllListeners", "other");
            Assert.Empty(Values("EventNames"));
            Call("On", "all", listener);
            Call("RemoveAllListeners", new object?[] { null });
            Assert.Empty(Values("EventNames"));
        }
    }

    private static EmittedEventEmitterRuntime CreateDeclarations(string? missingHandle = null)
    {
        var events = new EmittedEventEmitterRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"eventemitter_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(events, handle);
        }
        return events;
    }

    private static void AssertFrozen(EmittedEventEmitterRuntime events)
    {
        Assert.True(events.IsComplete);
        Assert.Throws<InvalidOperationException>(events.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(events);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(events, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"eventemitter_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
