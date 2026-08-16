namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Shared self-signed certificate generator for the tls dual-mode tests.
/// CN=localhost with SAN DNS:localhost + IP:127.0.0.1; backticks escaped for JS template literals.
/// </summary>
public static class TlsModuleTestsCertHelper
{
    public static (string certPem, string keyPem) GenerateSelfSignedCert()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var certReq = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=localhost", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        var sanBuilder = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        certReq.CertificateExtensions.Add(sanBuilder.Build());

        var cert = certReq.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));

        return (cert.ExportCertificatePem().Replace("`", "\\`"), rsa.ExportPkcs8PrivateKeyPem().Replace("`", "\\`"));
    }
}
