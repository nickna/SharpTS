using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for crypto.createCipheriv and crypto.createDecipheriv.
/// </summary>
public class CryptoCipherTests
{
    #region CBC Mode Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Aes256Cbc_RoundTrip(ExecutionMode mode)
    {
        // Encrypt then decrypt, verify match
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Aes128Cbc_RoundTrip(ExecutionMode mode)
    {
        // Test with AES-128-CBC
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Aes192Cbc_RoundTrip(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Cbc_MultipleUpdates(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Cbc_EmptyInput(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Cbc_Base64Encoding(ExecutionMode mode)
    {
        // Test with base64 encoding
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Cbc_BufferOutput(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region GCM Mode Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Aes256Gcm_RoundTrip_String(ExecutionMode mode)
    {
        // GCM mode with auth tag using string output
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Aes256Gcm_RoundTrip_Buffer(ExecutionMode mode)
    {
        // GCM mode with auth tag using Buffer output
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Aes128Gcm_RoundTrip(ExecutionMode mode)
    {
        // AES-128-GCM mode
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Gcm_WithAAD(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Gcm_GetAuthTagReturnsBuffer(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Error Handling Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_InvalidAlgorithm(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_WrongKeySize(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_WrongIVSize(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_UpdateAfterFinal(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_DoubleFinal(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateDecipheriv_Gcm_MissingAuthTag(ExecutionMode mode)
    {
        // GCM decryption without setAuthTag should throw
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Gcm_GetAuthTagBeforeFinal(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Cbc_GetAuthTagThrows(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    #endregion

    #region Buffer Input Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_BufferInput(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Chaining Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_SetAutoPaddingChaining(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_SetAADChaining(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Input Encoding Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_HexInputEncoding(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_CreateCipheriv_Base64InputEncoding(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion
}
