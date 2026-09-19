using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedFileSystemRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedFileSystemRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var fileSystem = new EmittedFileSystemRuntime();
        FillDeclarations(fileSystem, missingHandle);
        var property = typeof(EmittedFileSystemRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(fileSystem));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(fileSystem.CompleteEmission).Message);
        Assert.False(fileSystem.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(fileSystem, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = new EmittedFileSystemRuntime();
        FillDeclarations(declarations);
        property.SetValue(fileSystem, property.GetValue(declarations));
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.FileSystem);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireFileSystem).Message);
        runtime.BeginFileSystemEmission();
        var fileSystem = runtime.RequireFileSystem();
        Assert.Same(runtime.FileSystem, fileSystem);
        Assert.Throws<InvalidOperationException>(runtime.BeginFileSystemEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.FileSystem))!.SetMethod!.IsPrivate);
        FillDeclarations(fileSystem);
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
        Assert.Throws<InvalidOperationException>(runtime.BeginFileSystemEmission);
    }

    [Fact]
    public void ForwardCallsCanUseDeclarationsBeforeTheirBodiesExist()
    {
        var fileSystem = new EmittedFileSystemRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("filesystem_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        fileSystem.ExistsSync = type.DefineMethod("Exists", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, fileSystem.ExistsSync);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(fileSystem.IsComplete);
        fileSystem.ExistsSync.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        fileSystem.ExistsSync.GetILGenerator().Emit(OpCodes.Ret);
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
        Assert.Equal(enabled, runtime.FileSystem is not null);
        if (enabled)
        {
            var fileSystem = runtime.RequireFileSystem();
            AssertFrozen(fileSystem);
            Assert.NotNull(runtime.Buffer);
            Assert.NotNull(runtime.NodeStreams);
            Assert.NotNull(runtime.Promise);
            Assert.Equal("$Stats", fileSystem.StatsType.Name);
            Assert.Equal("$Dir", fileSystem.DirCtor.DeclaringType!.Name);
            Assert.Same(fileSystem.DirCtor.DeclaringType, fileSystem.DirPathField.DeclaringType);
            Assert.Same(fileSystem.DirentCtor.DeclaringType, fileSystem.DirentNameField.DeclaringType);
            Assert.Same(fileSystem.FileDescriptorTableInstance.FieldType, fileSystem.FileDescriptorTableOpen.DeclaringType);
            Assert.True(fileSystem.DirPathField.IsInitOnly);
            Assert.True(fileSystem.DirentNameField.IsInitOnly);
            Assert.True(fileSystem.StatsSizeField.IsPrivate);
            Assert.True(fileSystem.Kernel32CreateHardLink.Attributes.HasFlag(MethodAttributes.PinvokeImpl));
            Assert.True(fileSystem.LibcLink.Attributes.HasFlag(MethodAttributes.PinvokeImpl));
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireFileSystem);

        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "$Stats", "$Dir", "$Dirent", "$FileDescriptorTable" })
            Assert.Equal(enabled, types.Contains(name));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "FsReadFileSync", "FsStatRaw", "FsFsyncSync", "FsFlagsParsePure", "CreateHardLinkPure" })
            Assert.Equal(enabled, methods.Contains(name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryStatAndDescriptorMetadataVerifyAndExecute(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as fs from 'fs';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var statsType = assembly.GetType("$Stats")!;
        var stats = Activator.CreateInstance(statsType, [true, false, false, 7.0, 420.0, 11.0, 12.0, 13.0, 14.0]);
        Assert.Equal(true, statsType.GetMethod("isFile")!.Invoke(stats, null));
        Assert.Equal(false, statsType.GetMethod("isDirectory")!.Invoke(stats, null));
        Assert.Equal(7.0, statsType.GetProperty("size")!.GetValue(stats));
        Assert.Equal(14.0, statsType.GetProperty("birthtimeMs")!.GetValue(stats));

        var directory = Path.Combine(Path.GetTempPath(), $"sharpts_filesystem_metadata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "data.txt");
            File.WriteAllText(path, "content");
            var dirType = assembly.GetType("$Dir")!;
            var dir = Activator.CreateInstance(dirType, [directory]);
            try
            {
                var entry = dirType.GetMethod("ReadSync")!.Invoke(dir, null)!;
                Assert.Equal("data.txt", entry.GetType().GetProperty("Name")!.GetValue(entry));
                Assert.Equal(true, entry.GetType().GetMethod("IsFile")!.Invoke(entry, null));
                Assert.Null(dirType.GetMethod("ReadSync")!.Invoke(dir, null));
            }
            finally { dirType.GetMethod("CloseSync")!.Invoke(dir, null); }

            var tableType = assembly.GetType("$FileDescriptorTable")!;
            var table = tableType.GetField("Instance")!.GetValue(null);
            var fd = tableType.GetMethod("Open")!.Invoke(table, [path, FileMode.Open, FileAccess.Read, FileShare.Read]);
            var get = tableType.GetMethod("Get")!;
            try
            {
                Assert.Equal(3, fd);
                var stream = Assert.IsType<FileStream>(get.Invoke(table, [fd]));
                Assert.Equal(7, stream.Length);
            }
            finally { tableType.GetMethod("Close")!.Invoke(table, [fd]); }
            var error = Assert.Throws<TargetInvocationException>(() => get.Invoke(table, [fd]));
            Assert.Contains("EBADF", error.InnerException!.Message);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as fs from 'fs';", false, emitter).RequireFileSystem();
        var minimal = EmitRuntime("console.log(1);", false, emitter);
        var second = EmitRuntime("import * as fs from 'fs';", false, emitter).RequireFileSystem();
        Assert.Null(minimal.FileSystem);
        Assert.NotSame(first, second);
        Assert.NotSame(first.StatsType, second.StatsType);
        Assert.NotSame(first.DirPathField, second.DirPathField);
        Assert.NotSame(first.Kernel32CreateHardLink, second.Kernel32CreateHardLink);
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static void FillDeclarations(EmittedFileSystemRuntime fileSystem, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"filesystem_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
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

    private static void AssertFrozen(EmittedFileSystemRuntime fileSystem)
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
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"filesystem_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
