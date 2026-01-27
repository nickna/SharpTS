using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for crypto.createCipheriv and crypto.createDecipheriv in compiled mode.
/// </summary>
public class CryptoCipherCompiledTests
{
    // ============ CBC MODE TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_Aes256Cbc_RoundTrip_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted: string = '';
                encrypted = encrypted + cipher.update('Hello, World!', 'utf8', 'hex');
                encrypted = encrypted + cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted: string = '';
                decrypted = decrypted + decipher.update(encrypted, 'hex', 'utf8');
                decrypted = decrypted + decipher.final('utf8');

                console.log(decrypted === 'Hello, World!');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Aes128Cbc_RoundTrip_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(16);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-128-cbc', key, iv);
                let encrypted: string = '';
                encrypted = encrypted + cipher.update('Test message', 'utf8', 'hex');
                encrypted = encrypted + cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-128-cbc', key, iv);
                let decrypted: string = '';
                decrypted = decrypted + decipher.update(encrypted, 'hex', 'utf8');
                decrypted = decrypted + decipher.final('utf8');

                console.log(decrypted === 'Test message');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Cbc_Base64Encoding_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted: string = '';
                encrypted = encrypted + cipher.update('Test base64', 'utf8', 'base64');
                encrypted = encrypted + cipher.final('base64');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted: string = '';
                decrypted = decrypted + decipher.update(encrypted, 'base64', 'utf8');
                decrypted = decrypted + decipher.final('utf8');

                console.log(decrypted === 'Test base64');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ GCM MODE TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_Aes256Gcm_RoundTrip_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(12);

                const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
                cipher.update('Hello, GCM World!', 'utf8');
                const encrypted = cipher.final();
                const authTag = cipher.getAuthTag();

                const decipher = crypto.createDecipheriv('aes-256-gcm', key, iv);
                decipher.setAuthTag(authTag);
                decipher.update(encrypted);
                const decrypted = decipher.final('utf8');

                console.log(decrypted === 'Hello, GCM World!');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Aes128Gcm_RoundTrip_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(16);
                const iv = crypto.randomBytes(12);

                const cipher = crypto.createCipheriv('aes-128-gcm', key, iv);
                cipher.update('GCM 128-bit test', 'utf8');
                const encrypted = cipher.final();
                const authTag = cipher.getAuthTag();

                const decipher = crypto.createDecipheriv('aes-128-gcm', key, iv);
                decipher.setAuthTag(authTag);
                decipher.update(encrypted);
                const decrypted = decipher.final('utf8');

                console.log(decrypted === 'GCM 128-bit test');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Gcm_WithAAD_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(12);
                const aad = Buffer.from('additional data');

                const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
                cipher.setAAD(aad);
                cipher.update('Secret with AAD', 'utf8');
                const encrypted = cipher.final();
                const authTag = cipher.getAuthTag();

                const decipher = crypto.createDecipheriv('aes-256-gcm', key, iv);
                decipher.setAAD(aad);
                decipher.setAuthTag(authTag);
                decipher.update(encrypted);
                const decrypted = decipher.final('utf8');

                console.log(decrypted === 'Secret with AAD');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateDecipheriv_Gcm_MissingAuthTag_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(12);

                try {
                    const decipher = crypto.createDecipheriv('aes-256-gcm', key, iv);
                    decipher.update(Buffer.from([1, 2, 3]));
                    decipher.final();
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    // ============ ERROR HANDLING TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_InvalidAlgorithm_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                try {
                    crypto.createCipheriv('invalid-algorithm', key, iv);
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
    public void Crypto_CreateCipheriv_WrongKeySize_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(16);
                const iv = crypto.randomBytes(16);

                try {
                    crypto.createCipheriv('aes-256-cbc', key, iv);
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    // ============ INPUT ENCODING TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_HexInputEncoding_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                // "Hello" in hex is "48656c6c6f"
                const hexInput = '48656c6c6f';

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted: string = '';
                encrypted = encrypted + cipher.update(hexInput, 'hex', 'hex');
                encrypted = encrypted + cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted: string = '';
                decrypted = decrypted + decipher.update(encrypted, 'hex', 'utf8');
                decrypted = decrypted + decipher.final('utf8');

                console.log(decrypted === 'Hello');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Base64InputEncoding_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                // "Hello" in base64 is "SGVsbG8="
                const base64Input = 'SGVsbG8=';

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted: string = '';
                encrypted = encrypted + cipher.update(base64Input, 'base64', 'base64');
                encrypted = encrypted + cipher.final('base64');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted: string = '';
                decrypted = decrypted + decipher.update(encrypted, 'base64', 'utf8');
                decrypted = decrypted + decipher.final('utf8');

                console.log(decrypted === 'Hello');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }
}
