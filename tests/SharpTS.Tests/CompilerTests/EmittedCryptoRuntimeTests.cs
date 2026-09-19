using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedCryptoRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedCryptoRuntime).GetProperties()
        .Where(property => property.Name != nameof(EmittedCryptoRuntime.IsComplete));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledFeatureHasNoMetadataOrCryptoTypes(bool hosted)
    {
        var runtime = EmitRuntime("console.log(1);", hosted);
        Assert.Null(runtime.Crypto);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireCrypto).Message);
        Assert.Null(runtime.GetBuiltInModuleMethod("crypto", "createHash"));
        Assert.Null(runtime.GetBuiltInModuleMethod("crypto", "getRandomValues"));
        Assert.DoesNotContain(runtime.RuntimeType.GetMethods(),
            method => method.Name.StartsWith("Crypto", StringComparison.Ordinal));

        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(stream);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name));
        foreach (var name in new[] { "$CryptoPrimitives", "$Hash", "$Hmac", "$Cipher", "$Decipher",
                     "$Sign", "$Verify", "$DiffieHellman", "$ECDH", "$TSKeyObject", "$X509Certificate",
                     "$CryptoKey", "$SubtleCrypto", "$WebCrypto" })
            Assert.DoesNotContain(name, names);

        // The shared global accessor is reserved even when the crypto feature is absent.
        var stub = reader.MethodDefinitions.Select(handle => reader.GetMethodDefinition(handle))
            .Single(method => reader.GetString(method.Name) == "GetWebCryptoObject");
        Assert.Equal(new byte[] { 0x14, 0x2a }, pe.GetMethodBody(stub.RelativeVirtualAddress).GetILBytes());
    }

    [Fact]
    public void FeatureStartsOnceAndEcdhDeclarationSupportsCallsBeforeBodyEmission()
    {
        var runtime = new EmittedRuntime();
        runtime.BeginCryptoEmission();
        var crypto = runtime.RequireCrypto();
        Assert.Same(crypto, runtime.Crypto);
        Assert.Throws<InvalidOperationException>(runtime.BeginCryptoEmission);
        Assert.False(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Crypto))!.SetMethod!.IsPublic);
        Assert.Contains("'ECDHGetMember'",
            Assert.Throws<InvalidOperationException>(() => crypto.ECDHGetMember).Message);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("crypto_forward"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        emitter.EmitTSECDHTypeDefinition(module, crypto);
        var getMember = crypto.ECDHGetMember;
        var type = module.DefineType("Caller");
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [crypto.ECDHType, typeof(string)]);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, getMember);
        il.Emit(OpCodes.Ret);
        Assert.False(crypto.ECDHType.IsCreated());
        Assert.False(crypto.IsComplete);
        Assert.Same(crypto.ECDHType, getMember.DeclaringType);
        getMember.GetILGenerator().Emit(OpCodes.Ldnull);
        getMember.GetILGenerator().Emit(OpCodes.Ret);
        crypto.ECDHType.CreateType();
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        Assert.Same(getMember, crypto.ECDHGetMember);
        Assert.Throws<InvalidOperationException>(crypto.CompleteEmission);
        Assert.Throws<ArgumentNullException>(() => crypto.ECDHGetMember = null!);
        Assert.Same(getMember, crypto.ECDHGetMember);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void CompletionRejectsEachMissingDeclarationAndCanBeRetried(string missingHandle)
    {
        var crypto = CreateDeclarations(missingHandle);
        var property = typeof(EmittedCryptoRuntime).GetProperty(missingHandle)!;
        var readError = Assert.Throws<TargetInvocationException>(() => property.GetValue(crypto));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(readError.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(crypto.CompleteEmission).Message);
        Assert.False(crypto.IsComplete);

        property.SetValue(crypto, property.GetValue(CreateDeclarations()));
        crypto.CompleteEmission();
        AssertFrozen(crypto);
    }

    [Theory]
    [InlineData("import * as crypto from 'crypto';", false)]
    [InlineData("import { createHash } from 'node:crypto';", false)]
    [InlineData("crypto.randomUUID();", false)]
    [InlineData("import * as crypto from 'crypto';", true)]
    [InlineData(null, false)]
    public void EnabledImpliedAndFullEmissionCompleteHandles(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var crypto = runtime.RequireCrypto();
        AssertFrozen(crypto);
        Assert.Equal("$ECDH", crypto.ECDHType.Name);
        Assert.Equal("$DiffieHellman", crypto.DiffieHellmanType.Name);
        Assert.True(crypto.ECDHType.IsCreated());
        Assert.True(crypto.DiffieHellmanType.IsCreated());
        Assert.Same(crypto.ECDHType, crypto.ECDHCtor.DeclaringType);
        Assert.Same(crypto.ECDHType, crypto.ECDHGetMember.DeclaringType);
        Assert.Same(crypto.DiffieHellmanType, crypto.DiffieHellmanCtorGroup.DeclaringType);
        Assert.Same(crypto.DiffieHellmanType, crypto.DHGetMember.DeclaringType);
        Assert.Equal("$CryptoPrimitives", crypto.HashData.DeclaringType!.Name);
        Assert.True(Assert.IsAssignableFrom<TypeBuilder>(crypto.SignCtor.DeclaringType).IsCreated());
        Assert.True(Assert.IsAssignableFrom<TypeBuilder>(crypto.VerifyCtor.DeclaringType).IsCreated());
        Assert.NotNull(runtime.GetBuiltInModuleMethod("crypto", "createHash"));
        Assert.NotNull(runtime.GetBuiltInModuleMethod("crypto", "generateKeyPair"));
        Assert.NotNull(runtime.GetBuiltInModuleMethod("crypto", "getRandomValues"));
        Assert.True(runtime.RequirePromise().IsComplete);
    }

    [Fact]
    public void CryptoDoesNotIntroduceGuestRuntimeDependencies()
    {
        var runtime = EmitRuntime("import * as crypto from 'crypto';");
        Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(stream);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.AssemblyReferences,
            handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
    }

    [Theory]
    [InlineData("console.log(crypto.createHash('sha256').update('abc').digest('hex')); const fn = createHash; console.log(fn('sha256').update('abc').digest('hex'));")]
    [InlineData("const key = Buffer.alloc(32); const iv = Buffer.alloc(16); const cipher = crypto.createCipheriv('aes-256-cbc', key, iv); cipher.update('abc'); cipher.final(); const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);")]
    [InlineData("crypto.pbkdf2Sync('p', 's', 2, 16, 'sha256'); crypto.scryptSync('p', 's', 16); crypto.hkdfSync('sha256', 'key', 'salt', 'info', 16);")]
    [InlineData("const pair = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' }); const s = crypto.createSign('sha256'); s.update('abc'); const sig = s.sign(pair.privateKey); const v = crypto.createVerify('sha256'); v.update('abc'); console.log(v.verify(pair.publicKey, sig));")]
    [InlineData("const a = crypto.createECDH('prime256v1'); a.generateKeys(); a.getPublicKey(); const dh = crypto.getDiffieHellman('modp14'); dh.getPrime();")]
    [InlineData("crypto.createSecretKey(Buffer.from('key')); crypto.hash('sha256', 'abc'); console.log(crypto.getCiphers().length, crypto.getHashes().length); crypto.generateKeySync('hmac', { length: 128 });")]
    [InlineData("new crypto.X509Certificate('invalid certificate');")]
    [InlineData("async function main() { const d = await crypto.subtle.digest('SHA-256', Buffer.from('abc')); console.log(Buffer.from(d).toString('hex')); } main();")]
    public void ModuleConsumersAndDeferredBodiesPassILVerification(string body)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = "import * as crypto from 'crypto'; import { createHash } from 'crypto';\n" + body
        };
        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void UserX509ClassStillRunsWhenCryptoIsAbsent()
    {
        const string source = """
            class X509Certificate { value: number = 42; }
            console.log(new X509Certificate().value);
            """;
        Assert.Null(EmitRuntime(source).Crypto);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("42\n", TestHarness.RunCompiledStandalone(source));
    }

    private static EmittedCryptoRuntime CreateDeclarations(string? missingHandle = null)
    {
        var crypto = new EmittedCryptoRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("crypto_incomplete"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor : method;
            property.SetValue(crypto, handle);
        }
        return crypto;
    }

    private static void AssertFrozen(EmittedCryptoRuntime crypto)
    {
        Assert.True(crypto.IsComplete);
        foreach (var property in Handles)
        {
            var value = property.GetValue(crypto);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var exception = Assert.Throws<TargetInvocationException>(() => property.SetValue(crypto, value));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }
        Assert.Throws<InvalidOperationException>(crypto.CompleteEmission);
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"crypto_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
