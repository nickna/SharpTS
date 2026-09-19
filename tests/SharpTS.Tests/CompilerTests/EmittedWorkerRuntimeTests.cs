using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedWorkerRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedWorkerRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRepair(string missingHandle)
    {
        var workers = CreateDeclarations(missingHandle);
        var property = typeof(EmittedWorkerRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(workers));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(workers.CompleteEmission).Message);
        Assert.False(workers.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(workers, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(workers, property.GetValue(CreateDeclarations()));
        workers.CompleteEmission();
        AssertFrozen(workers);
    }

    [Fact]
    public void RequiredOwnersAreIndependentAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.Workers, second.Workers);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Workers))!.SetMethod);
        Assert.Throws<InvalidOperationException>(first.Workers.CompleteEmission);
        Assert.False(first.Workers.IsComplete);
    }

    [Fact]
    public void ReceiveIsDeclaredBeforeItsDeferredBodyAndRuntimeTypeCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("worker_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, Detect("const value=1;"));
        var helpers = module.DefineType("StagedWorkers", TypeAttributes.Public);
        var workers = new EmittedWorkerRuntime();
        EmitHelper(emitter, "EmitWorkerHelper", helpers, workers, runtime.EventLoop);
        EmitHelper(emitter, "EmitWorkerThreadsModuleHelpers", helpers, workers);
        Assert.Equal(0, workers.ReceiveMessageOnPort.GetILGenerator().ILOffset);
        Assert.False(helpers.IsCreated());
        Assert.False(workers.IsComplete);
        foreach (var property in Handles)
        {
            var member = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(workers));
            Assert.Same(helpers, member.DeclaringType);
            if (member is MethodBuilder method && method != workers.ReceiveMessageOnPort)
                Assert.True(method.GetILGenerator().ILOffset > 0);
        }
        EmitHelper(emitter, "EmitWorkerThreadsReceiveMessageOnPortBody", workers,
            runtime.MessageChannels.Port, runtime.Sentinels.UndefinedInstance);
        Assert.True(workers.ReceiveMessageOnPort.GetILGenerator().ILOffset > 0);
        helpers.CreateType();
        workers.CompleteEmission();
        AssertFrozen(workers);
        using var bytes = Save(runtime);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var type = Assembly.Load(bytes.ToArray()).GetType("StagedWorkers")!;
        Assert.Equal(true, Call(type, "IsMainThread"));
        Assert.Equal("$Undefined", Call(type, "ReceiveMessageOnPort", new object?[] { null })!.GetType().Name);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value=1;", true)]
    [InlineData("new Worker('child.ts');", false)]
    [InlineData("import {Worker} from 'worker_threads';", false)]
    [InlineData("import * as workers from 'node:worker_threads';", false)]
    [InlineData("const workers=require('worker_threads');", false)]
    [InlineData("new MessageChannel();", false)]
    [InlineData("new Uint8Array(2);", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void RequiredMetadataKeepsSignaturesAndDependenciesAcrossFeatureSets(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.Workers);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var property in Handles.Where(property => property.PropertyType == typeof(MethodBuilder)))
            Assert.Contains(property.Name == "Create" ? "CreateWorker" : "WorkerThreads" + property.Name, methods);
        Assert.Contains("ConfigureWorkerContext", methods);
        Assert.Contains("ClearWorkerContext", methods);
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.DoesNotContain(runtime.RequiredSharpTSRuntimeReasons, reason =>
            reason == "Worker" || reason.StartsWith("worker_threads.", StringComparison.Ordinal));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var create = type.GetMethod("CreateWorker")!;
        Assert.Equal(typeof(object), create.ReturnType);
        Assert.Equal(new[] { typeof(string), typeof(object), typeof(object) }, create.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(bool), type.GetMethod("WorkerThreadsIsMainThread")!.ReturnType);
        Assert.Equal(typeof(double), type.GetMethod("WorkerThreadsThreadId")!.ReturnType);
        Assert.Equal(typeof(void), type.GetMethod("WorkerThreadsSetEnvironmentData")!.ReturnType);
        Assert.Equal(typeof(void), type.GetMethod("WorkerThreadsMarkAsUntransferable")!.ReturnType);
        Assert.Equal(typeof(Type), Field(type, runtime.Workers.ForeignReceiveType.Name).FieldType);
        Assert.Equal(typeof(MethodInfo), Field(type, runtime.Workers.ForeignReceiveMethod.Name).FieldType);
    }

    [Fact]
    public void ForeignReceiveCachePreservesHitsMissesNullAndTypeChanges()
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var cachedType = Field(type, runtime.Workers.ForeignReceiveType.Name);
        var cachedMethod = Field(type, runtime.Workers.ForeignReceiveMethod.Name);
        object? Receive(object? port) => Call(type, "ReceiveMessageOnPort", port);
        Assert.True(cachedType.IsPrivate && cachedType.IsStatic);
        Assert.True(cachedMethod.IsPrivate && cachedMethod.IsStatic);
        var undefined = Receive(null);
        Assert.Equal("$Undefined", undefined!.GetType().Name);
        Assert.Null(cachedType.GetValue(null));
        var first = new ForeignPort();
        Assert.Same(first.Message, Receive(first));
        var method = cachedMethod.GetValue(null);
        Assert.Equal(typeof(ForeignPort), cachedType.GetValue(null));
        Assert.NotNull(method);
        Assert.Same(first.Message, Receive(first));
        Assert.Same(method, cachedMethod.GetValue(null));
        Assert.Equal(2, first.Calls);
        Assert.Same(undefined, Receive(new EmptyForeignPort()));
        Assert.Equal(typeof(EmptyForeignPort), cachedType.GetValue(null));
        Assert.NotNull(cachedMethod.GetValue(null));
        Assert.Same(undefined, Receive(new object()));
        Assert.Equal(typeof(object), cachedType.GetValue(null));
        Assert.Null(cachedMethod.GetValue(null));
        Assert.Same(undefined, Receive(new object()));
        Assert.Same(undefined, Receive(null));
        Assert.Equal(typeof(object), cachedType.GetValue(null));
        Assert.Same(first.Message, Receive(first));
        // ClearWorkerContext clears host context objects, but preserves this existing cache contract.
        type.GetMethod("ClearWorkerContext")!.Invoke(null, null);
        Assert.Equal(typeof(ForeignPort), cachedType.GetValue(null));
        Assert.Same(method, cachedMethod.GetValue(null));
    }

    [Fact]
    public void ReusingEmitterKeepsWorkerContextAndReceiveCachesInTheirOwnAssemblies()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("new Worker('child.ts');", false, emitter);
        var second = EmitRuntime("const value=1;", false, emitter);
        Assert.NotSame(first.Workers, second.Workers);
        foreach (var property in Handles) Assert.NotSame(property.GetValue(first.Workers), property.GetValue(second.Workers));
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        var firstType = Assembly.Load(firstBytes.ToArray()).GetType("$Runtime")!;
        var secondType = Assembly.Load(secondBytes.ToArray()).GetType("$Runtime")!;
        var data = new object();
        var port = new ForeignPort();
        AssertMain(firstType);
        AssertMain(secondType);
        firstType.GetMethod("ConfigureWorkerContext")!.Invoke(null, [7d, data, port]);
        Assert.Equal(false, Call(firstType, "IsMainThread"));
        Assert.Equal(7d, Call(firstType, "ThreadId"));
        Assert.Same(data, Call(firstType, "WorkerData"));
        Assert.Same(port, Call(firstType, "ParentPort"));
        Assert.Same(port.Message, Call(firstType, "ReceiveMessageOnPort", port));
        AssertMain(secondType);
        Assert.Null(Field(secondType, second.Workers.ForeignReceiveType.Name).GetValue(null));
        Assert.Null(Field(secondType, second.Workers.ForeignReceiveMethod.Name).GetValue(null));
        firstType.GetMethod("ClearWorkerContext")!.Invoke(null, null);
        AssertMain(firstType);
        AssertMain(secondType);
        Assert.Equal(false, Field(firstType, "_workerContextEnabled").GetValue(null));
        Assert.Equal(0d, Field(firstType, "_workerThreadId").GetValue(null));
        Assert.Null(Field(firstType, "_workerData").GetValue(null));
        Assert.Null(Field(firstType, "_workerParentPort").GetValue(null));
    }

    public sealed class ForeignPort
    {
        public object Message { get; } = new Dictionary<string, object> { ["message"] = 7d };
        public int Calls { get; private set; }
        internal object ReceiveMessageSyncForCompiled() { Calls++; return Message; }
    }

    public sealed class EmptyForeignPort
    {
        internal object? ReceiveMessageSyncForCompiled() => null;
    }

    private static void AssertMain(Type type)
    {
        Assert.Equal(true, Call(type, "IsMainThread"));
        Assert.Equal(0d, Call(type, "ThreadId"));
        Assert.Null(Call(type, "WorkerData"));
        Assert.Null(Call(type, "ParentPort"));
    }

    private static object? Call(Type type, string method, params object?[] args) =>
        type.GetMethod("WorkerThreads" + method)!.Invoke(null, args);

    private static FieldInfo Field(Type type, string name) => type.GetField(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void EmitHelper(RuntimeEmitter emitter, string name, params object[] args) =>
        typeof(RuntimeEmitter).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, args);

    private static EmittedWorkerRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"worker_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("WorkerDeclarations", TypeAttributes.Public);
        var workers = new EmittedWorkerRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omitted) continue;
            object member;
            if (property.PropertyType == typeof(FieldBuilder))
                member = type.DefineField(property.Name, typeof(object), FieldAttributes.Public);
            else
            {
                var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                method.GetILGenerator().Emit(OpCodes.Ret);
                member = method;
            }
            property.SetValue(workers, member);
        }
        return workers;
    }

    private static void AssertFrozen(EmittedWorkerRuntime workers)
    {
        Assert.True(workers.IsComplete);
        Assert.Throws<InvalidOperationException>(workers.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(workers);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(workers, value));
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

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"workers_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
