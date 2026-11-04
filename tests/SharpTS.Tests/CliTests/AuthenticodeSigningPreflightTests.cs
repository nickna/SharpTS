using PEPacker;
using SharpTS.Cli;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace SharpTS.Tests.CliTests;

public sealed class AuthenticodeSigningPreflightTests
{
    [Theory]
    [InlineData("malformed")]
    [InlineData("empty")]
    [InlineData("public-certificate")]
    public async Task InvalidCertificateWinsOverUnavailableToolAndPreservesTheCompletedPe(string format)
    {
        using var fixture = new Fixture();
        byte[] bytes = format switch
        {
            "empty" => [],
            "public-certificate" => CertificateBytes(X509ContentType.Cert),
            _ => "deliberately invalid signing fixture"u8.ToArray()
        };
        File.WriteAllBytes(fixture.Certificate, bytes);
        byte[] original = SHA256.HashData(File.ReadAllBytes(fixture.Executable));

        var error = await Assert.ThrowsAsync<PEPackerException>(() =>
            AuthenticodeSigner.SignAsync(fixture.Executable, fixture.Options));

        Assert.Contains("PKCS#12", error.Message);
        Assert.DoesNotContain("Could not start", error.Message);
        Assert.DoesNotContain(fixture.Options.Password!, error.Message);
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(fixture.Executable)));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.signed*"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("fixture-password")]
    public async Task ValidPkcs12StillReachesTheSigningToolWithoutPreflightPasswordValidation(string password)
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.Certificate, CertificateBytes(X509ContentType.Pkcs12, password));
        // A different supplied password must also reach the tool: preflight only
        // recognizes PKCS#12; the tool retains password/private-key validation.
        var error = await Assert.ThrowsAsync<PEPackerException>(() =>
            AuthenticodeSigner.SignAsync(fixture.Executable, fixture.Options));
        Assert.Contains("Could not start the signing tool", error.Message);
        Assert.DoesNotContain(fixture.Options.Password!, error.Message);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.signed*"));
    }

    [Fact]
    public async Task ThumbprintSelectionKeepsItsPlatformBoundaryWithoutReadingAFile()
    {
        using var fixture = new Fixture();
        var options = fixture.Options with { Certificate = "sha1:" + new string('a', 40), Password = null };
        var error = await Assert.ThrowsAsync<PEPackerException>(() => AuthenticodeSigner.SignAsync(fixture.Executable, options));
        Assert.Contains(OperatingSystem.IsWindows() ? "Could not start the signing tool" : "require Windows SignTool", error.Message);
    }

    private static byte[] CertificateBytes(X509ContentType contentType, string? password = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=SharpTS signing preflight fixture", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        return certificate.Export(contentType, password);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpts-sign-preflight-" + Guid.NewGuid().ToString("N"));
        public string Executable => Path.Combine(Root, "completed.exe");
        public string Certificate => Path.Combine(Root, "input.pfx");
        public AuthenticodeSigningOptions Options => new()
        {
            Certificate = Certificate, Password = "must-not-log-this-password", TimestampUrl = null,
            ToolPath = Path.Combine(Root, "unavailable-signing-tool.exe")
        };
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            File.Copy(typeof(AuthenticodeSigningOptions).Assembly.Location, Executable);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
