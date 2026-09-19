using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedBufferRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedBufferRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void CompletionRejectsEveryMissingHandleAndCanBeRetried(string missingHandle)
    {
        var buffer = CreateDeclarations(missingHandle);
        var property = typeof(EmittedBufferRuntime).GetProperty(missingHandle)!;
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
    public void AvailabilityStartsOnceAndCannotBeReplacedAfterCompletion()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.Buffer);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireBuffer).Message);
        runtime.BeginBufferEmission(false);
        var buffer = runtime.RequireBuffer();
        Assert.Same(runtime.Buffer, buffer);
        Assert.Throws<InvalidOperationException>(() => runtime.BeginBufferEmission(true));
        var declarations = CreateDeclarations(hasTypedArrayCopy: false);
        foreach (var property in EnabledHandles(buffer))
            property.SetValue(buffer, property.GetValue(declarations));
        buffer.CompleteEmission();
        AssertFrozen(buffer);
        Assert.Throws<InvalidOperationException>(() => runtime.BeginBufferEmission(true));
        Assert.False(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Buffer))!.SetMethod!.IsPublic);
        Assert.Null(typeof(EmittedBufferRuntime).GetProperty(nameof(EmittedBufferRuntime.HasTypedArrayCopy))!.SetMethod);
    }

    [Fact]
    public void DisabledTypedArrayCopyRejectsReadsAndWritesButDoesNotBlockCompletion()
    {
        var buffer = CreateDeclarations(hasTypedArrayCopy: false);
        Assert.False(buffer.HasTypedArrayCopy);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(() => buffer.CopyBytesFrom).Message);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(() => buffer.CopyBytesFrom = CreateDeclarations().CopyBytesFrom).Message);
        buffer.CompleteEmission();
        AssertFrozen(buffer);
        Assert.Throws<InvalidOperationException>(() => buffer.CopyBytesFrom = CreateDeclarations().CopyBytesFrom);
    }

    [Fact]
    public void DeclarationSupportsForwardCallsBeforeBodyEmissionAndCompletion()
    {
        var runtime = new EmittedRuntime();
        runtime.BeginBufferEmission(false);
        var buffer = runtime.RequireBuffer();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("buffer_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        buffer.CoerceString = type.DefineMethod("Coerce", MethodAttributes.Public | MethodAttributes.Static,
            typeof(string), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(string), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, buffer.CoerceString);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(type.IsCreated());
        Assert.False(buffer.IsComplete);
        buffer.CoerceString.GetILGenerator().Emit(OpCodes.Ldstr, "later body");
        buffer.CoerceString.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        Assert.Equal("later body", Assembly.Load(stream.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("console.log(1);", true)]
    [InlineData("new ArrayBuffer(8);", false)]
    [InlineData("new Uint8Array(8);", false)]
    [InlineData("function* items() { yield* [1, 2]; }", false)]
    public void DisabledFeatureHasNoComponentOrGuestBufferDeclarations(string source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Null(runtime.Buffer);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireBuffer).Message);
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "$Buffer");
        Assert.DoesNotContain(reader.MethodDefinitions,
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) is "BufferCopyBytesFrom" or "BufferAtob" or "BufferBytesOf");
    }

    [Theory]
    [InlineData("Buffer.from('data');", false)]
    [InlineData("Buffer.from('data');", true)]
    [InlineData("Buffer.copyBytesFrom(new Uint8Array([1]));", false)]
    [InlineData("import * as buffer from 'node:buffer';", false)]
    [InlineData("import * as crypto from 'crypto';", false)]
    [InlineData("import * as fs from 'fs';", false)]
    [InlineData("import * as zlib from 'zlib';", false)]
    [InlineData("import * as http from 'http';", false)]
    [InlineData("import * as net from 'net';", false)]
    [InlineData("import * as dgram from 'dgram';", false)]
    [InlineData("new TextEncoder();", false)]
    [InlineData(null, false)]
    public void EnabledImpliedHostedAndFullEmissionCompleteOwnedHandles(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var buffer = runtime.RequireBuffer();
        AssertFrozen(buffer);
        Assert.Equal(runtime.TypedArrays.Implementation is not null, buffer.HasTypedArrayCopy);
        Assert.Same(buffer.Type, buffer.Ctor.DeclaringType);
        Assert.Same(buffer.Type, buffer.DataField.DeclaringType);
        Assert.Same(buffer.Type, buffer.ReadUInt32LE.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, buffer.BytesOf.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, buffer.CoerceString.DeclaringType);
        foreach (var property in EnabledHandles(buffer))
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(buffer));
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
        }
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.Equal(buffer.HasTypedArrayCopy, reader.MethodDefinitions.Any(
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "BufferCopyBytesFrom"));
        Assert.DoesNotContain(reader.AssemblyReferences,
            handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
        if (source is not null)
        {
            Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
            Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        }
    }

    [Fact]
    public void GlobalGetterReturnsTheOwnedBufferType()
    {
        var runtime = EmitRuntime("Buffer.from('data');");
        using var stream = Save(runtime);
        var loaded = Assembly.Load(stream.ToArray());
        var getter = loaded.GetType(runtime.RuntimeClass.Type.Name)!.GetMethod(runtime.GlobalObject.GetProperty.Name)!;
        Assert.Same(loaded.GetType(runtime.RequireBuffer().Type.Name), getter.Invoke(null, ["Buffer"]));
    }

    [Theory]
    [InlineData("const b = Buffer.from('abc'); console.log(b.toString('hex'), b.length, b[0], b instanceof Buffer);")]
    [InlineData("const b = Buffer.alloc(32); b.writeIntBE(-42, 0, 3); b.writeDoubleLE(1.5, 8); b.writeBigInt64BE(-123n, 16); console.log(b.readIntBE(0, 3), b.readDoubleLE(8), b.readBigInt64BE(16));")]
    [InlineData("const b = Buffer.from([1, 2, 3, 4]); const f = b.toString; console.log(f('hex'), b.swap16(), b.equals(Buffer.from(b)));")]
    [InlineData("console.log(Buffer.copyBytesFrom(new Uint16Array([1, 2]), 1, 1), Buffer.from(new Uint8Array([3])));")]
    [InlineData("function* items() { yield* Buffer.from([1, 2]); yield* new Uint8Array([3]); } console.log([...items()]);")]
    [InlineData("function* items() { yield* [1, 2]; } console.log([...items()]);")]
    [InlineData("console.log(atob(btoa('abc')));")]
    public void BufferConsumersAndOptionalGeneratorBranchesPassILVerification(string source)
    {
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static IEnumerable<PropertyInfo> EnabledHandles(EmittedBufferRuntime buffer) => Handles
        .Where(property => buffer.HasTypedArrayCopy || property.Name != nameof(EmittedBufferRuntime.CopyBytesFrom));

    private static EmittedBufferRuntime CreateDeclarations(string? missingHandle = null, bool hasTypedArrayCopy = true)
    {
        var runtime = new EmittedRuntime();
        runtime.BeginBufferEmission(hasTypedArrayCopy);
        var buffer = runtime.RequireBuffer();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"buffer_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in EnabledHandles(buffer))
        {
            if (property.Name == missingHandle)
                continue;
            object handle = property.PropertyType == typeof(Type) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(buffer, handle);
        }
        return buffer;
    }

    private static void AssertFrozen(EmittedBufferRuntime buffer)
    {
        Assert.True(buffer.IsComplete);
        Assert.Throws<InvalidOperationException>(buffer.CompleteEmission);
        foreach (var property in EnabledHandles(buffer))
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"buffer_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
