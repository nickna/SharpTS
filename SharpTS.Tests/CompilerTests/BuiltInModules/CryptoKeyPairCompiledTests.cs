using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Compiled mode tests for crypto.generateKeyPairSync().
/// </summary>
public class CryptoKeyPairCompiledTests
{
    [Fact]
    public void Crypto_GenerateKeyPairSync_RSA_2048_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey, privateKey } = crypto.generateKeyPairSync('rsa', {
                    modulusLength: 2048
                });
                console.log(typeof publicKey === 'string');
                console.log(typeof privateKey === 'string');
                console.log(publicKey.includes('BEGIN PUBLIC KEY'));
                console.log(privateKey.includes('BEGIN PRIVATE KEY'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GenerateKeyPairSync_RSA_4096_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey, privateKey } = crypto.generateKeyPairSync('rsa', {
                    modulusLength: 4096
                });
                console.log(publicKey.includes('BEGIN PUBLIC KEY'));
                console.log(privateKey.includes('BEGIN PRIVATE KEY'));
                // 4096-bit key should be significantly longer than 2048-bit
                // Just verify the key is non-empty (actual length is ~3200+ chars)
                console.log(privateKey.length > 1000);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GenerateKeyPairSync_EC_P256_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey, privateKey } = crypto.generateKeyPairSync('ec', {
                    namedCurve: 'prime256v1'
                });
                console.log(typeof publicKey === 'string');
                console.log(typeof privateKey === 'string');
                console.log(publicKey.includes('BEGIN PUBLIC KEY'));
                console.log(privateKey.includes('BEGIN PRIVATE KEY'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GenerateKeyPairSync_EC_P384_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey, privateKey } = crypto.generateKeyPairSync('ec', {
                    namedCurve: 'secp384r1'
                });
                console.log(publicKey.includes('BEGIN PUBLIC KEY'));
                console.log(privateKey.includes('BEGIN PRIVATE KEY'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GenerateKeyPairSync_EC_P521_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey, privateKey } = crypto.generateKeyPairSync('ec', {
                    namedCurve: 'secp521r1'
                });
                console.log(publicKey.includes('BEGIN PUBLIC KEY'));
                console.log(privateKey.includes('BEGIN PRIVATE KEY'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GenerateKeyPairSync_RSA_DefaultOptions_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                // Should use default modulusLength of 2048
                const { publicKey, privateKey } = crypto.generateKeyPairSync('rsa');
                console.log(publicKey.includes('BEGIN PUBLIC KEY'));
                console.log(privateKey.includes('BEGIN PRIVATE KEY'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GenerateKeyPairSync_EC_DefaultCurve_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                // Should use default curve of prime256v1
                const { publicKey, privateKey } = crypto.generateKeyPairSync('ec');
                console.log(publicKey.includes('BEGIN PUBLIC KEY'));
                console.log(privateKey.includes('BEGIN PRIVATE KEY'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GenerateKeyPairSync_InvalidType_Throws_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                try {
                    crypto.generateKeyPairSync('invalid');
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
    public void Crypto_GenerateKeyPairSync_RSA_WorksWithCreateSign_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey, privateKey } = crypto.generateKeyPairSync('rsa', {
                    modulusLength: 2048
                });

                // Sign some data
                const sign = crypto.createSign('sha256');
                sign.update('test data');
                const signature = sign.sign(privateKey, 'hex');

                // Verify the signature
                const verify = crypto.createVerify('sha256');
                verify.update('test data');
                const isValid = verify.verify(publicKey, signature, 'hex');

                console.log(typeof signature === 'string');
                console.log(signature.length > 0);
                console.log(isValid);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GenerateKeyPairSync_EC_WorksWithCreateSign_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey, privateKey } = crypto.generateKeyPairSync('ec', {
                    namedCurve: 'prime256v1'
                });

                // Sign some data
                const sign = crypto.createSign('sha256');
                sign.update('test data');
                const signature = sign.sign(privateKey, 'hex');

                // Verify the signature
                const verify = crypto.createVerify('sha256');
                verify.update('test data');
                const isValid = verify.verify(publicKey, signature, 'hex');

                console.log(typeof signature === 'string');
                console.log(signature.length > 0);
                console.log(isValid);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }
}
