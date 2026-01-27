using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for crypto.createCipheriv and crypto.createDecipheriv.
/// Uses interpreter mode to test the runtime types.
/// </summary>
public class CryptoCipherTests
{
    // ============ CBC MODE TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_Aes256Cbc_RoundTrip()
    {
        // Encrypt then decrypt, verify match
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted = cipher.update('Hello, World!', 'utf8', 'hex');
                encrypted += cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'Hello, World!');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Aes128Cbc_RoundTrip()
    {
        // Test with AES-128-CBC
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(16);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-128-cbc', key, iv);
                let encrypted = cipher.update('Test message', 'utf8', 'hex');
                encrypted += cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-128-cbc', key, iv);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'Test message');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Aes192Cbc_RoundTrip()
    {
        // Test with AES-192-CBC
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(24);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-192-cbc', key, iv);
                let encrypted = cipher.update('Test message 192', 'utf8', 'hex');
                encrypted += cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-192-cbc', key, iv);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'Test message 192');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Cbc_MultipleUpdates()
    {
        // Multiple update() calls should work correctly
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted = '';
                encrypted += cipher.update('Hello', 'utf8', 'hex');
                encrypted += cipher.update(', ', 'utf8', 'hex');
                encrypted += cipher.update('World!', 'utf8', 'hex');
                encrypted += cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'Hello, World!');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Cbc_EmptyInput()
    {
        // Empty plaintext should still work
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted = cipher.update('', 'utf8', 'hex');
                encrypted += cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === '');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Cbc_Base64Encoding()
    {
        // Test with base64 encoding
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted = cipher.update('Test base64', 'utf8', 'base64');
                encrypted += cipher.final('base64');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted = decipher.update(encrypted, 'base64', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'Test base64');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Cbc_BufferOutput()
    {
        // Test with Buffer output (default)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                const encryptedUpdate = cipher.update('Test', 'utf8');
                const encryptedFinal = cipher.final();

                console.log(Buffer.isBuffer(encryptedUpdate));
                console.log(Buffer.isBuffer(encryptedFinal));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ GCM MODE TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_Aes256Gcm_RoundTrip()
    {
        // GCM mode with auth tag
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(12);

                const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
                let encrypted = cipher.update('GCM test message', 'utf8', 'hex');
                encrypted += cipher.final('hex');
                const authTag = cipher.getAuthTag();

                const decipher = crypto.createDecipheriv('aes-256-gcm', key, iv);
                decipher.setAuthTag(authTag);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'GCM test message');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Aes128Gcm_RoundTrip()
    {
        // AES-128-GCM mode
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(16);
                const iv = crypto.randomBytes(12);

                const cipher = crypto.createCipheriv('aes-128-gcm', key, iv);
                let encrypted = cipher.update('GCM 128 test', 'utf8', 'hex');
                encrypted += cipher.final('hex');
                const authTag = cipher.getAuthTag();

                const decipher = crypto.createDecipheriv('aes-128-gcm', key, iv);
                decipher.setAuthTag(authTag);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'GCM 128 test');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Gcm_WithAAD()
    {
        // GCM with additional authenticated data
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(12);
                const aad = Buffer.from('additional data');

                const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
                cipher.setAAD(aad);
                let encrypted = cipher.update('GCM with AAD', 'utf8', 'hex');
                encrypted += cipher.final('hex');
                const authTag = cipher.getAuthTag();

                const decipher = crypto.createDecipheriv('aes-256-gcm', key, iv);
                decipher.setAAD(aad);
                decipher.setAuthTag(authTag);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'GCM with AAD');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Gcm_GetAuthTagReturnsBuffer()
    {
        // getAuthTag should return a Buffer
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(12);

                const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
                cipher.update('test', 'utf8');
                cipher.final();
                const authTag = cipher.getAuthTag();

                console.log(Buffer.isBuffer(authTag));
                console.log(authTag.length === 16);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ ERROR HANDLING TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_InvalidAlgorithm()
    {
        // Unknown algorithm should throw
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_WrongKeySize()
    {
        // Wrong key size should throw
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(16);  // Wrong size for aes-256-cbc
                const iv = crypto.randomBytes(16);

                try {
                    crypto.createCipheriv('aes-256-cbc', key, iv);
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
    public void Crypto_CreateCipheriv_WrongIVSize()
    {
        // Wrong IV size should throw
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(8);  // Wrong size for CBC

                try {
                    crypto.createCipheriv('aes-256-cbc', key, iv);
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
    public void Crypto_CreateCipheriv_UpdateAfterFinal()
    {
        // Calling update after final should throw
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                cipher.update('test', 'utf8');
                cipher.final();

                try {
                    cipher.update('more data', 'utf8');
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
    public void Crypto_CreateCipheriv_DoubleFinal()
    {
        // Calling final twice should throw
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                cipher.update('test', 'utf8');
                cipher.final();

                try {
                    cipher.final();
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
    public void Crypto_CreateDecipheriv_Gcm_MissingAuthTag()
    {
        // GCM decryption without setAuthTag should throw
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(12);

                const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
                let encrypted = cipher.update('test', 'utf8', 'hex');
                encrypted += cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-256-gcm', key, iv);
                decipher.update(encrypted, 'hex');

                try {
                    decipher.final();  // No setAuthTag called
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
    public void Crypto_CreateCipheriv_Gcm_GetAuthTagBeforeFinal()
    {
        // Calling getAuthTag before final should throw
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(12);

                const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
                cipher.update('test', 'utf8');

                try {
                    cipher.getAuthTag();  // final not called yet
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
    public void Crypto_CreateCipheriv_Cbc_GetAuthTagThrows()
    {
        // getAuthTag on CBC cipher should throw
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                cipher.update('test', 'utf8');
                cipher.final();

                try {
                    cipher.getAuthTag();  // Not available for CBC
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    // ============ BUFFER INPUT TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_BufferInput()
    {
        // Using Buffer as input with hex encoding for simpler output handling
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                const data = Buffer.from('Buffer input test');
                let encrypted = cipher.update(data, undefined, 'hex');
                encrypted += cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'Buffer input test');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ CHAINING TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_SetAutoPaddingChaining()
    {
        // setAutoPadding should return this for chaining
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                const result = cipher.setAutoPadding(true);

                console.log(result === cipher);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_SetAADChaining()
    {
        // setAAD should return this for chaining
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(12);

                const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
                const aad = Buffer.from('aad');
                const result = cipher.setAAD(aad);

                console.log(result === cipher);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ INPUT ENCODING TESTS ============

    [Fact]
    public void Crypto_CreateCipheriv_HexInputEncoding()
    {
        // Test hex input encoding for cipher
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                // "Hello" in hex is "48656c6c6f"
                const hexInput = '48656c6c6f';

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted = cipher.update(hexInput, 'hex', 'hex');
                encrypted += cipher.final('hex');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted = decipher.update(encrypted, 'hex', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'Hello');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateCipheriv_Base64InputEncoding()
    {
        // Test base64 input encoding for cipher
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.randomBytes(32);
                const iv = crypto.randomBytes(16);

                // "Hello" in base64 is "SGVsbG8="
                const base64Input = 'SGVsbG8=';

                const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
                let encrypted = cipher.update(base64Input, 'base64', 'base64');
                encrypted += cipher.final('base64');

                const decipher = crypto.createDecipheriv('aes-256-cbc', key, iv);
                let decrypted = decipher.update(encrypted, 'base64', 'utf8');
                decrypted += decipher.final('utf8');

                console.log(decrypted === 'Hello');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }
}
