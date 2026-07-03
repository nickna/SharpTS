using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for crypto.X509Certificate (#1064). Uses a fixed self-signed RSA-2048
/// certificate (CN=sharpts.test, SAN: DNS sharpts.test + *.wild.sharpts.test,
/// IP 127.0.0.1, email admin@sharpts.test; valid 2020-01-01 → 2050-01-01, CA:false).
/// Compiled-deferred members (checkEmail, toLegacyObject, infoAccess, email SAN
/// rendering) are tested interpreted-only.
/// </summary>
public class CryptoX509Tests
{
    private const string TestCertPem = """
-----BEGIN CERTIFICATE-----
MIIDZzCCAk+gAwIBAgIIC4tumCn1Q6kwDQYJKoZIhvcNAQELBQAwNjELMAkGA1UE
BhMCVVMxEDAOBgNVBAoTB1NoYXJwVFMxFTATBgNVBAMTDHNoYXJwdHMudGVzdDAg
Fw0yMDAxMDEwMDAwMDBaGA8yMDUwMDEwMTAwMDAwMFowNjELMAkGA1UEBhMCVVMx
EDAOBgNVBAoTB1NoYXJwVFMxFTATBgNVBAMTDHNoYXJwdHMudGVzdDCCASIwDQYJ
KoZIhvcNAQEBBQADggEPADCCAQoCggEBALF5dldsRxlu8SxxmM+5+lollPtLZdH2
Gu4By+6PdFWFIal1xLrstr0iAR31PkOMzyPxXgDtWR3OKFi6Zq656xnz+3BXZjKJ
ZGp3K0sGw0fv5EgZF0g/cf4LNYYnvquEE/sY9Y/VPhZEbmp9PbugmBwHRdMZikAs
Q1kfQ+kd5et10b8YGVFrY/8O70zNPsUs9vBiq+1+VkpQzoqGxNZJlXlavmtakiK9
qodkQJHrdD1EXbAqr9jF7lAwCPnQF1aBDb+nVcWlCInD/KeQ0hvcPTiNIiPoO2uW
+lHwSirrERCKa3waW1tkSqjrjAga59l0ZK7ooKgcf42nVATGumMyTikCAwEAAaN3
MHUwRgYDVR0RBD8wPYIMc2hhcnB0cy50ZXN0ghMqLndpbGQuc2hhcnB0cy50ZXN0
hwR/AAABgRJhZG1pbkBzaGFycHRzLnRlc3QwCQYDVR0TBAIwADALBgNVHQ8EBAMC
BaAwEwYDVR0lBAwwCgYIKwYBBQUHAwEwDQYJKoZIhvcNAQELBQADggEBADY1jq56
BVpoH4bpB6VWZEPDYyAuN9oU476zNpz+Kq4f4JJmItn+BTwZWQdNoGno0QliaIvH
nmiHh64u6p63SGZJTFqX9nRXnxclMpYz6E9yn2lMwdCWKRMbjbw7Bwgy2xac0ldi
LF/9NrwFP//52eQFxEpIZ02JUZdAMFbH1htkDOzpj+GQ67JH9JRdKq/Z6uJ8JYqZ
izy9JsAv0ebFfMuAk7Aspcz+3fDIjHFr356gPfTllTBDGMibLBe90mjGvPhv1qR0
GQQJL/Lh9fjaIpdQOH/FQJzlEhVbTxrQcZDMQXXlJ74m4WcID9n8tzPecbnlqOVV
sRSGRiV3zOrqtOo=
-----END CERTIFICATE-----
""";

    private const string TestPublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAsXl2V2xHGW7xLHGYz7n6
WiWU+0tl0fYa7gHL7o90VYUhqXXEuuy2vSIBHfU+Q4zPI/FeAO1ZHc4oWLpmrrnr
GfP7cFdmMolkancrSwbDR+/kSBkXSD9x/gs1hie+q4QT+xj1j9U+FkRuan09u6CY
HAdF0xmKQCxDWR9D6R3l63XRvxgZUWtj/w7vTM0+xSz28GKr7X5WSlDOiobE1kmV
eVq+a1qSIr2qh2RAket0PURdsCqv2MXuUDAI+dAXVoENv6dVxaUIicP8p5DSG9w9
OI0iI+g7a5b6UfBKKusREIprfBpbW2RKqOuMCBrn2XRkruigqBx/jadUBMa6YzJO
KQIDAQAB
-----END PUBLIC KEY-----
""";

    private static string Escape(string pem) => pem.Replace("\r\n", "\n").Replace("\n", "\\n");

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void X509_Parse_And_CoreProperties(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const pem = "{{Escape(TestCertPem)}}";
                const cert = new crypto.X509Certificate(pem);

                console.log(cert.subject.includes('CN=sharpts.test'));
                console.log(cert.subject.includes('O=SharpTS'));
                console.log(cert.issuer.includes('CN=sharpts.test'));
                console.log(cert.validFrom === 'Jan  1 00:00:00 2020 GMT');
                console.log(cert.validTo === 'Jan  1 00:00:00 2050 GMT');
                console.log(cert.serialNumber === '0B8B6E9829F543A9');
                console.log(cert.fingerprint256 === 'DC:E9:C1:6E:C6:9F:79:E4:3E:CF:66:23:32:05:50:6C:39:24:90:B2:32:8C:33:15:E0:8C:C3:59:06:8D:2E:B7');
                console.log(cert.ca === false);
                console.log(cert.raw.length > 500);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void X509_SubjectAltName_And_CheckHost(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const cert = new crypto.X509Certificate("{{Escape(TestCertPem)}}");

                console.log(cert.subjectAltName.includes('DNS:sharpts.test'));
                console.log(cert.subjectAltName.includes('DNS:*.wild.sharpts.test'));
                console.log(cert.subjectAltName.includes('IP Address:127.0.0.1'));

                console.log(cert.checkHost('sharpts.test') === 'sharpts.test');
                console.log(cert.checkHost('a.wild.sharpts.test') === 'a.wild.sharpts.test');
                console.log(cert.checkHost('nope.example') === undefined);
                console.log(cert.checkHost('deep.a.wild.sharpts.test') === undefined);
                console.log(cert.checkIP('127.0.0.1') === '127.0.0.1');
                console.log(cert.checkIP('10.0.0.1') === undefined);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void X509_Verify_PublicKey_And_ToString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const cert = new crypto.X509Certificate("{{Escape(TestCertPem)}}");
                const key = crypto.createPublicKey("{{Escape(TestPublicKeyPem)}}");

                console.log(cert.verify(key) === true);
                console.log(cert.publicKey.type === 'public');
                console.log(cert.publicKey.asymmetricKeyType === 'rsa');
                console.log(cert.toString().startsWith('-----BEGIN CERTIFICATE-----'));
                console.log(cert.checkIssued(cert) === true); // self-signed
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void X509_KeyUsage_And_ExtKeyUsage(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const cert = new crypto.X509Certificate("{{Escape(TestCertPem)}}");

                console.log(cert.keyUsage.includes('Digital Signature'));
                console.log(cert.keyUsage.includes('Key Encipherment'));
                console.log(cert.extKeyUsage.includes('1.3.6.1.5.5.7.3.1'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void X509_NamedImport_Construction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { X509Certificate } from 'crypto';

                const cert = new X509Certificate("{{Escape(TestCertPem)}}");
                console.log(cert.serialNumber === '0B8B6E9829F543A9');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void X509_FromBuffer_Pem(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const cert = new crypto.X509Certificate(Buffer.from("{{Escape(TestCertPem)}}"));
                console.log(cert.serialNumber === '0B8B6E9829F543A9');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    // --- interpreter-only surface (compiled defers: checkEmail, toLegacyObject, email SANs) ---

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void X509_CheckEmail_And_LegacyObject_InterpOnly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const cert = new crypto.X509Certificate("{{Escape(TestCertPem)}}");

                console.log(cert.subjectAltName.includes('email:admin@sharpts.test'));
                console.log(cert.checkEmail('admin@sharpts.test') === 'admin@sharpts.test');
                console.log(cert.checkEmail('other@sharpts.test') === undefined);

                const legacy = cert.toLegacyObject();
                console.log(legacy.subject.CN === 'sharpts.test');
                console.log(legacy.serialNumber === '0B8B6E9829F543A9');
                console.log(legacy.fingerprint256 === cert.fingerprint256);
                console.log(legacy.bits === 2048);

                console.log(cert.validFromDate.getUTCFullYear() === 2020);
                console.log(cert.validToDate.getUTCFullYear() === 2050);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void X509_ValidDates_DualMode(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const d = new Date(0); // ensure Date support is tree-shaken IN for compiled mode
                const cert = new crypto.X509Certificate("{{Escape(TestCertPem)}}");
                console.log(cert.validFromDate.getUTCFullYear() === 2020);
                console.log(cert.validToDate.getUTCFullYear() === 2050);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }
}
