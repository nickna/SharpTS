using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeCryptoKeyExportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HashingAndRandomBytesRunFromVerifiedOutput(bool native)
    {
        const string source = """
            import {createHash,randomBytes} from 'crypto';
            console.log(createHash('sha256').update('abc').digest('hex'));
            console.log(randomBytes(8).length);
            """;
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\n8\n", Run(source, native));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ActualPublicKeysPreserveValidAndInvalidSignatureResults(bool native, bool elliptic)
    {
        using AsymmetricAlgorithm key = elliptic ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : RSA.Create(2048);
        using AsymmetricAlgorithm wrong = elliptic ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : RSA.Create(2048);
        var name = new X500DistinguishedName("CN=Native key export regression");
        var request = key is ECDsa ec
            ? new CertificateRequest(name, ec, HashAlgorithmName.SHA256)
            : new CertificateRequest(name, (RSA)key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        string source = $$$"""
            import {X509Certificate,createPublicKey} from 'crypto';
            const certificate=new X509Certificate({{{JsonSerializer.Serialize(certificate.ExportCertificatePem())}}});
            const key=createPublicKey({{{JsonSerializer.Serialize(key.ExportSubjectPublicKeyInfoPem())}}});
            const wrong=createPublicKey({{{JsonSerializer.Serialize(wrong.ExportSubjectPublicKeyInfoPem())}}});
            console.log(certificate.verify(key),certificate.verify(wrong));
            """;
        Assert.Equal("true false\n", Run(source, native));
    }

    [Fact]
    public void NativeVerificationRefusesForeignExportCallbacksBeforeCallingThem()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Native key carrier", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        string source = $$$"""
            import {X509Certificate} from 'crypto';
            const certificate=new X509Certificate({{{JsonSerializer.Serialize(certificate.ExportCertificatePem())}}});
            let calls=0;
            class ForeignKey{export(options:any):any{calls++;return {{{JsonSerializer.Serialize(key.ExportSubjectPublicKeyInfoPem())}}};}}
            for(const key of [null,{},new ForeignKey()]){
                try{certificate.verify(key as any);}catch(error:any){console.log(error.name);}
            }
            console.log(calls);
            """;
        Assert.Equal("TypeError\nTypeError\nTypeError\n0\n", Run(source, native: true));
    }

    private static string Run(string source, bool native)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.GetPath("main.ts");
        var resolver = new ModuleResolver(entry, new Dictionary<string, string> { [entry] = source });
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker();
        var types = checker.CheckModules(modules, resolver);
        var compiler = new ILCompiler("crypto_keys_" + Guid.NewGuid().ToString("N"), false, true,
            null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.CompileModules(modules, resolver, types);
        byte[] bytes = compiler.SaveToBytes();
        using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
        return TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20));
    }
}
