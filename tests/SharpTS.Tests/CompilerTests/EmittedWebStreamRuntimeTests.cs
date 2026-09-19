using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedWebStreamRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedWebStreamRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    private static readonly string[] SharedFields =
    [
        nameof(EmittedWebStreamRuntime.ReadableLockedField), nameof(EmittedWebStreamRuntime.ReadableReaderField),
        nameof(EmittedWebStreamRuntime.WritableStateField), nameof(EmittedWebStreamRuntime.WritableStoredErrorField),
        nameof(EmittedWebStreamRuntime.WritableWriterField), nameof(EmittedWebStreamRuntime.WritableHwmField)
    ];

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var streams = new EmittedWebStreamRuntime();
        FillDeclarations(streams, missingHandle);
        var property = typeof(EmittedWebStreamRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(streams));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(streams.CompleteEmission).Message);
        Assert.False(streams.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(streams, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = new EmittedWebStreamRuntime();
        FillDeclarations(declarations);
        var repaired = property.GetValue(declarations);
        property.SetValue(streams, repaired);
        var replacement = new EmittedWebStreamRuntime();
        FillDeclarations(replacement);
        var duplicate = Assert.Throws<TargetInvocationException>(() => property.SetValue(streams, property.GetValue(replacement)));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(duplicate.InnerException).Message);
        Assert.Same(repaired, property.GetValue(streams));
        streams.CompleteEmission();
        AssertFrozen(streams);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.WebStreams);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireWebStreams).Message);
        runtime.BeginWebStreamEmission();
        var streams = runtime.RequireWebStreams();
        Assert.Same(runtime.WebStreams, streams);
        Assert.Throws<InvalidOperationException>(runtime.BeginWebStreamEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.WebStreams))!.SetMethod!.IsPrivate);
        FillDeclarations(streams);
        streams.CompleteEmission();
        AssertFrozen(streams);
        Assert.Throws<InvalidOperationException>(runtime.BeginWebStreamEmission);
    }

    [Fact]
    public void ControllerCanCallAStreamDeclarationBeforeItsBodyExists()
    {
        var streams = new EmittedWebStreamRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("web_stream_forward"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        streams.ReadableType = module.DefineType("Readable", TypeAttributes.Public);
        streams.ReadableEnqueue = streams.ReadableType.DefineMethod("Enqueue", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var controller = module.DefineType("Controller", TypeAttributes.Public);
        var forward = controller.DefineMethod("Enqueue", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var il = forward.GetILGenerator();
        il.Emit(OpCodes.Call, streams.ReadableEnqueue);
        il.Emit(OpCodes.Ret);
        controller.CreateType();
        Assert.False(streams.IsComplete);
        il = streams.ReadableEnqueue.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Ret);
        streams.ReadableType.CreateType();
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        var loaded = Assembly.Load(bytes.ToArray());
        Assert.Equal(7, loaded.GetType("Controller")!.GetMethod("Enqueue")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false, false)]
    [InlineData("Promise.resolve(1);", false, false)]
    [InlineData("new AbortController();", false, false)]
    [InlineData("import * as stream from 'stream';", false, false)]
    [InlineData("import * as fs from 'fs';", false, false)]
    [InlineData("new ReadableStream();", true, false)]
    [InlineData("new WritableStream();", true, false)]
    [InlineData("new TransformStream();", true, false)]
    // Bare strategy names currently do not select UsesWebStreams; preserve that
    // detector boundary. Imports or another Web stream reference select the group.
    [InlineData("new CountQueuingStrategy({ highWaterMark: 2 });", false, false)]
    [InlineData("new ByteLengthQueuingStrategy({ highWaterMark: 8 });", false, false)]
    [InlineData("import { CountQueuingStrategy } from 'stream/web'; new CountQueuingStrategy({ highWaterMark: 2 });", true, false)]
    [InlineData("new ByteLengthQueuingStrategy({ highWaterMark: 8 }); new ReadableStream();", true, false)]
    [InlineData("globalThis.ReadableStream;", true, false)]
    [InlineData("import * as stream from 'stream/web';", true, false)]
    [InlineData("import * as stream from 'node:stream/web';", true, false)]
    [InlineData("import * as consumers from 'stream/consumers';", true, false)]
    [InlineData("console.log(1);", false, true)]
    [InlineData("new ReadableStream();", true, true)]
    [InlineData("import * as stream from 'stream/web';", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void MinimalEnabledImpliedHostedAndFullEmissionPreserveFeatureSelection(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.WebStreams is not null);
        if (enabled)
        {
            var streams = runtime.RequireWebStreams();
            AssertFrozen(streams);
            Assert.NotNull(runtime.Promise);
            Assert.NotNull(runtime.RequireAbort().SignalGetAborted);
            Assert.Equal("$ReadableStream", streams.ReadableType.Name);
            Assert.Equal("$WritableStream", streams.WritableType.Name);
            Assert.Equal("$TransformStream", streams.TransformType.Name);
            Assert.Same(streams.ReadableType, streams.ReadableQueueField.DeclaringType);
            Assert.Same(streams.WritableType, streams.WritableWriterField.DeclaringType);
            Assert.Same(streams.ReadableType, streams.TransformReadableField.FieldType);
            Assert.Same(streams.WritableType, streams.TransformWritableField.FieldType);
            foreach (var property in Handles)
            {
                var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(streams));
                Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
                if (handle is FieldBuilder field)
                {
                    Assert.Equal(SharedFields.Contains(property.Name), field.IsAssembly);
                    Assert.Equal(!SharedFields.Contains(property.Name), field.IsPrivate);
                }
            }
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireWebStreams);

        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "$ReadableStream", "$WritableStream", "$TransformStream", "$ReadableStreamDefaultReader", "$ReadableStreamDefaultController", "$WritableStreamDefaultWriter", "$WritableStreamDefaultController", "$TransformSinkHolder", "$CountQueuingStrategy", "$ByteLengthQueuingStrategy" })
            Assert.Equal(enabled, types.Contains(name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PeerReaderWriterAndControllerAccessesVerifyAndExecute(bool hosted)
    {
        using var bytes = Save(EmitRuntime("new TransformStream();", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var readableType = assembly.GetType("$ReadableStream")!;
        var readable = readableType.GetConstructor([typeof(object), typeof(object)])!.Invoke([null, null]);
        object GetReader() => readableType.GetMethod("GetReader")!.Invoke(readable, null)!;
        var firstReader = GetReader();
        Assert.Equal(true, readableType.GetProperty("Locked")!.GetValue(readable));
        firstReader.GetType().GetMethod("ReleaseLock")!.Invoke(firstReader, null);
        Assert.Equal(false, readableType.GetProperty("Locked")!.GetValue(readable));
        Assert.NotSame(firstReader, GetReader());

        var writableType = assembly.GetType("$WritableStream")!;
        var strategy = new Dictionary<string, object?> { ["highWaterMark"] = 5.0 };
        var writable = writableType.GetConstructor([typeof(object), typeof(object)])!.Invoke([null, strategy]);
        object GetWriter() => writableType.GetMethod("GetWriter")!.Invoke(writable, null)!;
        var firstWriter = GetWriter();
        Assert.Equal(5.0, firstWriter.GetType().GetProperty("DesiredSize")!.GetValue(firstWriter));
        firstWriter.GetType().GetMethod("ReleaseLock")!.Invoke(firstWriter, null);
        Assert.Equal(false, writableType.GetProperty("Locked")!.GetValue(writable));
        Assert.NotSame(firstWriter, GetWriter());

        var controllerType = assembly.GetType("$WritableStreamDefaultController")!;
        var controller = controllerType.GetConstructor([writableType])!.Invoke([writable]);
        controllerType.GetMethod("Error")!.Invoke(controller, ["broken"]);
        Assert.Equal(2, writableType.GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(writable));
        Assert.Equal("broken", writableType.GetField("_storedError", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(writable));
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("new ReadableStream();", false, emitter).RequireWebStreams();
        var minimal = EmitRuntime("console.log(1);", false, emitter);
        var second = EmitRuntime("new ReadableStream();", false, emitter).RequireWebStreams();
        Assert.Null(minimal.WebStreams);
        Assert.NotSame(first, second);
        Assert.NotSame(first.ReadableType, second.ReadableType);
        Assert.NotSame(first.ReadableQueueField, second.ReadableQueueField);
        AssertFrozen(first);
        AssertFrozen(second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsQueuesAndPendingReadsWithinEachAssembly(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedWebStreamRuntime>();
        var assemblies = new List<Assembly>();
        var completions = new List<Action>();
        foreach (string? source in new[]
        {
            "console.log(1);", "new ReadableStream();", "import * as stream from 'node:stream/web';",
            "new WritableStream();", null, "console.log(1);", "new TransformStream();"
        })
        {
            var runtime = EmitRuntime(source, hosted, emitter);
            var builder = runtime.RuntimeClass.Type.Assembly;
            if (runtime.WebStreams is { } streams)
            {
                Assert.True(owners.Add(streams));
                AssertFrozen(streams);
                foreach (var property in Handles)
                    Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(streams)).Module.Assembly);
            }
            using var bytes = Save(runtime);
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(bytes));
            var assembly = Assembly.Load(bytes.ToArray());
            var references = assembly.GetReferencedAssemblies();
            Assert.DoesNotContain(references, reference => reference.Name == "SharpTS");
            Assert.DoesNotContain(references, reference => assemblies.Any(previous => previous.GetName().Name == reference.Name));
            Assert.Equal(hosted, references.Any(reference => reference.Name == "SharpTS.Hosting.Abstractions"));
            assemblies.Add(assembly);
            if (source == "console.log(1);")
            {
                Assert.Null(runtime.WebStreams);
                Assert.Null(assembly.GetType("$ReadableStream"));
                continue;
            }

            Assert.NotNull(runtime.WebStreams);
            var type = assembly.GetType("$ReadableStream")!;
            var ctor = type.GetConstructor([typeof(object), typeof(object)])!;
            var read = type.GetMethod("Read")!;
            var enqueue = type.GetMethod("Enqueue")!;
            string marker = $"assembly-{assemblies.Count}";
            var buffered = ctor.Invoke([null, null]);
            enqueue.Invoke(buffered, [marker]);
            completions.Add(() =>
            {
                var task = Assert.IsAssignableFrom<Task<object>>(read.Invoke(buffered, null));
                Assert.True(task.IsCompletedSuccessfully);
                var result = Assert.IsType<Dictionary<string, object?>>(task.Result);
                Assert.Equal(false, result["done"]);
                Assert.Equal(marker, result["value"]);
                var cancelled = Assert.IsAssignableFrom<Task<object>>(type.GetMethod("Cancel")!.Invoke(buffered, [null]));
                Assert.True(cancelled.IsCompletedSuccessfully);
                var eof = Assert.IsAssignableFrom<Task<object>>(read.Invoke(buffered, null));
                Assert.True(eof.IsCompletedSuccessfully);
                Assert.Equal(true, Assert.IsType<Dictionary<string, object?>>(eof.Result)["done"]);
            });

            foreach (string operation in new[] { "Enqueue", "CloseStream", "ErrorStream" })
            {
                var stream = ctor.Invoke([null, null]);
                var first = Assert.IsAssignableFrom<Task<object>>(read.Invoke(stream, null));
                var second = Assert.IsAssignableFrom<Task<object>>(read.Invoke(stream, null));
                Assert.False(first.IsCompleted);
                Assert.False(second.IsCompleted);
                completions.Add(() =>
                {
                    Assert.False(first.IsCompleted);
                    Assert.False(second.IsCompleted);
                    if (operation == "Enqueue")
                    {
                        enqueue.Invoke(stream, [marker + "-first"]);
                        Assert.True(first.IsCompletedSuccessfully);
                        Assert.False(second.IsCompleted);
                        enqueue.Invoke(stream, [marker + "-second"]);
                        Assert.True(second.IsCompletedSuccessfully);
                        Assert.Equal(marker + "-first", Assert.IsType<Dictionary<string, object?>>(first.Result)["value"]);
                        Assert.Equal(marker + "-second", Assert.IsType<Dictionary<string, object?>>(second.Result)["value"]);
                    }
                    else if (operation == "CloseStream")
                    {
                        type.GetMethod(operation)!.Invoke(stream, null);
                        var sentinel = runtime.Sentinels.UndefinedInstance;
                        var undefined = assembly.GetType(sentinel.DeclaringType!.Name)!
                            .GetField(sentinel.Name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null);
                        foreach (var task in new[] { first, second })
                        {
                            Assert.True(task.IsCompletedSuccessfully);
                            var result = Assert.IsType<Dictionary<string, object?>>(task.Result);
                            Assert.Equal(true, result["done"]);
                            Assert.Same(undefined, result["value"]);
                        }
                    }
                    else
                    {
                        type.GetMethod(operation)!.Invoke(stream, [marker]);
                        foreach (var task in new[] { first, second })
                        {
                            Assert.True(task.IsFaulted);
                            Assert.Equal(marker, Assert.Single(task.Exception!.InnerExceptions).Message);
                        }
                    }
                });
            }
        }
        foreach (var complete in completions)
            complete();
    }

    private static void FillDeclarations(EmittedWebStreamRuntime streams, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"web_stream_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
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
            property.SetValue(streams, handle);
        }
    }

    private static void AssertFrozen(EmittedWebStreamRuntime streams)
    {
        Assert.True(streams.IsComplete);
        Assert.Throws<InvalidOperationException>(streams.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(streams);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(streams, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"web_stream_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
