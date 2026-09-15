using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedTextEncodingRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedTextEncodingRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var textEncoding = CreateDeclarations(missingHandle);
        var property = typeof(EmittedTextEncodingRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(textEncoding));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(textEncoding.CompleteEmission).Message);
        Assert.False(textEncoding.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(textEncoding, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(textEncoding, property.GetValue(CreateDeclarations()));
        textEncoding.CompleteEmission();
        AssertFrozen(textEncoding);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.TextEncoding);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireTextEncoding).Message);
        runtime.BeginTextEncodingEmission();
        var textEncoding = runtime.RequireTextEncoding();
        Assert.Same(runtime.TextEncoding, textEncoding);
        Assert.Throws<InvalidOperationException>(runtime.BeginTextEncodingEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.TextEncoding))!.SetMethod!.IsPrivate);
        var declarations = CreateDeclarations();
        foreach (var property in Handles) property.SetValue(textEncoding, property.GetValue(declarations));
        textEncoding.CompleteEmission();
        AssertFrozen(textEncoding);
        Assert.Throws<InvalidOperationException>(runtime.BeginTextEncodingEmission);
    }

    [Fact]
    public void StagedTypeEmissionExposesDeclarationsBeforeFamilyCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("text_encoding_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var bufferType = module.DefineType("Buffer", TypeAttributes.Public);
        var buffer = new EmittedBufferRuntime(false) { Type = bufferType };
        buffer.Ctor = bufferType.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(byte[])]);
        buffer.Ctor.GetILGenerator().Emit(OpCodes.Ldarg_0);
        buffer.Ctor.GetILGenerator().Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        buffer.Ctor.GetILGenerator().Emit(OpCodes.Ret);
        buffer.GetData = bufferType.DefineMethod("GetData", MethodAttributes.Public, typeof(byte[]), Type.EmptyTypes);
        buffer.GetData.GetILGenerator().Emit(OpCodes.Ldnull);
        buffer.GetData.GetILGenerator().Emit(OpCodes.Ret);
        bufferType.CreateType();

        var textEncoding = new EmittedTextEncodingRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        emitter.EmitTSTextEncoderClass(module, textEncoding, buffer);
        Assert.True(textEncoding.EncoderType.IsCreated());
        Assert.Same(textEncoding.EncoderType, textEncoding.EncoderCtor.DeclaringType);
        Assert.Throws<InvalidOperationException>(() => textEncoding.DecoderType);
        Assert.False(textEncoding.IsComplete);
        emitter.EmitTSTextDecoderClass(module, textEncoding, buffer);
        Assert.True(textEncoding.DecoderType.IsCreated());
        Assert.Same(textEncoding.DecoderType, textEncoding.DecoderDecode.DeclaringType);
        Assert.Throws<InvalidOperationException>(() => textEncoding.DecodeMethodInvoke);
        Assert.False(textEncoding.IsComplete);
        emitter.EmitTSTextDecoderDecodeMethodClass(module, textEncoding, buffer);
        Assert.True(textEncoding.DecodeMethodType.IsCreated());
        textEncoding.CompleteEmission();
        AssertFrozen(textEncoding);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    [Theory]
    [InlineData("const value = 1;", false, false)]
    [InlineData("Buffer.from('only');", false, false)]
    [InlineData("new Uint8Array(2);", false, false)]
    [InlineData("import * as os from 'os';", false, false)]
    [InlineData("new TextEncoder();", true, false)]
    [InlineData("new TextDecoder();", true, false)]
    [InlineData("globalThis.TextEncoder;", true, false)]
    [InlineData("import { TextDecoder } from 'util'; new TextDecoder();", true, false)]
    [InlineData("const value = 1;", false, true)]
    [InlineData("new TextEncoder();", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void MinimalEnabledImpliedHostedAndFullEmissionPreserveFeatureSelection(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.TextEncoding is not null);
        if (enabled)
        {
            var textEncoding = runtime.RequireTextEncoding();
            AssertFrozen(textEncoding);
            Assert.Equal(7, Handles.Count());
            Assert.NotNull(runtime.Buffer);
            Assert.True(runtime.RequireBuffer().IsComplete);
            Assert.True(textEncoding.EncoderType.IsCreated());
            Assert.True(textEncoding.DecoderType.IsCreated());
            Assert.True(textEncoding.DecodeMethodType.IsCreated());
            Assert.Same(textEncoding.EncoderType, textEncoding.EncoderCtor.DeclaringType);
            Assert.Same(textEncoding.DecoderType, textEncoding.DecoderCtor.DeclaringType);
            Assert.Same(textEncoding.DecoderType, textEncoding.DecoderDecode.DeclaringType);
            Assert.Same(textEncoding.DecodeMethodType, textEncoding.DecodeMethodInvoke.DeclaringType);
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireTextEncoding);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "$TextEncoder", "$TextDecoder", "$TextDecoderDecodeMethod" })
            Assert.Equal(enabled, types.Contains(name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedTypesVerifyAndPreserveCurrentEncodingAndWrapperBehavior(bool hosted)
    {
        using var bytes = Save(EmitRuntime("new TextEncoder();", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var encoderType = assembly.GetType("$TextEncoder")!;
        var decoderType = assembly.GetType("$TextDecoder")!;
        var wrapperType = assembly.GetType("$TextDecoderDecodeMethod")!;
        var encoder = Activator.CreateInstance(encoderType)!;
        var decoder = Activator.CreateInstance(decoderType, [null, true, true])!;
        var encode = encoderType.GetMethod("Encode")!;
        var decode = decoderType.GetMethod("Decode")!;
        const string text = "h\u00e9\U0001F600";
        var buffer = encode.Invoke(encoder, [text])!;
        Assert.Equal(Encoding.UTF8.GetBytes(text), Assert.IsType<byte[]>(buffer.GetType().GetMethod("GetData")!.Invoke(buffer, null)));
        Assert.Equal(text, decode.Invoke(decoder, [buffer]));
        Assert.Equal(text, decode.Invoke(decoder, [Encoding.UTF8.GetBytes(text)]));
        Assert.Equal("", decode.Invoke(decoder, [null]));
        Assert.Equal("", decode.Invoke(decoder, [Array.Empty<byte>()]));
        Assert.Equal("", decode.Invoke(decoder, [new object()]));
        Assert.Equal("\uFFFD", decode.Invoke(decoder, [new byte[] { 233 }]));
        Assert.Equal("utf-8", decoderType.GetProperty("Encoding")!.GetValue(decoder));
        Assert.Equal(true, decoderType.GetMethod("get_Fatal")!.Invoke(decoder, null));
        Assert.Equal(true, decoderType.GetMethod("get_IgnoreBOM")!.Invoke(decoder, null));
        var unknown = Activator.CreateInstance(decoderType, ["not-a-real-label", false, false])!;
        Assert.Equal("not-a-real-label", decoderType.GetProperty("Encoding")!.GetValue(unknown));
        Assert.Equal("\uFFFD", decode.Invoke(unknown, [new byte[] { 233 }]));
        Assert.Equal("[object TextEncoder]", encoder.ToString());
        Assert.Equal("[object TextDecoder]", decoder.ToString());

        var wrapper = Activator.CreateInstance(wrapperType, [decoder])!;
        var invoke = wrapperType.GetMethod("Invoke")!;
        Assert.Equal(text, invoke.Invoke(wrapper, [new object[] { buffer }]));
        Assert.Equal(text, invoke.Invoke(wrapper, [new object[] { Encoding.UTF8.GetBytes(text) }]));
        Assert.Equal("", invoke.Invoke(wrapper, [Array.Empty<object>()]));
        Assert.Equal("", invoke.Invoke(wrapper, [null]));
        Assert.Equal("[Function: decode]", wrapper.ToString());
        var error = Assert.Throws<TargetInvocationException>(() => invoke.Invoke(wrapper, [new object[] { 123d }]));
        Assert.IsType<InvalidCastException>(error.InnerException);
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("new TextEncoder();", false, emitter).RequireTextEncoding();
        var minimal = EmitRuntime("const value = 1;", false, emitter);
        var second = EmitRuntime("new TextDecoder();", false, emitter).RequireTextEncoding();
        Assert.Null(minimal.TextEncoding);
        Assert.NotSame(first, second);
        foreach (var property in Handles) Assert.NotSame(property.GetValue(first), property.GetValue(second));
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static EmittedTextEncodingRuntime CreateDeclarations(string? missingHandle = null)
    {
        var textEncoding = new EmittedTextEncodingRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"text_encoding_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor : method;
            property.SetValue(textEncoding, handle);
        }
        return textEncoding;
    }

    private static void AssertFrozen(EmittedTextEncodingRuntime textEncoding)
    {
        Assert.True(textEncoding.IsComplete);
        Assert.Throws<InvalidOperationException>(textEncoding.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(textEncoding);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(textEncoding, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"text_encoding_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
