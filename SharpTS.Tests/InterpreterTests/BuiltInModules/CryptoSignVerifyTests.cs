using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the crypto module's createSign and createVerify functions.
/// Uses interpreter mode for testing.
/// </summary>
public class CryptoSignVerifyTests
{
    // Sample RSA key pair for testing (2048-bit, generated for testing purposes only)
    private const string TestPrivateKey = """
-----BEGIN PRIVATE KEY-----
MIIEvAIBADANBgkqhkiG9w0BAQEFAASCBKYwggSiAgEAAoIBAQDQ9wlSXsD5OUJ3
vIhuDuW1J6YN5dvbxAxqZRe4Cm2BXbY1l+tC//x9iZKrkKBbtsBjl3U2WXpwpcWf
rpqdMrQmxeb1gVHwHUrOamdO5+SG8Zy0ojKWxPUIkahHA5wRacsApLetHAS5N5US
vlzymEhs/jfhGpcl21fAH72kJuxoRF19tNWACSSb+DpeOHXWHm/PqenFI1ZShK+X
qkxeFBN8hHUUhhTxdd+LH7n9ImhhDxb6y9Nlio5lZrfHzPLtZ/EHTIyaxV80wHZ0
H4FER0kybl5Jso0VYBKMEZkWrOto9LlN/EmXmCdkJIZhGjVR/noH1v2jC/xDZ5n9
8khHsi/lAgMBAAECggEAEC9SFYMpRyRcNZHwrzWQLRvJDMKE6NyiaYsy7xo/qQlt
F3GQ0zuofsCtD4TAJtpcxFnyxibgCOGOEPQhHZPTyD0DyngdtI9QP/SV09K6LImC
LatyZ6MRp3xAoF9zMxYSlxYq88l7xCy96xm7cT7CPU7jXRgGJPR8M3FB6vjozpp5
GYfByegTEzyBiZ689H9Q2syhU+F9MJnQzZsyJLZ1RsLcKOnqCifhWWiDMjqt+EKU
WQc72dxaveekhp/ISNzo0iCMGCxDi9i2ModAg9wURB7aB7MTIiChzHEQ6hmSeXy0
feiTLTsvG7e99uSiV/GBV2FtTQ+kqpaUQtZ8uezggQKBgQDvjp7UtrjdNJBQmRPP
fbGg8uwXoKpVkeZt1F1u0TPJx6UPYqwMQcHGJCVaHhUQ8sI8Sppf+ySc3onR9ix0
Oai7CGR3hM/Nhy8bf+0h8qUbuSuItlrF+J7lwsLymlMQkN/X3w/bnudN7sLSjTsu
oKB8rbLI2q62lalBVEAHtBIozQKBgQDfTt3A9GP/0r/UexfPiaJmqTl6KSwWos/Q
A6dBNhmugwmdLtJru+mVZ0mgFgfiepQJG1W38ynL4/GNv6g3QYFlP/9fFfDxfvRN
rYRTDtRnKNBXxE712pkInzPAWYcDpEcDMVdLLqLppxDslWeJlr71cJiSdp1WG7n3
WxVTUSyDeQKBgEsnIwz4heZfpyah32UouaEUlJyU+tr9epzaErXBS83xpAa/ndn6
hx/yFwW+ij1W6zie7u9Nip7r8bC82hVcQWLrrxkPwWFpF445A9uyk7muzcmF69RP
uwm5oA8b+xMnYBIJGKB9qXL5hIUpaXenTLHQjFYWxNji+sZT+AJyq3/BAoGAN3qG
iVuuRG59jjKOtdcB6/N6/iigdXc5nfpqYT8pnjub9dseF/n1jFK+7fDLQK8nfCO4
Zh0ZczhMWOUWy7OQjDEcJulylOzvkSTczS3QA1kWedehrl8CyiuTVeRoMLVtlxN5
FoqdmuMQx1ZPBNXY122D2k9xw2TcDOIqKCrwnjECgYAv24vgAfSQRZLd3MrK41El
xGWRu1CAYmdC2UXV92cJpGAB4irVxs+E7u9qTu1zqZA5ZzbRzz1uxgnhKenC62uh
cWwcgaiz9LOlCil1gb3bazz0V6HiWrmi++soWhPPNMYSgE002KFHq58G2e1nwBmU
pYlA6+GlII/hM4c3iRjCyQ==
-----END PRIVATE KEY-----
""";

    private const string TestPublicKey = """
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0PcJUl7A+TlCd7yIbg7l
tSemDeXb28QMamUXuAptgV22NZfrQv/8fYmSq5CgW7bAY5d1Nll6cKXFn66anTK0
JsXm9YFR8B1KzmpnTufkhvGctKIylsT1CJGoRwOcEWnLAKS3rRwEuTeVEr5c8phI
bP434RqXJdtXwB+9pCbsaERdfbTVgAkkm/g6Xjh11h5vz6npxSNWUoSvl6pMXhQT
fIR1FIYU8XXfix+5/SJoYQ8W+svTZYqOZWa3x8zy7WfxB0yMmsVfNMB2dB+BREdJ
Mm5eSbKNFWASjBGZFqzraPS5TfxJl5gnZCSGYRo1Uf56B9b9owv8Q2eZ/fJIR7Iv
5QIDAQAB
-----END PUBLIC KEY-----
""";

    [Fact]
    public void Crypto_CreateSign_ReturnsSignObject()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const signer = crypto.createSign('sha256');
                console.log(typeof signer === 'object');
                console.log(typeof signer.update === 'function');
                console.log(typeof signer.sign === 'function');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateVerify_ReturnsVerifyObject()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const verifier = crypto.createVerify('sha256');
                console.log(typeof verifier === 'object');
                console.log(typeof verifier.update === 'function');
                console.log(typeof verifier.verify === 'function');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_Sign_UpdateReturnsThis()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const signer = crypto.createSign('sha256');
                const result = signer.update('test');
                console.log(result === signer);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_Verify_UpdateReturnsThis()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const verifier = crypto.createVerify('sha256');
                const result = verifier.update('test');
                console.log(result === verifier);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_Sign_SupportsRsaSha256Algorithm()
    {
        // RSA-SHA256 prefixed algorithm should work the same as sha256
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const signer = crypto.createSign('RSA-SHA256');
                console.log(typeof signer === 'object');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_Sign_SupportsSha384Algorithm()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const signer = crypto.createSign('sha384');
                console.log(typeof signer === 'object');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_Sign_SupportsSha512Algorithm()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const signer = crypto.createSign('sha512');
                console.log(typeof signer === 'object');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_SignAndVerify_RoundTrip()
    {
        // Test that we can sign data and verify it
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;
                const publicKey = `{{TestPublicKey}}`;

                // Sign the data
                const signer = crypto.createSign('sha256');
                signer.update('Hello, World!');
                const signature = signer.sign(privateKey, 'hex');

                console.log(typeof signature === 'string');
                console.log(signature.length > 0);

                // Verify the signature
                const verifier = crypto.createVerify('sha256');
                verifier.update('Hello, World!');
                const isValid = verifier.verify(publicKey, signature, 'hex');

                console.log(isValid === true);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_Verify_ReturnsFalseForInvalidSignature()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;
                const publicKey = `{{TestPublicKey}}`;

                // Sign the data
                const signer = crypto.createSign('sha256');
                signer.update('Hello, World!');
                const signature = signer.sign(privateKey, 'hex');

                // Try to verify with different data
                const verifier = crypto.createVerify('sha256');
                verifier.update('Different data');
                const isValid = verifier.verify(publicKey, signature, 'hex');

                console.log(isValid === false);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_Sign_MultipleUpdates()
    {
        // Multiple updates should work the same as a single update with concatenated data
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;
                const publicKey = `{{TestPublicKey}}`;

                // Sign with single update
                const signer1 = crypto.createSign('sha256');
                signer1.update('HelloWorld');
                const sig1 = signer1.sign(privateKey, 'hex');

                // Sign with multiple updates
                const signer2 = crypto.createSign('sha256');
                signer2.update('Hello');
                signer2.update('World');
                const sig2 = signer2.sign(privateKey, 'hex');

                // Both should produce the same signature
                console.log(sig1 === sig2);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_Sign_MethodChaining()
    {
        // Test that update().sign() chaining works
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;

                const signature = crypto.createSign('sha256').update('test').sign(privateKey, 'hex');
                console.log(typeof signature === 'string');
                console.log(signature.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_Sign_Base64Encoding()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;

                const signer = crypto.createSign('sha256');
                signer.update('test');
                const signature = signer.sign(privateKey, 'base64');

                console.log(typeof signature === 'string');
                // Base64 encoded RSA-2048 signature should be around 344 chars
                console.log(signature.length > 100);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_SignAndVerify_Base64()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;
                const publicKey = `{{TestPublicKey}}`;

                // Sign with base64 encoding
                const signer = crypto.createSign('sha256');
                signer.update('test data');
                const signature = signer.sign(privateKey, 'base64');

                // Verify with base64 encoding
                const verifier = crypto.createVerify('sha256');
                verifier.update('test data');
                const isValid = verifier.verify(publicKey, signature, 'base64');

                console.log(isValid === true);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_Sign_ReturnsBuffer_WhenNoEncoding()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;

                const signer = crypto.createSign('sha256');
                signer.update('test');
                const signature = signer.sign(privateKey);

                console.log(Buffer.isBuffer(signature));
                // RSA-2048 signature should be 256 bytes
                console.log(signature.length === 256);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_Sign_ThrowsOnUnsupportedAlgorithm()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                try {
                    crypto.createSign('unsupported');
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    [Fact]
    public void Crypto_Sign_ThrowsOnDoubleSign()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;

                const signer = crypto.createSign('sha256');
                signer.update('test');
                signer.sign(privateKey, 'hex');

                try {
                    signer.sign(privateKey, 'hex');
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    [Fact]
    public void Crypto_Sign_ThrowsOnUpdateAfterSign()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;

                const signer = crypto.createSign('sha256');
                signer.update('test');
                signer.sign(privateKey, 'hex');

                try {
                    signer.update('more data');
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    [Fact]
    public void Crypto_Verify_ThrowsOnDoubleVerify()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;

                const verifier = crypto.createVerify('sha256');
                verifier.update('test');
                verifier.verify(publicKey, 'deadbeef', 'hex');

                try {
                    verifier.verify(publicKey, 'deadbeef', 'hex');
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }
}
