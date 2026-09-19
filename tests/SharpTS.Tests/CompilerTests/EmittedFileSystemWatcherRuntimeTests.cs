using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedFileSystemWatcherRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedFileSystemWatcherRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var fileSystem = new EmittedFileSystemWatcherRuntime();
        FillDeclarations(fileSystem, missingHandle);
        var property = typeof(EmittedFileSystemWatcherRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(fileSystem));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(fileSystem.CompleteEmission).Message);
        Assert.False(fileSystem.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(fileSystem, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = new EmittedFileSystemWatcherRuntime();
        FillDeclarations(declarations);
        property.SetValue(fileSystem, property.GetValue(declarations));
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.FileSystemWatchers);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireFileSystemWatchers).Message);
        runtime.BeginFileSystemWatcherEmission();
        var fileSystem = runtime.RequireFileSystemWatchers();
        Assert.Same(runtime.FileSystemWatchers, fileSystem);
        Assert.Throws<InvalidOperationException>(runtime.BeginFileSystemWatcherEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.FileSystemWatchers))!.SetMethod!.IsPrivate);
        FillDeclarations(fileSystem);
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
        Assert.Throws<InvalidOperationException>(runtime.BeginFileSystemWatcherEmission);
    }

    [Fact]
    public void ForwardCallsCanUseDeclarationsBeforeTheirBodiesExist()
    {
        var fileSystem = new EmittedFileSystemWatcherRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("filesystem_watcher_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        fileSystem.Watch = type.DefineMethod("Declared", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, fileSystem.Watch);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(fileSystem.IsComplete);
        fileSystem.Watch.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        fileSystem.Watch.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        Assert.Equal(true, Assembly.Load(bytes.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false, false)]
    [InlineData("Promise.resolve(1);", false, false)]
    [InlineData("Buffer.from('data');", false, false)]
    [InlineData("new ReadableStream();", false, false)]
    [InlineData("import * as stream from 'stream';", false, false)]
    [InlineData("import * as http from 'http';", false, false)]
    [InlineData("import * as os from 'os';", false, false)]
    [InlineData("import * as worker from 'worker_threads';", false, false)]
    [InlineData("import * as fs from 'fs';", true, false)]
    [InlineData("import * as fs from 'node:fs';", true, false)]
    [InlineData("import * as fs from 'fs/promises';", true, false)]
    [InlineData("import * as fs from 'node:fs/promises';", true, false)]
    [InlineData("import * as fs from 'primitive:fs/promises';", true, false)]
    [InlineData("const fs = require('fs');", true, false)]
    [InlineData("import('fs');", true, false)]
    [InlineData("console.log(1);", false, true)]
    [InlineData("import * as fs from 'fs';", true, true)]
    [InlineData("import * as fs from 'fs/promises';", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void MinimalEnabledImpliedHostedAndFullEmissionPreserveFeatureSelection(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.FileSystemWatchers is not null);
        if (enabled)
        {
            var fileSystem = runtime.RequireFileSystemWatchers();
            AssertFrozen(fileSystem);
            Assert.NotNull(runtime.Buffer);
            Assert.NotNull(runtime.NodeStreams);
            Assert.NotNull(runtime.Promise);
            Assert.NotNull(runtime.FileSystem);
            Assert.NotNull(runtime.FileSystemAsync);
            Assert.Equal("$FsWatcher", fileSystem.WatcherType.Name);
            Assert.Equal("$StatWatcher", fileSystem.StatType.Name);
            Assert.Equal("$FsWatchChangeClosure", fileSystem.ChangeClosureType.Name);
            Assert.Equal("$StatWatchPollClosure", fileSystem.PollClosureType.Name);
            Assert.Same(runtime.EventEmitter.Type, fileSystem.WatcherType.BaseType);
            Assert.Same(runtime.EventEmitter.Type, fileSystem.StatType.BaseType);
            Assert.Same(fileSystem.WatcherType, fileSystem.WatcherCtor.DeclaringType);
            Assert.Same(fileSystem.StatType, fileSystem.StatCtor.DeclaringType);
            Assert.Same(fileSystem.ChangeClosureType, fileSystem.ChangeClosureCtor.DeclaringType);
            Assert.Same(fileSystem.PollClosureType, fileSystem.PollClosureCtor.DeclaringType);
            Assert.Same(fileSystem.WatcherType, fileSystem.WatcherOnFsEvent.DeclaringType);
            Assert.Same(fileSystem.StatType, fileSystem.StatPollCallback.DeclaringType);
            Assert.Same(fileSystem.ChangeClosureType, fileSystem.ChangeClosureRun.DeclaringType);
            Assert.Same(fileSystem.PollClosureType, fileSystem.PollClosureRun.DeclaringType);
            Assert.Same(runtime.RuntimeClass.Type, fileSystem.Watch.DeclaringType);
            Assert.Same(runtime.RuntimeClass.Type, fileSystem.WatchFile.DeclaringType);
            Assert.Same(runtime.RuntimeClass.Type, fileSystem.UnwatchFile.DeclaringType);
            Assert.Same(runtime.RuntimeClass.Type, fileSystem.StatRegistryField.DeclaringType);
            Assert.True(fileSystem.StatRegistryField.IsStatic);
            Assert.Equal(typeof(Dictionary<string, object>), fileSystem.StatRegistryField.FieldType);
            foreach (var property in Handles.Where(property => property.PropertyType == typeof(FieldBuilder)))
            {
                var field = (FieldBuilder)property.GetValue(fileSystem)!;
                bool closureField = property.Name.Contains("Closure", StringComparison.Ordinal);
                Assert.Equal(closureField, field.IsPublic);
                Assert.Equal(!closureField, field.IsPrivate);
            }
            foreach (var property in Handles)
            {
                var handle = (MemberInfo)property.GetValue(fileSystem)!;
                Assert.True(((TypeBuilder)(handle is Type ? handle : handle.DeclaringType)!).IsCreated(), property.Name);
            }
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireFileSystemWatchers);

        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "$FsWatcher", "$StatWatcher", "$FsWatchChangeClosure", "$StatWatchPollClosure" })
            Assert.Equal(enabled, types.Contains(name));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "FsWatch", "FsWatchFile", "FsUnwatchFile" })
            Assert.Equal(enabled, methods.Contains(name));
        var fields = reader.FieldDefinitions.Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name)).ToArray();
        Assert.Equal(enabled, fields.Contains("_statWatchers"));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScheduledClosuresVerifyAndPreservePayloadOrder(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as fs from 'fs';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var receiver = Activator.CreateInstance(assembly.GetType("$EventEmitter")!)!;
        var collector = AddChangeListener(assembly, receiver);
        var changeType = assembly.GetType("$FsWatchChangeClosure")!;
        var change = Activator.CreateInstance(changeType, [receiver, "change", "data.txt"]);
        Assert.Empty(collector.Calls);
        changeType.GetMethod("Run")!.Invoke(change, null);
        Assert.Equal(("change", "data.txt"), collector.Calls.Single());

        var current = new object();
        var previous = new object();
        var pollType = assembly.GetType("$StatWatchPollClosure")!;
        var poll = Activator.CreateInstance(pollType, [receiver, current, previous]);
        pollType.GetMethod("Run")!.Invoke(poll, null);
        Assert.Equal(2, collector.Calls.Count);
        Assert.Same(current, collector.Calls[1].Current);
        Assert.Same(previous, collector.Calls[1].Previous);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WatcherDispatchesOnTheLoopAndClosesExactlyOnce(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as fs from 'fs';", hosted));
        var assembly = Assembly.Load(bytes.ToArray());
        var directory = Path.Combine(Path.GetTempPath(), $"sharpts_watcher_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var type = assembly.GetType("$FsWatcher")!;
        object? watcher = null;
        try
        {
            watcher = assembly.GetType("$Runtime")!.GetMethod("FsWatch")!.Invoke(null, [directory, null, null])!;
            var storage = (FileSystemWatcher)PrivateField(type, "_watcher").GetValue(watcher)!;
            storage.EnableRaisingEvents = false; // Drive callbacks explicitly, without OS timing races.
            var collector = AddChangeListener(assembly, watcher);
            var loopType = assembly.GetType("$EventLoop")!;
            var loop = loopType.GetMethod("GetInstance")!.Invoke(null, null)!;
            Assert.Equal(1, PrivateField(loopType, "_activeHandles").GetValue(loop));
            type.GetMethod("OnFsEvent")!.Invoke(watcher, [null, new FileSystemEventArgs(WatcherChangeTypes.Changed, directory, "data.txt")]);
            Assert.Empty(collector.Calls);
            loopType.GetMethod("PumpOnce")!.Invoke(loop, null);
            Assert.Equal(("change", "data.txt"), collector.Calls.Single());
            type.GetMethod("Close")!.Invoke(watcher, null);
            type.GetMethod("Close")!.Invoke(watcher, null);
            Assert.Equal(true, PrivateField(type, "_closed").GetValue(watcher));
            Assert.Equal(0, PrivateField(loopType, "_activeHandles").GetValue(loop));
            type.GetMethod("OnFsEvent")!.Invoke(watcher, [null, new FileSystemEventArgs(WatcherChangeTypes.Changed, directory, "ignored.txt")]);
            Assert.Equal(0, loopType.GetMethod("PumpOnce")!.Invoke(loop, null));
            Assert.Single(collector.Calls);
            Assert.Equal(false, loopType.GetMethod("HasPendingWork")!.Invoke(loop, null));
        }
        finally
        {
            if (watcher is not null) type.GetMethod("Close")!.Invoke(watcher, null);
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PollingFactoriesPreserveRegistryStatsCallbacksAndIdempotentUnwatch(bool hosted, bool twoArgumentCallback)
    {
        using var bytes = Save(EmitRuntime("import * as fs from 'fs';", hosted));
        var assembly = Assembly.Load(bytes.ToArray());
        var runtimeType = assembly.GetType("$Runtime")!;
        var registryField = runtimeType.GetField("_statWatchers", BindingFlags.NonPublic | BindingFlags.Static)!;
        var directory = Path.Combine(Path.GetTempPath(), $"sharpts_poll_watcher_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "data.txt");
        var alternatePath = Path.Combine(directory, ".", "data.txt");
        var type = assembly.GetType("$StatWatcher")!;
        object? watcher = null;
        try
        {
            File.WriteAllText(path, "old");
            Assert.Null(registryField.GetValue(null));
            Assert.Null(runtimeType.GetMethod("FsUnwatchFile")!.Invoke(null, [path]));
            Assert.Null(registryField.GetValue(null));
            var collector = new ChangeCollector();
            var callback = WrapCallback(assembly, collector);
            var options = new Dictionary<string, object> { ["interval"] = -1.0 };
            Assert.Null(runtimeType.GetMethod("FsWatchFile")!.Invoke(null,
                [alternatePath, twoArgumentCallback ? callback : options, twoArgumentCallback ? null : callback]));
            var registry = Assert.IsType<Dictionary<string, object>>(registryField.GetValue(null));
            watcher = registry[path];
            Assert.Single(registry);
            // The two-argument form uses the default interval; disable its timer before driving polls.
            ((Timer)PrivateField(type, "_timer").GetValue(watcher)!).Change(Timeout.Infinite, Timeout.Infinite);
            Assert.Equal(path, PrivateField(type, "_filename").GetValue(watcher));
            var loopType = assembly.GetType("$EventLoop")!;
            var loop = loopType.GetMethod("GetInstance")!.Invoke(null, null)!;
            Assert.Equal(1, PrivateField(loopType, "_activeHandles").GetValue(loop));
            type.GetMethod("PollCallback")!.Invoke(watcher, [null]);
            loopType.GetMethod("PumpOnce")!.Invoke(loop, null);
            Assert.Empty(collector.Calls);
            File.Delete(path);
            type.GetMethod("PollCallback")!.Invoke(watcher, [null]); // Missing-file errors remain ignored.
            File.WriteAllText(path, "updated");
            type.GetMethod("PollCallback")!.Invoke(watcher, [null]);
            Assert.Empty(collector.Calls);
            loopType.GetMethod("PumpOnce")!.Invoke(loop, null);
            var call = Assert.Single(collector.Calls);
            var statsType = assembly.GetType("$Stats")!;
            Assert.Equal(7.0, statsType.GetProperty("size")!.GetValue(call.Current));
            Assert.Equal(3.0, statsType.GetProperty("size")!.GetValue(call.Previous));
            Assert.Equal(true, statsType.GetMethod("isFile")!.Invoke(call.Current, null));
            Assert.Equal(true, statsType.GetMethod("isFile")!.Invoke(call.Previous, null));
            Assert.Null(runtimeType.GetMethod("FsUnwatchFile")!.Invoke(null, [path]));
            Assert.Null(runtimeType.GetMethod("FsUnwatchFile")!.Invoke(null, [alternatePath]));
            Assert.Empty(registry);
            Assert.Equal(true, PrivateField(type, "_closed").GetValue(watcher));
            Assert.Equal(0, PrivateField(loopType, "_activeHandles").GetValue(loop));
            File.WriteAllText(path, "closed watcher must ignore this change");
            type.GetMethod("PollCallback")!.Invoke(watcher, [null]);
            Assert.Equal(0, loopType.GetMethod("PumpOnce")!.Invoke(loop, null));
            Assert.Single(collector.Calls);
        }
        finally
        {
            if (watcher is not null) type.GetMethod("Close")!.Invoke(watcher, null);
            Directory.Delete(directory, true);
        }
    }

    public sealed class ChangeCollector
    {
        public List<(object? Current, object? Previous)> Calls { get; } = [];
        public object? Record(object? current, object? previous)
        {
            Calls.Add((current, previous));
            return null;
        }
    }

    private static object WrapCallback(Assembly assembly, ChangeCollector collector) =>
        Activator.CreateInstance(assembly.GetType("$TSFunction")!, [collector, typeof(ChangeCollector).GetMethod(nameof(ChangeCollector.Record))])!;

    private static ChangeCollector AddChangeListener(Assembly assembly, object receiver)
    {
        var collector = new ChangeCollector();
        receiver.GetType().GetMethod("On")!.Invoke(receiver, ["change", WrapCallback(assembly, collector)]);
        return collector;
    }

    private static FieldInfo PrivateField(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as fs from 'fs';", false, emitter).RequireFileSystemWatchers();
        var minimal = EmitRuntime("console.log(1);", false, emitter);
        var second = EmitRuntime("import * as fs from 'fs';", false, emitter).RequireFileSystemWatchers();
        Assert.Null(minimal.FileSystemWatchers);
        Assert.NotSame(first, second);
        Assert.NotSame(first.WatcherType, second.WatcherType);
        Assert.NotSame(first.StatRegistryField, second.StatRegistryField);
        Assert.NotSame(first.PollClosureCtor, second.PollClosureCtor);
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static void FillDeclarations(EmittedFileSystemWatcherRuntime fileSystem, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"filesystem_watcher_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = typeof(Type).IsAssignableFrom(property.PropertyType) ? type
                : typeof(ConstructorInfo).IsAssignableFrom(property.PropertyType) ? ctor
                : typeof(FieldInfo).IsAssignableFrom(property.PropertyType) ? field : method;
            property.SetValue(fileSystem, handle);
        }
    }

    private static void AssertFrozen(EmittedFileSystemWatcherRuntime fileSystem)
    {
        Assert.True(fileSystem.IsComplete);
        Assert.Throws<InvalidOperationException>(fileSystem.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(fileSystem);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(fileSystem, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"filesystem_watcher_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
