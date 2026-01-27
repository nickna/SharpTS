using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the crypto module's KeyObject API.
/// Uses interpreter mode for testing.
/// </summary>
public class CryptoKeyObjectTests
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

    #region createSecretKey Tests

    [Fact]
    public void Crypto_CreateSecretKey_FromBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey(Buffer.from('my secret key'));

                console.log(typeof key === 'object');
                console.log(key.type === 'secret');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateSecretKey_FromStringUtf8()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey('my secret key', 'utf8');

                console.log(key.type === 'secret');
                console.log(key.symmetricKeySize === 13);  // 'my secret key' is 13 bytes in UTF-8
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateSecretKey_FromStringHex()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey('deadbeef', 'hex');

                console.log(key.type === 'secret');
                console.log(key.symmetricKeySize === 4);  // 'deadbeef' is 4 bytes
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateSecretKey_FromStringBase64()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey('SGVsbG8=', 'base64');  // 'Hello' in base64

                console.log(key.type === 'secret');
                console.log(key.symmetricKeySize === 5);  // 'Hello' is 5 bytes
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateSecretKey_Export()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const originalKey = Buffer.from('secret123');
                const keyObj = crypto.createSecretKey(originalKey);
                const exported = keyObj.export();

                console.log(Buffer.isBuffer(exported));
                console.log(exported.equals(originalKey));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateSecretKey_NoAsymmetricKeyType()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.createSecretKey(Buffer.from('secret'));

                console.log(key.asymmetricKeyType === undefined || key.asymmetricKeyType === null);
                console.log(key.asymmetricKeyDetails === undefined || key.asymmetricKeyDetails === null);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region createPublicKey Tests

    [Fact]
    public void Crypto_CreatePublicKey_FromPem()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const keyObj = crypto.createPublicKey(publicKey);

                console.log(typeof keyObj === 'object');
                console.log(keyObj.type === 'public');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreatePublicKey_AsymmetricKeyType()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const keyObj = crypto.createPublicKey(publicKey);

                console.log(keyObj.asymmetricKeyType === 'rsa');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreatePublicKey_AsymmetricKeyDetails_RSA()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const keyObj = crypto.createPublicKey(publicKey);
                const details = keyObj.asymmetricKeyDetails;

                console.log(details !== null && details !== undefined);
                console.log(details.modulusLength === 2048);
                console.log(details.publicExponent === 65537);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreatePublicKey_Export()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const keyObj = crypto.createPublicKey(publicKey);
                const exported = keyObj.export({ type: 'spki', format: 'pem' });

                console.log(typeof exported === 'string');
                console.log(exported.includes('BEGIN PUBLIC KEY'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreatePublicKey_NoSymmetricKeySize()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const keyObj = crypto.createPublicKey(publicKey);

                console.log(keyObj.symmetricKeySize === undefined || keyObj.symmetricKeySize === null);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreatePublicKey_FromObject()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const publicKey = `{{TestPublicKey}}`;
                const keyObj = crypto.createPublicKey({ key: publicKey });

                console.log(keyObj.type === 'public');
                console.log(keyObj.asymmetricKeyType === 'rsa');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region createPrivateKey Tests

    [Fact]
    public void Crypto_CreatePrivateKey_FromPem()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;
                const keyObj = crypto.createPrivateKey(privateKey);

                console.log(typeof keyObj === 'object');
                console.log(keyObj.type === 'private');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreatePrivateKey_AsymmetricKeyType()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;
                const keyObj = crypto.createPrivateKey(privateKey);

                console.log(keyObj.asymmetricKeyType === 'rsa');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreatePrivateKey_AsymmetricKeyDetails_RSA()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;
                const keyObj = crypto.createPrivateKey(privateKey);
                const details = keyObj.asymmetricKeyDetails;

                console.log(details !== null && details !== undefined);
                console.log(details.modulusLength === 2048);
                console.log(details.publicExponent === 65537);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreatePrivateKey_Export()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as crypto from 'crypto';

                const privateKey = `{{TestPrivateKey}}`;
                const keyObj = crypto.createPrivateKey(privateKey);
                const exported = keyObj.export({ type: 'pkcs8', format: 'pem' });

                console.log(typeof exported === 'string');
                console.log(exported.includes('BEGIN PRIVATE KEY'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region EC Key Tests

    [Fact]
    public void Crypto_CreatePrivateKey_EC()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const { privateKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
                const keyObj = crypto.createPrivateKey(privateKey);

                console.log(keyObj.type === 'private');
                console.log(keyObj.asymmetricKeyType === 'ec');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreatePublicKey_EC()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const { publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
                const keyObj = crypto.createPublicKey(publicKey);

                console.log(keyObj.type === 'public');
                console.log(keyObj.asymmetricKeyType === 'ec');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreatePublicKey_EC_AsymmetricKeyDetails()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const { publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
                const keyObj = crypto.createPublicKey(publicKey);
                const details = keyObj.asymmetricKeyDetails;

                console.log(details !== null && details !== undefined);
                // namedCurve should be present in the details object
                // Use Object.keys to check what's available
                const keys = Object.keys(details);
                console.log(keys.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Error Cases

    [Fact]
    public void Crypto_CreatePublicKey_InvalidPem_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                try {
                    crypto.createPublicKey('not a valid PEM');
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
    public void Crypto_CreatePrivateKey_InvalidPem_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                try {
                    crypto.createPrivateKey('not a valid PEM');
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
    public void Crypto_CreateSecretKey_InvalidEncoding_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                try {
                    crypto.createSecretKey('test', 'invalid-encoding');
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    #endregion
}
