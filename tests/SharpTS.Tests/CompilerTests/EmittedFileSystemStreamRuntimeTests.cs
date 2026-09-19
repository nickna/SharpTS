using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedFileSystemStreamRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedFileSystemStreamRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var fileSystem = new EmittedFileSystemStreamRuntime();
        FillDeclarations(fileSystem, missingHandle);
        var property = typeof(EmittedFileSystemStreamRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(fileSystem));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(fileSystem.CompleteEmission).Message);
        Assert.False(fileSystem.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(fileSystem, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = new EmittedFileSystemStreamRuntime();
        FillDeclarations(declarations);
        property.SetValue(fileSystem, property.GetValue(declarations));
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.FileSystemStreams);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireFileSystemStreams).Message);
        runtime.BeginFileSystemStreamEmission();
        var fileSystem = runtime.RequireFileSystemStreams();
        Assert.Same(runtime.FileSystemStreams, fileSystem);
        Assert.Throws<InvalidOperationException>(runtime.BeginFileSystemStreamEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.FileSystemStreams))!.SetMethod!.IsPrivate);
        FillDeclarations(fileSystem);
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
        Assert.Throws<InvalidOperationException>(runtime.BeginFileSystemStreamEmission);
    }

    [Fact]
    public void ForwardCallsCanUseDeclarationsBeforeTheirBodiesExist()
    {
        var fileSystem = new EmittedFileSystemStreamRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("filesystem_stream_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        fileSystem.CreateReadStream = type.DefineMethod("Exists", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, fileSystem.CreateReadStream);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(fileSystem.IsComplete);
        fileSystem.CreateReadStream.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        fileSystem.CreateReadStream.GetILGenerator().Emit(OpCodes.Ret);
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
        Assert.Equal(enabled, runtime.FileSystemStreams is not null);
        if (enabled)
        {
            var fileSystem = runtime.RequireFileSystemStreams();
            AssertFrozen(fileSystem);
            Assert.NotNull(runtime.Buffer);
            Assert.NotNull(runtime.NodeStreams);
            Assert.NotNull(runtime.Promise);
            Assert.NotNull(runtime.FileSystem);
            Assert.NotNull(runtime.FileSystemAsync);
            Assert.Equal("$FsReadStream", fileSystem.ReadType.Name);
            Assert.Equal("$FsWriteStream", fileSystem.WriteType.Name);
            Assert.Same(runtime.RequireNodeStreams().ReadableType, fileSystem.ReadType.BaseType);
            Assert.Same(runtime.EventEmitter.Type, fileSystem.WriteType.BaseType);
            Assert.Same(fileSystem.ReadType, fileSystem.ReadCtor.DeclaringType);
            Assert.Same(fileSystem.WriteType, fileSystem.WriteCtor.DeclaringType);
            Assert.Same(fileSystem.ReadType, fileSystem.ReadDataField.DeclaringType);
            Assert.Same(fileSystem.WriteType, fileSystem.WriteStreamField.DeclaringType);
            Assert.Same(fileSystem.WriteType, fileSystem.Write.DeclaringType);
            Assert.Same(fileSystem.WriteType, fileSystem.End.DeclaringType);
            Assert.Same(runtime.RuntimeClass.Type, fileSystem.CreateReadStream.DeclaringType);
            Assert.Same(runtime.RuntimeClass.Type, fileSystem.CreateWriteStream.DeclaringType);
            foreach (var property in Handles.Where(property => property.PropertyType == typeof(FieldBuilder)))
                Assert.True(((FieldBuilder)property.GetValue(fileSystem)!).IsPrivate);
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireFileSystemStreams);

        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "$FsReadStream", "$FsWriteStream" })
            Assert.Equal(enabled, types.Contains(name));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "FsCreateReadStream", "FsCreateWriteStream" })
            Assert.Equal(enabled, methods.Contains(name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamConstructorsStorageAndMethodsVerifyAndExecute(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as fs from 'fs';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var readType = assembly.GetType("$FsReadStream")!;
        var read = Activator.CreateInstance(readType, ["source.txt", "payload", false, 7.0, 7.0]);
        Assert.Equal("source.txt", readType.GetProperty("Path")!.GetValue(read));
        Assert.Equal(7.0, readType.GetProperty("BytesRead")!.GetValue(read));
        Assert.Equal(false, readType.GetProperty("Pending")!.GetValue(read));

        var directory = Path.Combine(Path.GetTempPath(), $"sharpts_filesystem_stream_metadata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "data.txt");
            var writeType = assembly.GetType("$FsWriteStream")!;
            var write = Activator.CreateInstance(writeType, [path, "w", true, true, null, 0.0]);
            var storage = writeType.GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var stream = Assert.IsType<FileStream>(storage.GetValue(write));
            try
            {
                Assert.Equal(path, writeType.GetProperty("Path")!.GetValue(write));
                Assert.Equal(false, writeType.GetProperty("Pending")!.GetValue(write));
                Assert.Equal(true, writeType.GetMethod("Write")!.Invoke(write, [new byte[] { 65, 66 }]));
                writeType.GetMethod("End")!.Invoke(write, ["C"]);
                Assert.Equal(3.0, writeType.GetProperty("BytesWritten")!.GetValue(write));
                Assert.False(stream.CanWrite);
                Assert.Equal("ABC", File.ReadAllText(path));
            }
            finally { stream.Dispose(); }

            // The read subclass calls the write subclass's declared methods directly.
            var pipedPath = Path.Combine(directory, "piped.txt");
            var destination = Activator.CreateInstance(writeType, [pipedPath, "w", true, false, null, 0.0]);
            var pipeStream = Assert.IsType<FileStream>(storage.GetValue(destination));
            try
            {
                Assert.Same(destination, readType.GetMethod("Pipe")!.Invoke(read, [destination, null]));
                Assert.Equal("payload", File.ReadAllText(pipedPath));
                Assert.Equal(7.0, writeType.GetProperty("BytesWritten")!.GetValue(destination));
            }
            finally { pipeStream.Dispose(); }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as fs from 'fs';", false, emitter).RequireFileSystemStreams();
        var minimal = EmitRuntime("console.log(1);", false, emitter);
        var second = EmitRuntime("import * as fs from 'fs';", false, emitter).RequireFileSystemStreams();
        Assert.Null(minimal.FileSystemStreams);
        Assert.NotSame(first, second);
        Assert.NotSame(first.ReadType, second.ReadType);
        Assert.NotSame(first.WriteStreamField, second.WriteStreamField);
        Assert.NotSame(first.WriteCtor, second.WriteCtor);
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static void FillDeclarations(EmittedFileSystemStreamRuntime fileSystem, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"filesystem_stream_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
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

    private static void AssertFrozen(EmittedFileSystemStreamRuntime fileSystem)
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"filesystem_stream_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
