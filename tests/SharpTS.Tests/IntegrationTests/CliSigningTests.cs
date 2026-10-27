using System.Formats.Asn1;
using System.Reflection.PortableExecutable;
using SharpTS.Cli;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public class CliSigningTests
{
    [Fact]
    public void SigningFailurePreservesExecutableAndDoesNotPrintPassword()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var source = directory.CreateFile("hello.ts", "console.log(42);");
        var certificate = directory.CreateFile("invalid.pfx", "deliberately invalid signing fixture");
        var result = CliTestHelper.RunCli($"-c \"{source}\" -t exe --sign \"{certificate}\" --sign-pass fixture-password --sign-timestamp none", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(directory.GetPath("hello.exe")));
        Assert.Contains("signing", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fixture-password", result.StandardError + result.StandardOutput);
        Assert.DoesNotContain(Directory.GetFiles(directory.Path), file => file.Contains(".signed", StringComparison.Ordinal));
        if (OperatingSystem.IsWindows())
        {
            var preserved = RunExecutable(directory.GetPath("hello.exe"), directory.Path);
            Assert.Equal(0, preserved.ExitCode);
            Assert.Equal("42", preserved.StandardOutput.Trim());
        }
    }

    [Fact]
    public void WindowsFinalBundleIsSignedWithTheSelectedFileCertificateAndStillRuns()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = CliTestHelper.CreateTempDirectory();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=SharpTS ephemeral signing test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.3") }, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var pfx = directory.GetPath("test.pfx");
        File.WriteAllBytes(pfx, certificate.Export(X509ContentType.Pkcs12, "fixture-password"));
        var source = directory.CreateFile("hello.ts", "console.log(42);");
        var result = CliTestHelper.RunCli($"-c \"{source}\" -t exe --sign \"{pfx}\" --sign-pass fixture-password --sign-timestamp none", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardError);
        using var signer = ReadEmbeddedCertificate(directory.GetPath("hello.exe"));
        Assert.Equal(certificate.Thumbprint, signer.Thumbprint);
        if (Environment.GetEnvironmentVariable("SHARPTS_TEST_OSSLSIGNCODE") is { Length: > 0 } verifier)
        {
            string publicCertificate = directory.CreateFile("test-cert.pem", certificate.ExportCertificatePem());
            var verify = new ProcessStartInfo(verifier)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = directory.Path
            };
            foreach (string argument in new[] { "verify", "-CAfile", publicCertificate, "-in", directory.GetPath("hello.exe") })
                verify.ArgumentList.Add(argument);
            var verification = TestProcess.Run(verify, CliTestHelper.DefaultTimeout);
            Assert.True(verification.ExitCode == 0, verification.StandardOutput + verification.StandardError);
            Assert.Contains("Signature verification: ok", verification.StandardOutput + verification.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        var run = TestProcess.Run(new ProcessStartInfo(directory.GetPath("hello.exe"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = directory.Path
        }, CliTestHelper.DefaultTimeout);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("42", run.StandardOutput.Trim());
    }

    [Fact]
    public void SigningRejectsConfiguredDeclarationOnlyBeforeWritingOutput()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(42);");
        directory.CreateFile("tsconfig.json", "{\"compilerOptions\":{\"declaration\":true,\"emitDeclarationOnly\":true}}");
        var result = CliTestHelper.RunCli("-c main.ts -t exe --sign absent.pfx", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("after applying tsconfig", result.StandardOutput + result.StandardError);
        Assert.False(File.Exists(directory.GetPath("main.exe")));
        Assert.False(File.Exists(directory.GetPath("main.d.ts")));
    }

    [Fact]
    public void FailedSigningStopsBeforePackaging()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(42);");
        directory.CreateFile("invalid.pfx", "not a certificate");
        var result = CliTestHelper.RunCli("-c main.ts -t exe --sign invalid.pfx --sign-timestamp none --pack --package-id SigningFixture --version 1.0.0", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(directory.GetPath("main.exe")));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.nupkg", SearchOption.AllDirectories));
    }

    [Fact]
    public void ExecutableCompilationWithoutSigningRetainsItsDefaultBehavior()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(42);");
        var result = CliTestHelper.RunCli("-c main.ts -t exe --verify", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardError);
        Assert.DoesNotContain("Signed ", result.StandardOutput);
        var run = RunExecutable(directory.GetPath("main.exe"), directory.Path);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("42", run.StandardOutput.Trim());
    }

    [Fact]
    public async Task RejectedCertificatePreservesTheExactCliBundleBytes()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(42);");
        var compiled = CliTestHelper.RunCli("-c main.ts -t exe --verify", directory.Path);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        string executable = directory.GetPath("main.exe");
        byte[] original = SHA256.HashData(File.ReadAllBytes(executable));
        string invalidCertificate = directory.CreateFile("invalid.pfx", "not a certificate");
        await Assert.ThrowsAsync<PEPacker.PEPackerException>(() => AuthenticodeSigner.SignAsync(executable,
            new AuthenticodeSigningOptions { Certificate = invalidCertificate, TimestampUrl = null }));
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(executable)));
        Assert.Equal("42", RunExecutable(executable, directory.Path).StandardOutput.Trim());
    }

    private static X509Certificate2 ReadEmbeddedCertificate(string executable)
    {
        byte[] bytes = File.ReadAllBytes(executable);
        using var pe = new PEReader(new MemoryStream(bytes));
        int certificateOffset = pe.PEHeaders.PEHeader!.CertificateTableDirectory.RelativeVirtualAddress;
        Assert.True(certificateOffset > 0);
        int length = BitConverter.ToInt32(bytes, certificateOffset);
        var contentInfo = new AsnReader(bytes.AsMemory(certificateOffset + 8, length - 8), AsnEncodingRules.BER).ReadSequence();
        Assert.Equal("1.2.840.113549.1.7.2", contentInfo.ReadObjectIdentifier());
        var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)).ReadSequence();
        signedData.ReadInteger();
        signedData.ReadEncodedValue(); // digestAlgorithms
        signedData.ReadEncodedValue(); // encapContentInfo
        var certificates = signedData.ReadSetOf(skipSortOrderValidation: true,
            expectedTag: new Asn1Tag(TagClass.ContextSpecific, 0, true));
        return X509CertificateLoader.LoadCertificate(certificates.ReadEncodedValue().Span);
    }

    private static TestProcessResult RunExecutable(string path, string workingDirectory) => TestProcess.Run(
        new ProcessStartInfo(path)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        }, CliTestHelper.DefaultTimeout);
}
