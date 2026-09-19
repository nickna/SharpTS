using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedDataViewRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedDataViewRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var buffer = CreateDeclarations(missingHandle);
        var property = typeof(EmittedDataViewRuntime).GetProperty(missingHandle)!;
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
        Assert.Null(runtime.DataView);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireDataView).Message);
        runtime.BeginDataViewEmission();
        var buffer = runtime.RequireDataView();
        Assert.Same(runtime.DataView, buffer);
        Assert.Throws<InvalidOperationException>(runtime.BeginDataViewEmission);
        var declarations = CreateDeclarations();
        foreach (var property in Handles)
            property.SetValue(buffer, property.GetValue(declarations));
        buffer.CompleteEmission();
        AssertFrozen(buffer);
        Assert.Throws<InvalidOperationException>(runtime.BeginDataViewEmission);
        Assert.False(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.DataView))!.SetMethod!.IsPublic);
    }

    [Fact]
    public void ForwardCallsCanUseDeclarationsBeforeTheirBodiesExist()
    {
        var buffer = new EmittedDataViewRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("dataview_forward"), typeof(object).Assembly);
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
    public void DisabledFeatureHasNoComponentOrDataViewGuestMetadata(string source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Null(runtime.DataView);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireDataView).Message);
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "$DataView");
        Assert.DoesNotContain(reader.MethodDefinitions,
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) is
                "CreateDataView" or "DataViewByteLength" or "DataViewGetInt8" or "DataViewSetBigUint64");
    }

    [Theory]
    [InlineData("new DataView(new ArrayBuffer(8));", false)]
    [InlineData("new DataView(new ArrayBuffer(8));", true)]
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
        var buffer = runtime.RequireDataView();
        AssertFrozen(buffer);
        Assert.True(runtime.RequireArrayBuffer().IsComplete);
        Assert.True(runtime.RequireSharedArrayBuffer().IsComplete);
        Assert.NotNull(runtime.TypedArrays.RequireImplementation().BaseType);
        Assert.Equal("$DataView", buffer.Type.Name);
        Assert.Same(buffer.Type, buffer.Ctor.DeclaringType);
        Assert.Same(buffer.Type, buffer.BufferField.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, buffer.Create.DeclaringType);
        foreach (var property in Handles)
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(buffer));
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedFieldsAndAdaptersRetainBackingStorageOffsetAndBounds(bool shared)
    {
        var runtime = EmitRuntime("new DataView(new ArrayBuffer(16));");
        var view = runtime.RequireDataView();
        Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        using var stream = Save(runtime);
        using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            var reader = pe.GetMetadataReader();
            Assert.DoesNotContain(reader.AssemblyReferences,
                handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
        }
        var loaded = Assembly.Load(stream.ToArray());
        var type = loaded.GetType(view.Type.Name)!;
        var helpers = loaded.GetType(runtime.RuntimeClass.Type.Name)!;
        var createBuffer = shared ? runtime.RequireSharedArrayBuffer().Create : runtime.RequireArrayBuffer().Create;
        var getBuffer = shared ? runtime.RequireSharedArrayBuffer().GetBuffer : runtime.RequireArrayBuffer().GetBuffer;
        var buffer = helpers.GetMethod(createBuffer.Name)!.Invoke(null, [16d]);
        var bytes = Assert.IsType<byte[]>(buffer!.GetType().GetMethod(getBuffer.Name)!.Invoke(buffer, null));
        var createView = helpers.GetMethod(view.Create.Name)!;
        var value = createView.Invoke(null, [buffer, 4d, 8d]);
        Assert.Same(bytes, Field(view.BufferField).GetValue(value));
        Assert.Same(buffer, Field(view.ArrayBufferField).GetValue(value));
        Assert.Equal(4, Field(view.ByteOffsetField).GetValue(value));
        Assert.Equal(8, Field(view.ByteLengthField).GetValue(value));
        Assert.Same(buffer, helpers.GetMethod(view.GetBuffer.Name)!.Invoke(null, [value]));
        Assert.Equal(4d, helpers.GetMethod(view.GetByteOffset.Name)!.Invoke(null, [value]));
        Assert.Equal(8d, helpers.GetMethod(view.GetByteLength.Name)!.Invoke(null, [value]));

        var result = helpers.GetMethod(view.SetUint32Object.Name)!.Invoke(null, [value, 0, "123", true]);
        Assert.Equal("$Undefined", result!.GetType().Name);
        Assert.Equal(123, bytes[4]);
        Assert.Equal(0, bytes[7]);
        Assert.Equal(123d, helpers.GetMethod(view.GetUint32Object.Name)!.Invoke(null, [value, 0, true]));
        bytes[4] = 42;
        Assert.Equal(42d, helpers.GetMethod(view.GetUint8Object.Name)!.Invoke(null, [value, 0]));
        var boundsError = Assert.Throws<TargetInvocationException>(() =>
            helpers.GetMethod(view.GetUint32Object.Name)!.Invoke(null, [value, 6, true]));
        Assert.Contains("outside the bounds", boundsError.InnerException!.Message);
        var offsetError = Assert.Throws<TargetInvocationException>(() => createView.Invoke(null, [buffer, 17d, null]));
        Assert.Contains("Invalid DataView offset", offsetError.InnerException!.Message);
        var empty = createView.Invoke(null, [buffer, 16d, null]);
        Assert.Equal(0d, helpers.GetMethod(view.GetByteLength.Name)!.Invoke(null, [empty]));

        FieldInfo Field(FieldBuilder field)
        {
            Assert.True(field.IsInitOnly);
            Assert.Same(view.Type, field.DeclaringType);
            return type.GetField(field.Name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        }
    }

    [Theory]
    [InlineData("Int8", "-12", "-12", false)]
    [InlineData("Uint8", "250", "250", false)]
    [InlineData("Int16", "-1234", "-1234", true)]
    [InlineData("Uint16", "60000", "60000", true)]
    [InlineData("Int32", "-123456", "-123456", true)]
    [InlineData("Uint32", "4000000000", "4000000000", true)]
    [InlineData("Float32", "1.25", "1.25", true)]
    [InlineData("Float64", "-3.5", "-3.5", true)]
    [InlineData("BigInt64", "-9007199254740993n", "-9007199254740993", true)]
    [InlineData("BigUint64", "18446744073709551615n", "18446744073709551615", true)]
    public void ExtractedReadWriteAdaptersVerifyAndRunStandalone(string kind, string value, string expected, bool endian)
    {
        string endianness = endian ? ", true" : "";
        string source = $$"""
            const buffer = new ArrayBuffer(24);
            const view: any = new DataView(buffer, 4, 16);
            const set = view.set{{kind}};
            const get = view.get{{kind}};
            console.log(set(1, {{value}}{{endianness}}) === undefined);
            console.log(get(1{{endianness}}).toString());
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal($"true\n{expected}\n", TestHarness.RunCompiledStandalone(source));
    }

    [Theory]
    [InlineData("const b = new ArrayBuffer(16); const v = new DataView(b, 4, 8); console.log(v.buffer === b, v.byteOffset, v.byteLength);")]
    [InlineData("const b = new SharedArrayBuffer(16); const v = new DataView(b, 4, 8); v.setUint32(0, 123, true); console.log(new Uint8Array(b)[4]);")]
    [InlineData("const b = new ArrayBuffer(16); const v: any = Reflect.construct(DataView, [b, 4, 8]); console.log(v.byteLength, ArrayBuffer.isView(v));")]
    [InlineData("const b = new ArrayBuffer(16); const v: any = new DataView(b); v.setFloat32(0, undefined); console.log(Number.isNaN(v.getFloat32(0)));")]
    [InlineData("const v: any = new DataView(new ArrayBuffer(16)); try { v.setUint8(0, Symbol()); } catch (error) { console.log(error instanceof TypeError); }")]
    [InlineData("class Value {} console.log(Reflect.construct(Value, []) instanceof Value);")]
    public void ConsumersCoercionsAndOptionalReflectDispatchPassILVerification(string source)
    {
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void ReflectConstructionSharedStorageAndCoercionsRunStandalone()
    {
        const string source = """
            const buffer = new SharedArrayBuffer(24);
            const view: any = Reflect.construct(DataView, [buffer, 4, 16]);
            console.log(view.buffer === buffer, view.byteOffset, view.byteLength, view instanceof DataView);
            view.setUint16(0, 258);
            const bytes = new Uint8Array(buffer);
            console.log(bytes[4], bytes[5], view.getUint16(0, true));
            view.setFloat32(4, undefined);
            console.log(Number.isNaN(view.getFloat32(4)));
            try { view.setUint8(0, Symbol()); } catch (error) { console.log(error instanceof TypeError); }
            console.log(ArrayBuffer.isView(view), new DataView(buffer, 8).byteLength);
            try { Reflect.construct(DataView, [buffer, 25]); } catch (error) { console.log('invalid offset'); }
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("true 4 16 true\n1 2 513\ntrue\ntrue\ntrue 16\ninvalid offset\n",
            TestHarness.RunCompiledStandalone(source));
    }

    private static EmittedDataViewRuntime CreateDeclarations(string? missingHandle = null)
    {
        var buffer = new EmittedDataViewRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"dataview_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
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

    private static void AssertFrozen(EmittedDataViewRuntime buffer)
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"dataview_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
