using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedArrayBufferRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedArrayBufferRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var buffer = CreateDeclarations(missingHandle);
        var property = typeof(EmittedArrayBufferRuntime).GetProperty(missingHandle)!;
        var readError = Assert.Throws<TargetInvocationException>(() => property.GetValue(buffer));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(readError.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(buffer.CompleteEmission).Message);
        Assert.False(buffer.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(buffer, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(buffer, property.GetValue(CreateDeclarations()));
        buffer.CompleteEmission();
        AssertFrozen(buffer);
    }

    [Fact]
    public void OptionalComponentStartsOnceAndCannotBeReplacedAfterCompletion()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.ArrayBuffer);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireArrayBuffer).Message);
        runtime.BeginArrayBufferEmission();
        var buffer = runtime.RequireArrayBuffer();
        Assert.Same(runtime.ArrayBuffer, buffer);
        Assert.Throws<InvalidOperationException>(runtime.BeginArrayBufferEmission);
        var declarations = CreateDeclarations();
        foreach (var property in Handles)
            property.SetValue(buffer, property.GetValue(declarations));
        buffer.CompleteEmission();
        AssertFrozen(buffer);
        Assert.Throws<InvalidOperationException>(runtime.BeginArrayBufferEmission);
        Assert.False(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ArrayBuffer))!.SetMethod!.IsPublic);
    }

    [Fact]
    public void ForwardCallsCanUseDeclarationsBeforeTheirBodiesExist()
    {
        var runtime = new EmittedRuntime();
        runtime.BeginArrayBufferEmission();
        var buffer = runtime.RequireArrayBuffer();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("arraybuffer_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        buffer.IsView = type.DefineMethod("IsView", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, buffer.IsView);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(type.IsCreated());
        Assert.False(buffer.IsComplete);
        buffer.IsView.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        buffer.IsView.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        Assert.Equal(true, Assembly.Load(stream.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("console.log(1);", true)]
    [InlineData("Buffer.from('data');", false)]
    [InlineData("import * as http from 'http';", false)]
    [InlineData("import * as worker from 'worker_threads';", false)]
    [InlineData("new Response('body');", false)]
    [InlineData("console.log(Array.from([1, 2]));", false)]
    public void DisabledFeatureHasNoComponentOrArrayBufferGuestMetadata(string source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Null(runtime.ArrayBuffer);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireArrayBuffer).Message);
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "$ArrayBuffer");
        Assert.DoesNotContain(reader.MethodDefinitions,
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) is "CreateArrayBuffer" or "ArrayBufferIsView" or "ArrayBufferSlice");
    }

    [Theory]
    [InlineData("new ArrayBuffer(8);", false)]
    [InlineData("new ArrayBuffer(8);", true)]
    [InlineData("new SharedArrayBuffer(8);", false)]
    [InlineData("new DataView(new ArrayBuffer(8));", false)]
    [InlineData("new Uint8Array(8);", false)]
    [InlineData("new BigInt64Array(1);", false)]
    [InlineData("ArrayBuffer.isView(null);", false)]
    [InlineData("import * as crypto from 'crypto';", false)]
    [InlineData("Atomics;", false)]
    [InlineData(null, false)]
    public void EnabledImpliedHostedAndFullEmissionCompleteAllOwnedDeclarations(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var buffer = runtime.RequireArrayBuffer();
        AssertFrozen(buffer);
        Assert.NotNull(runtime.RequireSharedArrayBuffer().Type);
        Assert.NotNull(runtime.RequireDataView().Type);
        Assert.NotNull(runtime.TypedArrays.RequireImplementation().BaseType);
        Assert.Same(buffer.Type, buffer.Ctor.DeclaringType);
        Assert.Same(buffer.Type, buffer.BufferField.DeclaringType);
        Assert.Same(buffer.Type, buffer.DetachedField.DeclaringType);
        Assert.Same(buffer.Type, buffer.Detach.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, buffer.Create.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, buffer.IsView.DeclaringType);
        foreach (var property in Handles)
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(buffer));
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
        }
    }

    [Fact]
    public void OwnedStorageSliceAndDetachHandlesRetainBehaviorWithoutGuestDependencies()
    {
        var runtime = EmitRuntime("new ArrayBuffer(8);");
        var buffer = runtime.RequireArrayBuffer();
        Assert.Empty(runtime.Deployment.Reasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, runtime.Deployment.Requirements);
        using var stream = Save(runtime);
        using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            var reader = pe.GetMetadataReader();
            Assert.DoesNotContain(reader.AssemblyReferences,
                handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
        }
        var loaded = Assembly.Load(stream.ToArray());
        var type = loaded.GetType(buffer.Type.Name)!;
        var helpers = loaded.GetType(runtime.RuntimeClass.Type.Name)!;
        var value = helpers.GetMethod(buffer.Create.Name)!.Invoke(null, [8d]);
        var getBytes = type.GetMethod(buffer.GetBuffer.Name)!;
        var bytes = Assert.IsType<byte[]>(getBytes.Invoke(value, null));
        bytes[0] = 42;
        Assert.Same(bytes, type.GetField(buffer.BufferField.Name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value));
        Assert.True(buffer.BufferField.IsInitOnly);
        Assert.False(buffer.DetachedField.IsInitOnly);
        var slice = helpers.GetMethod(buffer.SliceObject.Name)!.Invoke(null, [value, 0, 2]);
        var slicedBytes = Assert.IsType<byte[]>(getBytes.Invoke(slice, null));
        Assert.Equal(new byte[] { 42, 0 }, slicedBytes);
        Assert.NotSame(bytes, slicedBytes);
        bytes[0] = 99;
        Assert.Equal(42, slicedBytes[0]);
        var dynamicSlice = type.GetMethod(buffer.SliceDynamic.Name)!.Invoke(value, ["1.9", 4d]);
        Assert.Equal(3d, helpers.GetMethod(buffer.GetByteLength.Name)!.Invoke(null, [dynamicSlice]));
        Assert.Equal(8d, helpers.GetMethod(buffer.GetByteLength.Name)!.Invoke(null, [value]));
        type.GetMethod(buffer.Detach.Name)!.Invoke(value, null);
        Assert.Equal(true, type.GetField(buffer.DetachedField.Name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value));
        Assert.Equal(0d, helpers.GetMethod(buffer.GetByteLength.Name)!.Invoke(null, [value]));
        Assert.Same(bytes, getBytes.Invoke(value, null));
    }

    [Theory]
    [InlineData("const b = new ArrayBuffer(8); console.log(b.byteLength, b.slice(-4).byteLength, b instanceof ArrayBuffer);")]
    [InlineData("const b: any = new ArrayBuffer(8); const slice = b.slice; console.log(slice(1.9, 5).byteLength, b.slice().byteLength);")]
    [InlineData("const isView = ArrayBuffer.isView; const b = new ArrayBuffer(8); console.log(isView(new Uint8Array(b)), isView(new DataView(b)), isView(b));")]
    [InlineData("const b = new ArrayBuffer(8); const view = new DataView(b); view.setUint32(0, 123, true); console.log(Buffer.from(b)[0], new Uint8Array(b).buffer === b);")]
    [InlineData("const b = new ArrayBuffer(8); console.log(Array.from(b).length, structuredClone(b).byteLength);")]
    [InlineData("console.log(Buffer.from([1, 2]).length, Array.from([3, 4]).length);")]
    public void ArrayBufferConsumersAndOptionalTypeProbesPassILVerification(string source)
    {
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static EmittedArrayBufferRuntime CreateDeclarations(string? missingHandle = null)
    {
        var runtime = new EmittedRuntime();
        runtime.BeginArrayBufferEmission();
        var buffer = runtime.RequireArrayBuffer();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"arraybuffer_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles)
        {
            if (property.Name == missingHandle)
                continue;
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(buffer, handle);
        }
        return buffer;
    }

    private static void AssertFrozen(EmittedArrayBufferRuntime buffer)
    {
        Assert.True(buffer.IsComplete);
        Assert.Throws<InvalidOperationException>(buffer.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(buffer);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(buffer, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"arraybuffer_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
