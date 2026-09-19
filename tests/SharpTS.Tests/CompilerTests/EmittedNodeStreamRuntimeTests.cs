using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedNodeStreamRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedNodeStreamRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var streams = new EmittedNodeStreamRuntime(hasAbortSignal: true);
        FillDeclarations(streams, missingHandle);
        var property = typeof(EmittedNodeStreamRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(streams));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(streams.CompleteEmission).Message);
        Assert.False(streams.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(streams, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);

        var declarations = new EmittedNodeStreamRuntime(hasAbortSignal: true);
        FillDeclarations(declarations);
        property.SetValue(streams, property.GetValue(declarations));
        streams.CompleteEmission();
        AssertFrozen(streams);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalComponentAndAbortWrapperHaveExplicitAvailability(bool hasAbortSignal)
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.NodeStreams);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireNodeStreams).Message);
        runtime.BeginNodeStreamEmission(hasAbortSignal);
        var streams = runtime.RequireNodeStreams();
        Assert.Same(runtime.NodeStreams, streams);
        Assert.Equal(hasAbortSignal, streams.HasAbortSignal);
        Assert.Throws<InvalidOperationException>(() => runtime.BeginNodeStreamEmission(!hasAbortSignal));
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.NodeStreams))!.SetMethod!.IsPrivate);

        if (!hasAbortSignal)
        {
            Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(() => streams.AddAbortSignal).Message);
            Assert.Throws<InvalidOperationException>(() => streams.AddAbortSignal = null!);
        }
        FillDeclarations(streams);
        streams.CompleteEmission();
        AssertFrozen(streams);
        Assert.Throws<InvalidOperationException>(() => runtime.BeginNodeStreamEmission(hasAbortSignal));
    }

    [Fact]
    public void ReadableCanCallAForwardDeclaredDuplexMethodBeforeItsBodyExists()
    {
        var streams = new EmittedNodeStreamRuntime(hasAbortSignal: false);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("node_stream_forward"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var readable = module.DefineType("Readable", TypeAttributes.Public);
        streams.ReadableType = readable;
        streams.ReadableCtor = readable.DefineDefaultConstructor(MethodAttributes.Public);
        var duplex = module.DefineType("Duplex", TypeAttributes.Public, streams.ReadableType);
        streams.DuplexType = duplex;
        streams.DuplexCtor = duplex.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var constructorIl = streams.DuplexCtor.GetILGenerator();
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Call, streams.ReadableCtor);
        constructorIl.Emit(OpCodes.Ret);
        streams.DuplexWrite = duplex.DefineMethod("Write", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        streams.ReadablePipe = readable.DefineMethod("Pipe", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var il = streams.ReadablePipe.GetILGenerator();
        il.Emit(OpCodes.Call, streams.DuplexWrite);
        il.Emit(OpCodes.Ret);
        il = streams.DuplexWrite.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Ret);
        duplex.CreateType();
        readable.CreateType();
        Assert.False(streams.IsComplete);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        var loaded = Assembly.Load(bytes.ToArray());
        Assert.Equal(7, loaded.GetType("Readable")!.GetMethod("Pipe")!.Invoke(null, null));
        Assert.Equal(loaded.GetType("Readable"), loaded.GetType("Duplex")!.BaseType);
    }

    [Theory]
    [InlineData("console.log(1);", false, false, false)]
    [InlineData("Promise.resolve(1);", false, false, false)]
    [InlineData("new AbortController();", false, false, false)]
    [InlineData("new ReadableStream();", false, false, false)]
    [InlineData("import * as net from 'net';", false, false, false)]
    [InlineData("import * as stream from 'stream';", true, false, false)]
    [InlineData("import * as stream from 'node:stream';", true, false, false)]
    [InlineData("import * as stream from 'stream/promises';", true, false, false)]
    [InlineData("import * as fs from 'fs';", true, false, false)]
    [InlineData("import * as zlib from 'zlib';", true, false, false)]
    [InlineData("import * as http from 'http';", true, true, false)]
    [InlineData("import * as child from 'child_process';", true, false, false)]
    [InlineData("import * as stream from 'stream/web';", true, true, false)]
    [InlineData("import * as stream from 'stream/consumers';", true, true, false)]
    [InlineData("import * as stream from 'stream'; new AbortController();", true, true, false)]
    [InlineData("console.log(1);", false, false, true)]
    [InlineData("import * as stream from 'stream';", true, false, true)]
    [InlineData("import * as stream from 'stream'; new AbortController();", true, true, true)]
    [InlineData(null, true, true, false)]
    [InlineData(null, true, true, true)]
    public void MinimalEnabledImpliedHostedAndFullEmissionPreserveFeatureSelection(string? source, bool enabled, bool abortSignal, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.NodeStreams is not null);
        if (enabled)
        {
            var streams = runtime.RequireNodeStreams();
            AssertFrozen(streams);
            Assert.Equal(abortSignal, streams.HasAbortSignal);
            Assert.NotNull(runtime.Promise);
            Assert.Equal("$Readable", streams.ReadableType.Name);
            Assert.Equal("$Writable", streams.WritableType.Name);
            Assert.Same(runtime.EventEmitter.Type, streams.ReadableType.BaseType);
            Assert.Same(runtime.EventEmitter.Type, streams.WritableType.BaseType);
            Assert.Same(streams.ReadableType, streams.DuplexType.BaseType);
            Assert.Same(streams.DuplexType, streams.TransformType.BaseType);
            Assert.Same(streams.TransformType, streams.PassThroughCtor.DeclaringType!.BaseType);
            Assert.Same(streams.ReadableType, streams.ReadableBufferField.DeclaringType);
            Assert.Same(streams.ReadableType, streams.ReadableIterNext.DeclaringType);
            Assert.Same(streams.WritableType, streams.WritableNeedDrainField.DeclaringType);
            Assert.True(streams.ReadableBufferField.IsFamily);
            foreach (var property in SelectedHandles(streams))
            {
                var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(streams));
                Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
            }
            Assert.Same(streams.Finished, runtime.BuiltInModules.GetOptional("stream", "finished"));
            Assert.Same(streams.PromisePipeline, runtime.BuiltInModules.GetOptional("stream/promises", "pipeline"));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(runtime.RequireNodeStreams);
            Assert.Null(runtime.BuiltInModules.GetOptional("stream", "finished"));
        }

        using var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "$Readable", "$Writable", "$Duplex", "$Transform", "$PassThrough", "$StreamUtils", "$StreamComposeBridge", "$StreamAbortCallback", "$StreamFinishedCleanup", "$TransformDoneCallback", "$WriteCallbackWrapper" })
            Assert.Equal(enabled, types.Contains(name));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        Assert.Equal(abortSignal, methods.Contains("StreamAddAbortSignal"));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    private static IEnumerable<PropertyInfo> SelectedHandles(EmittedNodeStreamRuntime streams) => Handles
        .Where(property => streams.HasAbortSignal || property.Name != nameof(EmittedNodeStreamRuntime.AddAbortSignal));

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as stream from 'stream';", false, emitter).RequireNodeStreams();
        var minimal = EmitRuntime("console.log(1);", false, emitter);
        var second = EmitRuntime("import * as stream from 'stream';", false, emitter).RequireNodeStreams();
        Assert.Null(minimal.NodeStreams);
        Assert.NotSame(first, second);
        Assert.NotSame(first.ReadableType, second.ReadableType);
        Assert.NotSame(first.ReadableBufferField, second.ReadableBufferField);
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static void FillDeclarations(EmittedNodeStreamRuntime streams, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"node_stream_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in SelectedHandles(streams).Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(Type) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(streams, handle);
        }
    }

    private static void AssertFrozen(EmittedNodeStreamRuntime streams)
    {
        Assert.True(streams.IsComplete);
        Assert.Throws<InvalidOperationException>(streams.CompleteEmission);
        foreach (var property in SelectedHandles(streams))
        {
            var value = property.GetValue(streams);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(streams, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        if (!streams.HasAbortSignal)
        {
            Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(() => streams.AddAbortSignal).Message);
            Assert.Throws<InvalidOperationException>(() => streams.AddAbortSignal = streams.Pipeline);
        }
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"node_stream_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
