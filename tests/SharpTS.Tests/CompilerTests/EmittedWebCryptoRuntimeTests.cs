using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedWebCryptoRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedWebCryptoImplementation).GetProperties()
        .Where(property => property.Name != nameof(EmittedWebCryptoImplementation.IsComplete));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Fact]
    public void RequiredAccessorSupportsForwardCallsAndFreezesWithoutAnImplementation()
    {
        var webCrypto = new EmittedRuntime().WebCrypto;
        Assert.Null(webCrypto.Implementation);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(webCrypto.RequireImplementation).Message);
        Assert.Contains("'GetObject'", Assert.Throws<InvalidOperationException>(webCrypto.CompleteEmission).Message);
        Assert.False(webCrypto.IsComplete);
        Assert.Throws<ArgumentNullException>(() => webCrypto.GetObject = null!);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("webcrypto_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Caller");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        emitter.DeclareGetWebCryptoObject(type, webCrypto);
        var accessor = webCrypto.GetObject;
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, accessor);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(type.IsCreated());
        emitter.EmitGetWebCryptoObjectStub(webCrypto);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        var loaded = Assembly.Load(stream.ToArray());
        Assert.Null(loaded.GetType("Caller")!.GetMethod("Call")!.Invoke(null, null));
        webCrypto.CompleteEmission();
        Assert.Same(accessor, webCrypto.GetObject);
        AssertFrozen(webCrypto);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(webCrypto.RequireImplementation).Message);
    }

    [Fact]
    public void ImplementationStartsOnceAndCannotCompleteWithoutTheAccessor()
    {
        var webCrypto = new EmittedRuntime().WebCrypto;
        webCrypto.BeginImplementationEmission();
        var implementation = webCrypto.RequireImplementation();
        Assert.Same(implementation, webCrypto.Implementation);
        Assert.Throws<InvalidOperationException>(webCrypto.BeginImplementationEmission);
        Assert.Contains("'GetObject'", Assert.Throws<InvalidOperationException>(webCrypto.CompleteEmission).Message);
        Assert.False(webCrypto.IsComplete);
        Assert.False(implementation.IsComplete);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.WebCrypto))!.SetMethod);
        Assert.False(typeof(EmittedWebCryptoRuntime).GetProperty(nameof(EmittedWebCryptoRuntime.Implementation))!.SetMethod!.IsPublic);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void CompletionRejectsEachMissingImplementationHandleAndCanBeRetried(string missingHandle)
    {
        var webCrypto = CreateDeclarations(missingHandle);
        var implementation = webCrypto.RequireImplementation();
        var property = typeof(EmittedWebCryptoImplementation).GetProperty(missingHandle)!;
        var readError = Assert.Throws<TargetInvocationException>(() => property.GetValue(implementation));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(readError.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(webCrypto.CompleteEmission).Message);
        Assert.False(webCrypto.IsComplete);
        Assert.False(implementation.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(implementation, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);

        property.SetValue(implementation, property.GetValue(CreateDeclarations().RequireImplementation()));
        webCrypto.CompleteEmission();
        AssertFrozen(webCrypto);
    }

    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("console.log(1);", true)]
    [InlineData("new Uint8Array(2);", false)]
    [InlineData("import * as tls from 'tls';", false)]
    public void DisabledFeatureCompletesOnlyTheRequiredStub(string source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var webCrypto = runtime.WebCrypto;
        Assert.Null(webCrypto.Implementation);
        AssertFrozen(webCrypto);
        Assert.Null(runtime.BuiltInModules.GetOptional("crypto", "getRandomValues"));
        Assert.Same(runtime.RuntimeClass.Type, webCrypto.GetObject.DeclaringType);

        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name));
        foreach (var name in new[] { "$CryptoKey", "$SubtleCrypto", "$WebCrypto" })
            Assert.DoesNotContain(name, names);
        var methods = reader.MethodDefinitions.Select(reader.GetMethodDefinition).ToArray();
        Assert.DoesNotContain(methods, method => reader.GetString(method.Name).StartsWith("Wc", StringComparison.Ordinal));
        var stub = methods.Single(method => reader.GetString(method.Name) == "GetWebCryptoObject");
        Assert.Equal(new byte[] { 0x14, 0x2a }, pe.GetMethodBody(stub.RelativeVirtualAddress).GetILBytes());
    }

    [Theory]
    [InlineData("import * as crypto from 'crypto';", false)]
    [InlineData("import { subtle } from 'node:crypto';", false)]
    [InlineData("crypto.subtle;", false)]
    [InlineData("import * as crypto from 'crypto';", true)]
    [InlineData(null, false)]
    public void EnabledImpliedAndFullEmissionCompleteImplementation(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var webCrypto = runtime.WebCrypto;
        var implementation = webCrypto.RequireImplementation();
        AssertFrozen(webCrypto);
        Assert.True(runtime.RequireCrypto().IsComplete);
        Assert.True(runtime.RequirePromise().IsComplete);
        Assert.Equal("$CryptoKey", implementation.CryptoKeyType.Name);
        Assert.Equal("$WebCrypto", implementation.Type.Name);
        Assert.True(implementation.CryptoKeyType.IsCreated());
        Assert.True(implementation.Type.IsCreated());
        Assert.Same(implementation.CryptoKeyType, implementation.CryptoKeyCtor.DeclaringType);
        Assert.Same(implementation.CryptoKeyType, implementation.KeyMaterialField.DeclaringType);
        Assert.Same(implementation.Type, implementation.GetRandomValues.DeclaringType);
        Assert.Equal("$SubtleCrypto", implementation.SubtleCtor.DeclaringType!.Name);
        Assert.True(Assert.IsAssignableFrom<TypeBuilder>(implementation.SubtleCtor.DeclaringType).IsCreated());
        Assert.Same(implementation.SubtleCtor.DeclaringType, implementation.SubtleDeriveBitsCore.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, implementation.Digest.DeclaringType);
        Assert.NotNull(runtime.BuiltInModules.GetOptional("crypto", "getRandomValues"));
    }

    [Fact]
    public void AccessorReturnsOneSingletonWithoutIntroducingGuestRuntimeDependencies()
    {
        var runtime = EmitRuntime("import * as crypto from 'crypto';");
        Assert.Empty(runtime.Deployment.Reasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, runtime.Deployment.Requirements);
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.AssemblyReferences,
            handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
        var loaded = Assembly.Load(stream.ToArray());
        var getter = loaded.GetType(runtime.RuntimeClass.Type.Name)!.GetMethod(runtime.WebCrypto.GetObject.Name)!;
        var first = getter.Invoke(null, null);
        Assert.NotNull(first);
        Assert.Equal("$WebCrypto", first.GetType().Name);
        Assert.Same(first, getter.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(crypto.webcrypto.subtle === crypto.subtle); const fn = getRandomValues; fn(new Uint8Array(16));")]
    [InlineData("await crypto.subtle.digest('SHA-256', Buffer.from('abc'));")]
    [InlineData("const key = await crypto.subtle.importKey('raw', Buffer.from('secret'), { name: 'HMAC', hash: 'SHA-256' }, true, ['sign', 'verify']); const sig = await crypto.subtle.sign('HMAC', key, Buffer.from('abc')); await crypto.subtle.verify('HMAC', key, sig, Buffer.from('abc')); await crypto.subtle.exportKey('raw', key);")]
    [InlineData("const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 128 }, true, ['encrypt', 'decrypt']); const iv = new Uint8Array(12); const ct = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, Buffer.from('abc')); await crypto.subtle.decrypt({ name: 'AES-GCM', iv }, key, ct);")]
    [InlineData("const pair = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']); const sig = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, pair.privateKey, Buffer.from('abc')); await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, pair.publicKey, sig, Buffer.from('abc')); await crypto.subtle.exportKey('spki', pair.publicKey);")]
    [InlineData("const key = await crypto.subtle.importKey('raw', Buffer.from('password'), 'PBKDF2', false, ['deriveBits', 'deriveKey']); const algorithm = { name: 'PBKDF2', salt: Buffer.from('salt'), iterations: 2, hash: 'SHA-256' }; await crypto.subtle.deriveBits(algorithm, key, 128); await crypto.subtle.deriveKey(algorithm, key, { name: 'AES-CBC', length: 128 }, true, ['encrypt']);")]
    [InlineData("try { await crypto.subtle.digest('invalid', Buffer.from('abc')); } catch (error) { console.log('rejected'); }")]
    public void ModuleConsumersAndPromiseWrappersPassILVerification(string body)
    {
        var source = "import * as crypto from 'crypto'; import { getRandomValues } from 'crypto';\n"
            + "async function main() {\n" + body + "\n} main();";
        var errors = TestHarness.CompileModulesAndVerifyOnly(new() { ["main.ts"] = source }, "main.ts");
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static EmittedWebCryptoRuntime CreateDeclarations(string? missingHandle = null)
    {
        var webCrypto = new EmittedRuntime().WebCrypto;
        webCrypto.BeginImplementationEmission();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("webcrypto_incomplete"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        webCrypto.GetObject = method;
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(webCrypto.RequireImplementation(), handle);
        }
        return webCrypto;
    }

    private static void AssertFrozen(EmittedWebCryptoRuntime webCrypto)
    {
        Assert.True(webCrypto.IsComplete);
        Assert.Throws<InvalidOperationException>(webCrypto.CompleteEmission);
        Assert.Throws<InvalidOperationException>(webCrypto.BeginImplementationEmission);
        Assert.Throws<InvalidOperationException>(() => webCrypto.GetObject = webCrypto.GetObject);
        Assert.False(typeof(EmittedWebCryptoRuntime).GetProperty(nameof(EmittedWebCryptoRuntime.GetObject))!.SetMethod!.IsPublic);
        if (webCrypto.Implementation is not { } implementation)
            return;
        Assert.True(implementation.IsComplete);
        foreach (var property in Handles)
        {
            var value = property.GetValue(implementation);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var exception = Assert.Throws<TargetInvocationException>(() => property.SetValue(implementation, value));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }
        Assert.Throws<InvalidOperationException>(implementation.CompleteEmission);
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"webcrypto_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
