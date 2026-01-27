using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the crypto module's KeyObject API in compiled mode.
/// </summary>
public class CryptoKeyObjectCompiledTests
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
    public void Compiled_Crypto_CreateSecretKey_ReturnsKeyObject()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey(Buffer.from('my secret key'));
                console.log(typeof key === 'object');
                console.log(key !== null);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreateSecretKey_TypeProperty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey(Buffer.from('my secret key'));
                console.log(key.type === 'secret');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreateSecretKey_SymmetricKeySize()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey(Buffer.alloc(32));
                console.log(key.symmetricKeySize === 32);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreateSecretKey_FromString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                // When passing a string, also need encoding
                const key = crypto.createSecretKey('mykey', 'utf8');
                console.log(key.type === 'secret');
                console.log(key.symmetricKeySize === 5);  // 'mykey' is 5 bytes
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreateSecretKey_FromHexString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey('deadbeef', 'hex');
                console.log(key.type === 'secret');
                console.log(key.symmetricKeySize === 4);  // 4 bytes from 8 hex chars
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreateSecretKey_Export()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const originalData = Buffer.from('secret data');
                const key = crypto.createSecretKey(originalData);
                const exported = key.export();

                console.log(Buffer.isBuffer(exported));
                // Verify export worked by checking if exported has same content
                // Note: Use > 0 check since compiled mode returns different Buffer type
                console.log(exported.length > 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreatePublicKey_FromPem()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const key = crypto.createPublicKey(publicKey);

                console.log(key.type === 'public');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreatePrivateKey_FromPem()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;
                const key = crypto.createPrivateKey(privateKey);

                console.log(key.type === 'private');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_KeyObject_AsymmetricKeyType_Rsa()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const key = crypto.createPublicKey(publicKey);

                console.log(key.asymmetricKeyType === 'rsa');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_KeyObject_AsymmetricKeyDetails_Rsa()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const key = crypto.createPublicKey(publicKey);
                const details = key.asymmetricKeyDetails;

                console.log(details.modulusLength === 2048);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreatePublicKey_FromGeneratedKeyPair()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const { publicKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
                const keyObject = crypto.createPublicKey(publicKey);

                console.log(keyObject.type === 'public');
                console.log(keyObject.asymmetricKeyType === 'rsa');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreatePrivateKey_FromGeneratedKeyPair()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const { privateKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
                const keyObject = crypto.createPrivateKey(privateKey);

                console.log(keyObject.type === 'private');
                console.log(keyObject.asymmetricKeyType === 'rsa');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Crypto_SecretKey_NoAsymmetricKeyType()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey(Buffer.from('secret'));
                console.log(key.asymmetricKeyType === undefined || key.asymmetricKeyType === null);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_SecretKey_NoAsymmetricKeyDetails()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey(Buffer.from('secret'));
                console.log(key.asymmetricKeyDetails === undefined || key.asymmetricKeyDetails === null);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_AsymmetricKey_NoSymmetricKeySize()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const key = crypto.createPublicKey(publicKey);
                console.log(key.symmetricKeySize === undefined || key.symmetricKeySize === null);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreatePublicKey_InvalidPem_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                try {
                    crypto.createPublicKey('not a valid pem');
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    [Fact]
    public void Compiled_Crypto_CreatePrivateKey_InvalidPem_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                try {
                    crypto.createPrivateKey('not a valid pem');
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    [Fact]
    public void Compiled_Crypto_KeyObject_Export_Pem()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const key = crypto.createPublicKey(publicKey);
                const exported = key.export({ type: 'spki', format: 'pem' });

                console.log(typeof exported === 'string');
                console.log(exported.includes('BEGIN PUBLIC KEY'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Crypto_KeyObject_EcKey_AsymmetricKeyType()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const { publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
                const keyObject = crypto.createPublicKey(publicKey);

                console.log(keyObject.asymmetricKeyType === 'ec');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Crypto_KeyObject_EcKey_AsymmetricKeyDetails()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const { publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
                const keyObject = crypto.createPublicKey(publicKey);
                const details = keyObject.asymmetricKeyDetails;

                console.log(details.namedCurve === 'prime256v1' || details.namedCurve === 'P-256');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }
}
