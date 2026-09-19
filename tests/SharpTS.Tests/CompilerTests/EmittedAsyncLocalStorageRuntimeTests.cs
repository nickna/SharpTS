using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedAsyncLocalStorageRuntimeTests
{
    [Fact]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRetry()
    {
        var storage = new EmittedAsyncLocalStorageRuntime();
        Assert.Contains("'Ctor'", Assert.Throws<InvalidOperationException>(() => storage.Ctor).Message);
        Assert.Contains("'Ctor'", Assert.Throws<InvalidOperationException>(storage.CompleteEmission).Message);
        Assert.False(storage.IsComplete);
        Assert.Throws<ArgumentNullException>(() => storage.Ctor = null!);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("storage_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        storage.Ctor = ctor;
        Assert.Same(ctor, storage.Ctor);
        Assert.False(type.IsCreated());
        storage.CompleteEmission();
        AssertFrozen(storage);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.AsyncLocalStorage);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireAsyncLocalStorage).Message);
        runtime.BeginAsyncLocalStorageEmission();
        var storage = runtime.RequireAsyncLocalStorage();
        Assert.Same(runtime.AsyncLocalStorage, storage);
        Assert.Throws<InvalidOperationException>(runtime.BeginAsyncLocalStorageEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.AsyncLocalStorage))!.SetMethod!.IsPrivate);
        storage.Ctor = EmitRuntime("import 'async_hooks';", false).RequireAsyncLocalStorage().Ctor;
        storage.CompleteEmission();
        AssertFrozen(storage);
        Assert.Throws<InvalidOperationException>(runtime.BeginAsyncLocalStorageEmission);
    }

    [Fact]
    public void ClassIsCreatedWithReadableConstructorBeforeFamilyCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("storage_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var function = module.DefineType("Function", TypeAttributes.Public);
        var invoke = function.DefineMethod("Invoke", MethodAttributes.Public, typeof(object), [typeof(object[])]);
        invoke.GetILGenerator().Emit(OpCodes.Ldnull);
        invoke.GetILGenerator().Emit(OpCodes.Ret);
        function.CreateType();
        var storage = new EmittedAsyncLocalStorageRuntime();
        new RuntimeEmitter(TypeProvider.Runtime).EmitAsyncLocalStorageClass(module, storage, function, invoke);
        Assert.True(Assert.IsAssignableFrom<TypeBuilder>(storage.Ctor.DeclaringType).IsCreated());
        Assert.False(storage.IsComplete);
        storage.CompleteEmission();
        AssertFrozen(storage);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    [Theory]
    [InlineData("const value=1;", false, false)]
    [InlineData("import * as hooks from 'async_hooks';", true, false)]
    [InlineData("import {AsyncLocalStorage as Storage} from 'node:async_hooks';", true, false)]
    [InlineData("const hooks=require('node:async_hooks');", true, false)]
    [InlineData("import('async_hooks');", true, false)]
    [InlineData("import * as workers from 'worker_threads';", true, false)]
    [InlineData("AsyncLocalStorage;", true, false)]
    [InlineData("import {create} from 'primitive:async_hooks';", false, false)]
    [InlineData("new Promise(resolve=>resolve(1));", false, false)]
    [InlineData("import * as os from 'os';", false, false)]
    [InlineData("const value=1;", false, true)]
    [InlineData("import 'node:async_hooks';", true, false)]
    [InlineData("import 'node:async_hooks';", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void FeatureGatesPreserveOptionalClassAndDependencies(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.AsyncLocalStorage is not null);
        if (enabled)
        {
            var storage = runtime.RequireAsyncLocalStorage();
            AssertFrozen(storage);
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(storage.Ctor.DeclaringType).IsCreated());
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireAsyncLocalStorage);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        Assert.Equal(enabled, types.Contains("$AsyncLocalStorage"));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        if (source == "import 'node:async_hooks';")
        {
            Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
            Assert.Equal(hosted, runtime.Promise is not null);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedClassVerifiesAndRestoresContextOnReturnsExceptionsAndInvalidCallbacks(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import 'async_hooks';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$AsyncLocalStorage")!;
        var functionType = assembly.GetType("$TSFunction")!;
        var storage = Activator.CreateInstance(type)!;
        var other = Activator.CreateInstance(type)!;
        var get = type.GetMethod("GetStore")!;
        var enter = type.GetMethod("EnterWith")!;
        var run = type.GetMethod("Run")!;
        var exit = type.GetMethod("Exit")!;
        object Wrap(Func<object?> callback) => Activator.CreateInstance(functionType,
            [callback, typeof(Func<object?>).GetMethod(nameof(Func<object?>.Invoke))!])!;
        object? Read() => get.Invoke(storage, null);
        object? Run(object? value, Func<object?> callback) => run.Invoke(storage, [value, Wrap(callback)]);
        object? Exit(Func<object?> callback) => exit.Invoke(storage, [Wrap(callback)]);

        Assert.Null(Read());
        var initial = new object();
        enter.Invoke(storage, [initial]);
        Assert.Equal(42, Run("outer", () =>
        {
            Assert.Equal("outer", Read());
            Assert.Equal("inner", Run("inner", Read));
            Assert.Equal("outer", Read());
            Assert.Equal(42, Exit(() => { Assert.Null(Read()); return 42; }));
            Assert.Equal("outer", Read());
            Assert.Null(get.Invoke(other, null));
            return 42;
        }));
        Assert.Same(initial, Read());
        var expected = new InvalidOperationException("callback failed");
        var runError = Assert.Throws<TargetInvocationException>(() => Run("throwing", () =>
        {
            Assert.Equal("throwing", Read());
            throw expected;
        }));
        Assert.Same(expected, runError.GetBaseException());
        Assert.Same(initial, Read());
        var exitError = Assert.Throws<TargetInvocationException>(() => Exit(() =>
        {
            Assert.Null(Read());
            throw expected;
        }));
        Assert.Same(expected, exitError.GetBaseException());
        Assert.Same(initial, Read());
        var invalidRun = Assert.Throws<TargetInvocationException>(() => run.Invoke(storage, ["invalid", new object()]));
        Assert.IsType<InvalidCastException>(invalidRun.InnerException);
        Assert.Same(initial, Read());
        var invalidExit = Assert.Throws<TargetInvocationException>(() => exit.Invoke(storage, [new object()]));
        Assert.IsType<InvalidCastException>(invalidExit.InnerException);
        Assert.Same(initial, Read());

        type.GetMethod("Disable")!.Invoke(storage, null);
        Assert.Null(Read());
        enter.Invoke(storage, ["after disable"]);
        Assert.Null(Read());
        Assert.Null(Run("still disabled", Read));
        Assert.Null(Read());
        Assert.Null(get.Invoke(other, null));
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import 'async_hooks';", false, emitter).RequireAsyncLocalStorage();
        var minimal = EmitRuntime("const value=1;", false, emitter);
        var second = EmitRuntime("import 'async_hooks';", false, emitter).RequireAsyncLocalStorage();
        Assert.Null(minimal.AsyncLocalStorage);
        Assert.NotSame(first, second);
        Assert.NotSame(first.Ctor, second.Ctor);
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static void AssertFrozen(EmittedAsyncLocalStorageRuntime storage)
    {
        Assert.True(storage.IsComplete);
        Assert.NotNull(storage.Ctor);
        Assert.False(typeof(EmittedAsyncLocalStorageRuntime).GetProperty(nameof(storage.Ctor))!.SetMethod!.IsPublic);
        Assert.Throws<InvalidOperationException>(storage.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => storage.Ctor = storage.Ctor);
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"storage_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
