using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the crypto module's RSA encryption/decryption functions.
/// Uses interpreter mode for testing.
/// </summary>
public class CryptoRsaEncryptTests
{
    // Sample RSA key pair for testing (2048-bit)
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
    public void Crypto_PublicEncrypt_ReturnsBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const message = Buffer.from('Hello, World!');
                const encrypted = crypto.publicEncrypt(publicKey, message);

                console.log(Buffer.isBuffer(encrypted));
                console.log(encrypted.length === 256);  // RSA-2048 produces 256-byte output
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_PublicEncrypt_PrivateDecrypt_Roundtrip()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const privateKey = `{{TestPrivateKey}}`;
                const message = Buffer.from('Hello, RSA!');

                const encrypted = crypto.publicEncrypt(publicKey, message);
                const decrypted = crypto.privateDecrypt(privateKey, encrypted);

                console.log(decrypted.toString() === 'Hello, RSA!');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_PublicEncrypt_PrivateDecrypt_WithGeneratedKeyPair()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const { publicKey, privateKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
                const message = Buffer.from('Test message with generated keys');

                const encrypted = crypto.publicEncrypt(publicKey, message);
                const decrypted = crypto.privateDecrypt(privateKey, encrypted);

                console.log(decrypted.toString() === 'Test message with generated keys');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_PublicEncrypt_StringInput()
    {
        // publicEncrypt should also accept string input (UTF-8 encoded)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const privateKey = `{{TestPrivateKey}}`;

                const encrypted = crypto.publicEncrypt(publicKey, 'Hello');
                const decrypted = crypto.privateDecrypt(privateKey, encrypted);

                console.log(decrypted.toString() === 'Hello');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_PrivateDecrypt_FailsWithWrongKey()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                // Generate two different key pairs
                const kp1 = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
                const kp2 = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });

                const message = Buffer.from('Secret');
                const encrypted = crypto.publicEncrypt(kp1.publicKey, message);

                try {
                    // Try to decrypt with wrong private key
                    crypto.privateDecrypt(kp2.privateKey, encrypted);
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
    public void Crypto_EncryptionProducesDifferentOutputEachTime()
    {
        // OAEP padding includes randomness, so encrypting the same message twice
        // should produce different ciphertexts
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const message = Buffer.from('Same message');

                const encrypted1 = crypto.publicEncrypt(publicKey, message);
                const encrypted2 = crypto.publicEncrypt(publicKey, message);

                // Both should be 256 bytes
                console.log(encrypted1.length === 256);
                console.log(encrypted2.length === 256);
                // But they should be different due to OAEP randomness
                console.log(!encrypted1.equals(encrypted2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_PublicEncrypt_ThrowsOnInvalidKey()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                try {
                    crypto.publicEncrypt('not a valid key', Buffer.from('test'));
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
    public void Crypto_PublicEncrypt_MessageSizeLimit()
    {
        // RSA-2048 with OAEP-SHA1 can encrypt at most 214 bytes
        // (256 - 2*20 - 2 = 214 bytes for OAEP with SHA-1)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const privateKey = `{{TestPrivateKey}}`;

                // 200 bytes should work
                const smallMessage = Buffer.alloc(200, 66);
                const encrypted = crypto.publicEncrypt(publicKey, smallMessage);
                const decrypted = crypto.privateDecrypt(privateKey, encrypted);
                console.log(decrypted.length === 200);

                // 300 bytes should fail (too large for RSA-2048 with OAEP)
                const largeMessage = Buffer.alloc(300, 66);
                try {
                    crypto.publicEncrypt(publicKey, largeMessage);
                    console.log('no error for large');
                } catch (e) {
                    console.log('error for large');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\nerror for large\n", output);
    }
}
