using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedFileSystemAsyncRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedFileSystemAsyncRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var fileSystem = new EmittedFileSystemAsyncRuntime();
        FillDeclarations(fileSystem, missingHandle);
        var property = typeof(EmittedFileSystemAsyncRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(fileSystem));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(fileSystem.CompleteEmission).Message);
        Assert.False(fileSystem.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(fileSystem, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = new EmittedFileSystemAsyncRuntime();
        FillDeclarations(declarations);
        property.SetValue(fileSystem, property.GetValue(declarations));
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.FileSystemAsync);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireFileSystemAsync).Message);
        runtime.BeginFileSystemAsyncEmission();
        var fileSystem = runtime.RequireFileSystemAsync();
        Assert.Same(runtime.FileSystemAsync, fileSystem);
        Assert.Throws<InvalidOperationException>(runtime.BeginFileSystemAsyncEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.FileSystemAsync))!.SetMethod!.IsPrivate);
        FillDeclarations(fileSystem);
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
        Assert.Throws<InvalidOperationException>(runtime.BeginFileSystemAsyncEmission);
    }

    private static readonly string[] WrapperNames =
    [
        "readFile", "writeFile", "appendFile", "stat", "lstat", "unlink", "mkdir",
        "rmdir", "rm", "readdir", "rename", "copyFile", "access", "chmod", "truncate",
        "utimes", "readlink", "realpath", "symlink", "link", "mkdtemp"
    ];

    public static IEnumerable<object[]> PromiseWrapperNames => WrapperNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(PromiseWrapperNames))]
    public void EveryMissingPromiseWrapperRejectsReadsAndCompletionThenAllowsRetry(string missingWrapper)
    {
        var fileSystem = new EmittedFileSystemAsyncRuntime();
        FillDeclarations(fileSystem, missingWrapper: missingWrapper);
        Assert.Contains($"'{missingWrapper}'", Assert.Throws<InvalidOperationException>(() => fileSystem.RequirePromiseWrapper(missingWrapper)).Message);
        Assert.Contains($"'{missingWrapper}'", Assert.Throws<InvalidOperationException>(fileSystem.CompleteEmission).Message);
        Assert.False(fileSystem.IsComplete);
        fileSystem.RegisterPromiseWrapper(missingWrapper, fileSystem.ReadFile);
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
    }

    [Fact]
    public void PromiseRegistryRejectsNullUnknownDuplicateAndExternalWrites()
    {
        var fileSystem = new EmittedFileSystemAsyncRuntime();
        var view = fileSystem.PromisesWrapperMethods;
        Assert.Empty(view);
        Assert.Null(typeof(EmittedFileSystemAsyncRuntime).GetProperty(nameof(EmittedFileSystemAsyncRuntime.PromisesWrapperMethods))!.SetMethod);
        Assert.Throws<ArgumentNullException>(() => fileSystem.RegisterPromiseWrapper("readFile", null!));
        FillDeclarations(fileSystem);
        Assert.Same(view, fileSystem.PromisesWrapperMethods);
        Assert.Throws<ArgumentNullException>(() => fileSystem.RegisterPromiseWrapper(null!, fileSystem.ReadFile));
        Assert.Throws<ArgumentException>(() => fileSystem.RegisterPromiseWrapper("", fileSystem.ReadFile));
        Assert.Throws<ArgumentException>(() => fileSystem.RegisterPromiseWrapper("READFILE", fileSystem.ReadFile));
        Assert.Throws<ArgumentException>(() => fileSystem.RegisterPromiseWrapper("unknown", fileSystem.ReadFile));
        Assert.Throws<ArgumentException>(() => fileSystem.RegisterPromiseWrapper("readFile", fileSystem.ReadFile));
        var mutableView = Assert.IsAssignableFrom<IDictionary<string, MethodBuilder>>(view);
        Assert.Throws<NotSupportedException>(() => mutableView["readFile"] = fileSystem.ReadFile);
        Assert.Throws<NotSupportedException>(() => mutableView.Remove("readFile"));
        Assert.Throws<NotSupportedException>(mutableView.Clear);
        Assert.Equal(WrapperNames, view.Keys);
        fileSystem.CompleteEmission();
        AssertFrozen(fileSystem);
    }

    [Fact]
    public void PromiseWrapperDeclarationSupportsCallsBeforeItsBodyExists()
    {
        var fileSystem = new EmittedFileSystemAsyncRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("filesystem_async_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        var wrapper = type.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        fileSystem.RegisterPromiseWrapper("readFile", wrapper);
        Assert.Same(wrapper, fileSystem.PromisesWrapperMethods["readFile"]);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, fileSystem.RequirePromiseWrapper("readFile"));
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(fileSystem.IsComplete);
        wrapper.GetILGenerator().Emit(OpCodes.Ldc_I4_7);
        wrapper.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        Assert.Equal(7, Assembly.Load(bytes.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
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
        Assert.Equal(enabled, runtime.FileSystemAsync is not null);
        if (enabled)
        {
            var fileSystem = runtime.RequireFileSystemAsync();
            AssertFrozen(fileSystem);
            Assert.NotNull(runtime.Buffer);
            Assert.NotNull(runtime.NodeStreams);
            Assert.NotNull(runtime.Promise);
            Assert.NotNull(runtime.FileSystem);
            Assert.Equal("$FsAsyncOp", fileSystem.OpCtor.DeclaringType!.Name);
            Assert.Same(fileSystem.OpCtor.DeclaringType, fileSystem.OpWorker.DeclaringType);
            Assert.Equal("FsRunAsync", fileSystem.RunAsync.Name);
            Assert.Equal(WrapperNames, fileSystem.PromisesWrapperMethods.Keys);
            Assert.Equal(WrapperNames.Length, fileSystem.PromisesWrapperMethods.Values.Distinct().Count());
            foreach (var name in WrapperNames)
            {
                var wrapper = fileSystem.RequirePromiseWrapper(name);
                Assert.Same(wrapper, fileSystem.PromisesWrapperMethods[name]);
                Assert.Same(runtime.RuntimeType, wrapper.DeclaringType);
                Assert.Equal($"FsPromises_{name}_Wrapper", wrapper.Name);
            }
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireFileSystemAsync);

        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "$FsAsyncOp" })
            Assert.Equal(enabled, types.Contains(name));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "FsReadFileAsync", "FsRmAsync", "FsRmAsyncImpl", "FsRunAsync", "FsAsyncUnref", "FsGetPromisesNamespace", "FsPromises_readFile_Wrapper" })
            Assert.Equal(enabled, methods.Contains(name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackgroundWorkerVerifiesAndPreservesResultsAndOriginalExceptions(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as fs from 'fs/promises';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var operation = assembly.GetType("$FsAsyncOp")!;
        var worker = operation.GetMethod("Worker")!;
        var method = typeof(EmittedFileSystemAsyncRuntimeTests).GetMethod(nameof(WorkerResult))!;
        var args = new object[] { "value" };
        var closure = Activator.CreateInstance(operation, [method, args]);
        Assert.Equal("value", worker.Invoke(closure, null));
        method = typeof(EmittedFileSystemAsyncRuntimeTests).GetMethod(nameof(WorkerFailure))!;
        closure = Activator.CreateInstance(operation, [method, Array.Empty<object>()]);
        var error = Assert.Throws<TargetInvocationException>(() => worker.Invoke(closure, null));
        Assert.Equal("worker failure", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
    }

    public static object WorkerResult(object value) => value;
    public static object WorkerFailure() => throw new InvalidOperationException("worker failure");

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as fs from 'fs';", false, emitter).RequireFileSystemAsync();
        var minimal = EmitRuntime("console.log(1);", false, emitter);
        var second = EmitRuntime("import * as fs from 'fs';", false, emitter).RequireFileSystemAsync();
        Assert.Null(minimal.FileSystemAsync);
        Assert.NotSame(first, second);
        Assert.NotSame(first.OpCtor, second.OpCtor);
        Assert.NotSame(first.RunAsync, second.RunAsync);
        Assert.NotSame(first.PromisesWrapperMethods, second.PromisesWrapperMethods);
        Assert.NotSame(first.RequirePromiseWrapper("readFile"), second.RequirePromiseWrapper("readFile"));
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static void FillDeclarations(EmittedFileSystemAsyncRuntime fileSystem, string? missingHandle = null, string? missingWrapper = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"filesystem_async_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(ConstructorBuilder) ? ctor : method;
            property.SetValue(fileSystem, handle);
        }
        foreach (var name in WrapperNames.Where(name => name != missingWrapper))
            fileSystem.RegisterPromiseWrapper(name, method);
    }

    private static void AssertFrozen(EmittedFileSystemAsyncRuntime fileSystem)
    {
        Assert.True(fileSystem.IsComplete);
        Assert.Equal(WrapperNames.Length, fileSystem.PromisesWrapperMethods.Count);
        Assert.Throws<InvalidOperationException>(() => fileSystem.RegisterPromiseWrapper("readFile", fileSystem.ReadFile));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"filesystem_async_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
