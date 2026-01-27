using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for crypto.timingSafeEqual function.
/// This function performs constant-time comparison of two buffers to prevent timing attacks.
/// </summary>
public class CryptoTimingSafeEqualTests
{
    // ============ BASIC FUNCTIONALITY TESTS ============

    [Fact]
    public void TimingSafeEqual_EqualBuffers_ReturnsTrue()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('hello');
                const b = Buffer.from('hello');
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TimingSafeEqual_DifferentBuffers_ReturnsFalse()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('hello');
                const b = Buffer.from('world');
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void TimingSafeEqual_EmptyBuffers_ReturnsTrue()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.alloc(0);
                const b = Buffer.alloc(0);
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TimingSafeEqual_SingleByte_Equal()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from([42]);
                const b = Buffer.from([42]);
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TimingSafeEqual_SingleByte_NotEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from([42]);
                const b = Buffer.from([43]);
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    // ============ LENGTH MISMATCH TESTS ============

    [Fact]
    public void TimingSafeEqual_DifferentLengths_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('hello');
                const b = Buffer.from('hi');
                try {
                    crypto.timingSafeEqual(a, b);
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
    public void TimingSafeEqual_EmptyVsNonEmpty_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.alloc(0);
                const b = Buffer.from('hello');
                try {
                    crypto.timingSafeEqual(a, b);
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    // ============ CRYPTO USE CASES ============

    [Fact]
    public void TimingSafeEqual_HashComparison()
    {
        // Common use case: comparing password hashes
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hash1 = crypto.createHash('sha256').update('password').digest();
                const hash2 = crypto.createHash('sha256').update('password').digest();
                console.log(crypto.timingSafeEqual(hash1, hash2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TimingSafeEqual_HmacComparison()
    {
        // Common use case: comparing HMAC signatures
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hmac1 = crypto.createHmac('sha256', 'secret').update('message').digest();
                const hmac2 = crypto.createHmac('sha256', 'secret').update('message').digest();
                console.log(crypto.timingSafeEqual(hmac1, hmac2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TimingSafeEqual_DifferentHashes()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hash1 = crypto.createHash('sha256').update('password1').digest();
                const hash2 = crypto.createHash('sha256').update('password2').digest();
                console.log(crypto.timingSafeEqual(hash1, hash2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    // ============ RETURN TYPE TESTS ============

    [Fact]
    public void TimingSafeEqual_ReturnsBoolean()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('test');
                const b = Buffer.from('test');
                const result = crypto.timingSafeEqual(a, b);
                console.log(typeof result === 'boolean');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ LARGE BUFFER TESTS ============

    [Fact]
    public void TimingSafeEqual_LargeBuffers_Equal()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = crypto.randomBytes(1024);
                const b = Buffer.from(a);
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TimingSafeEqual_LargeBuffers_OneByteDifferent()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.alloc(1024, 0);
                const b = Buffer.alloc(1024, 0);
                b.writeUInt8(1, 512);  // Change one byte in the middle
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    // ============ COMPILED MODE TESTS ============

    [Fact]
    public void TimingSafeEqual_Compiled_EqualBuffers()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('hello');
                const b = Buffer.from('hello');
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TimingSafeEqual_Compiled_DifferentBuffers()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('hello');
                const b = Buffer.from('world');
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void TimingSafeEqual_Compiled_DifferentLengths_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('hello');
                const b = Buffer.from('hi');
                try {
                    crypto.timingSafeEqual(a, b);
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
    public void TimingSafeEqual_Compiled_HashComparison()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hash1 = crypto.createHash('sha256').update('password').digest();
                const hash2 = crypto.createHash('sha256').update('password').digest();
                console.log(crypto.timingSafeEqual(hash1, hash2));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TimingSafeEqual_InterpreterAndCompiledMatch()
    {
        // Ensure interpreter and compiled produce the same result
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('test data here');
                const b = Buffer.from('test data here');
                console.log(crypto.timingSafeEqual(a, b));
                const c = Buffer.from('different data');
                const d = Buffer.from('other content!');
                console.log(crypto.timingSafeEqual(c, d));
                """
        };

        var interpretedOutput = TestHarness.RunModulesInterpreted(files, "main.ts");
        var compiledOutput = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    // ============ STRING INPUT TESTS ============

    [Fact]
    public void TimingSafeEqual_StringInputs_Equal()
    {
        // timingSafeEqual should also work with strings (converted to UTF-8 bytes)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = 'hello';
                const b = 'hello';
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TimingSafeEqual_StringInputs_NotEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = 'hello';
                const b = 'world';
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void TimingSafeEqual_StringInputs_DifferentLengths_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                try {
                    crypto.timingSafeEqual('hello', 'hi');
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
    public void TimingSafeEqual_MixedInputs_BufferAndString()
    {
        // Buffer and string with same content should be equal
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('hello');
                const b = 'hello';
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }
}
