using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedSharedArrayBufferRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedSharedArrayBufferRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var buffer = CreateDeclarations(missingHandle);
        var property = typeof(EmittedSharedArrayBufferRuntime).GetProperty(missingHandle)!;
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
        Assert.Null(runtime.SharedArrayBuffer);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireSharedArrayBuffer).Message);
        runtime.BeginSharedArrayBufferEmission();
        var buffer = runtime.RequireSharedArrayBuffer();
        Assert.Same(runtime.SharedArrayBuffer, buffer);
        Assert.Throws<InvalidOperationException>(runtime.BeginSharedArrayBufferEmission);
        var declarations = CreateDeclarations();
        foreach (var property in Handles)
            property.SetValue(buffer, property.GetValue(declarations));
        buffer.CompleteEmission();
        AssertFrozen(buffer);
        Assert.Throws<InvalidOperationException>(runtime.BeginSharedArrayBufferEmission);
        Assert.False(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.SharedArrayBuffer))!.SetMethod!.IsPublic);
    }

    [Fact]
    public void ForwardCallsCanUseDeclarationsBeforeTheirBodiesExist()
    {
        var buffer = new EmittedSharedArrayBufferRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("sharedarraybuffer_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        buffer.GetByteLength = type.DefineMethod("GetByteLength", MethodAttributes.Public | MethodAttributes.Static,
            typeof(double), [typeof(object)]);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(double), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Ldnull);
        caller.GetILGenerator().Emit(OpCodes.Call, buffer.GetByteLength);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(type.IsCreated());
        Assert.False(buffer.IsComplete);
        buffer.GetByteLength.GetILGenerator().Emit(OpCodes.Ldc_R8, 8d);
        buffer.GetByteLength.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        Assert.Equal(8d, Assembly.Load(stream.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("console.log(1);", true)]
    [InlineData("Buffer.from('data');", false)]
    [InlineData("import * as http from 'http';", false)]
    [InlineData("import * as worker from 'worker_threads';", false)]
    [InlineData("new Response('body');", false)]
    [InlineData("console.log(Array.from([1, 2]));", false)]
    public void DisabledFeatureHasNoComponentOrSharedArrayBufferGuestMetadata(string source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Null(runtime.SharedArrayBuffer);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireSharedArrayBuffer).Message);
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "$SharedArrayBuffer");
        Assert.DoesNotContain(reader.MethodDefinitions,
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) is
                "CreateSharedArrayBuffer" or "SharedArrayBufferByteLength" or "SharedArrayBufferSlice");
    }

    [Theory]
    [InlineData("new SharedArrayBuffer(8);", false)]
    [InlineData("new SharedArrayBuffer(8);", true)]
    [InlineData("new ArrayBuffer(8);", false)]
    [InlineData("new DataView(new SharedArrayBuffer(8));", false)]
    [InlineData("new Uint8Array(8);", false)]
    [InlineData("new BigInt64Array(1);", false)]
    [InlineData("ArrayBuffer.isView(null);", false)]
    [InlineData("import * as crypto from 'crypto';", false)]
    [InlineData("Atomics;", false)]
    [InlineData(null, false)]
    public void EnabledImpliedHostedAndFullEmissionCompleteAllOwnedDeclarations(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var buffer = runtime.RequireSharedArrayBuffer();
        AssertFrozen(buffer);
        Assert.True(runtime.RequireArrayBuffer().IsComplete);
        Assert.NotNull(runtime.RequireDataView().Type);
        Assert.NotNull(runtime.TypedArrays.RequireImplementation().BaseType);
        Assert.Equal("$SharedArrayBuffer", buffer.Type.Name);
        Assert.Same(buffer.Type, buffer.Ctor.DeclaringType);
        Assert.Same(buffer.Type, buffer.BufferField.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, buffer.Create.DeclaringType);
        foreach (var property in Handles)
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(buffer));
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
        }
    }

    [Fact]
    public void OwnedBackingStorageAndSliceHandlesRetainBehaviorWithoutGuestDependencies()
    {
        var runtime = EmitRuntime("new SharedArrayBuffer(8);");
        var buffer = runtime.RequireSharedArrayBuffer();
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
        bytes[1] = 42;
        Assert.Same(bytes, getBytes.Invoke(value, null));
        Assert.Same(bytes, type.GetField(buffer.BufferField.Name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value));
        Assert.True(buffer.BufferField.IsInitOnly);
        Assert.Equal(8d, helpers.GetMethod(buffer.GetByteLength.Name)!.Invoke(null, [value]));
        var slice = helpers.GetMethod(buffer.SliceObject.Name)!.Invoke(null, [value, 1, int.MaxValue]);
        Assert.IsType(type, slice);
        var slicedBytes = Assert.IsType<byte[]>(getBytes.Invoke(slice, null));
        Assert.Equal(new byte[] { 42, 0, 0, 0, 0, 0, 0 }, slicedBytes);
        Assert.NotSame(bytes, slicedBytes);
        bytes[1] = 99;
        Assert.Equal(42, slicedBytes[0]);
        Assert.Equal(7d, helpers.GetMethod(buffer.GetByteLength.Name)!.Invoke(null, [slice]));
    }

    [Theory]
    [InlineData("const b = new SharedArrayBuffer(8); console.log(b.byteLength, b.slice(2).byteLength, b instanceof SharedArrayBuffer);")]
    [InlineData("const b: any = new SharedArrayBuffer(8); const slice = b.slice; console.log(slice(1, 5).byteLength);")]
    [InlineData("const b = new SharedArrayBuffer(8); const view = new DataView(b); view.setUint32(0, 123, true); console.log(new Uint8Array(b)[0]);")]
    [InlineData("const b = new SharedArrayBuffer(8); console.log(Array.from(b).length, structuredClone(b).byteLength);")]
    [InlineData("console.log(Buffer.from([1, 2]).length, Array.from([3, 4]).length);")]
    public void ConsumersAndOptionalTypeProbesPassILVerification(string source)
    {
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void ViewsCloningAndAtomicAccessShareStorageWhileSlicesCopyInStandaloneOutput()
    {
        const string source = """
            const buffer = new SharedArrayBuffer(16);
            const bytes = new Uint8Array(buffer);
            const view = new DataView(buffer);
            view.setUint32(0, 123, true);
            const numbers = new Int32Array(buffer);
            console.log(bytes[0], view.buffer === buffer, numbers.buffer === buffer);
            const cloned = structuredClone(buffer);
            const clonedView = structuredClone(numbers);
            console.log(cloned === buffer, clonedView !== numbers, clonedView.buffer === buffer);
            console.log(Atomics.add(clonedView, 0, 7), Atomics.load(numbers, 0));
            const sliced = buffer.slice(0, 4);
            console.log(sliced instanceof SharedArrayBuffer, sliced.byteLength);
            numbers[0] = 9;
            console.log(new Int32Array(sliced)[0], new Int32Array(cloned)[0]);
            const dynamic: any = buffer;
            console.log(dynamic.byteLength, dynamic.slice(4, 8).byteLength);
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("123 true true\ntrue true true\n123 130\ntrue 4\n130 9\n16 4\n",
            TestHarness.RunCompiledStandalone(source));
    }

    private static EmittedSharedArrayBufferRuntime CreateDeclarations(string? missingHandle = null)
    {
        var buffer = new EmittedSharedArrayBufferRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"sharedarraybuffer_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
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
            property.SetValue(buffer, handle);
        }
        return buffer;
    }

    private static void AssertFrozen(EmittedSharedArrayBufferRuntime buffer)
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"sharedarraybuffer_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
