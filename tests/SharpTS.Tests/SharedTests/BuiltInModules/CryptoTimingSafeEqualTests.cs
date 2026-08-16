using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Shared tests for crypto.timingSafeEqual function.
/// This function performs constant-time comparison of two buffers to prevent timing attacks.
/// Migrated from InterpreterTests to run in both interpreter and compiled modes.
/// </summary>
public class CryptoTimingSafeEqualTests
{
    // ============ BASIC FUNCTIONALITY TESTS ============

    [Theory, ModeData]
    public void TimingSafeEqual_EqualBuffers_ReturnsTrue(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_DifferentBuffers_ReturnsFalse(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_EmptyBuffers_ReturnsTrue(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_SingleByte_Equal(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_SingleByte_NotEqual(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    // ============ LENGTH MISMATCH TESTS ============

    [Theory, ModeData]
    public void TimingSafeEqual_DifferentLengths_Throws(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_EmptyVsNonEmpty_Throws(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    // ============ CRYPTO USE CASES ============

    [Theory, ModeData]
    public void TimingSafeEqual_HashComparison(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_HmacComparison(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hmac1 = crypto.createHmac('sha256', 'secret').update('message').digest();
                const hmac2 = crypto.createHmac('sha256', 'secret').update('message').digest();
                console.log(crypto.timingSafeEqual(hmac1, hmac2));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_DifferentHashes(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    // ============ RETURN TYPE TESTS ============

    [Theory, ModeData]
    public void TimingSafeEqual_ReturnsBoolean(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    // ============ LARGE BUFFER TESTS ============

    [Theory, ModeData]
    public void TimingSafeEqual_LargeBuffers_Equal(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_LargeBuffers_OneByteDifferent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.alloc(1024, 0);
                const b = Buffer.alloc(1024, 0);
                b.writeUInt8(1, 512);
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    // ============ STRING INPUT TESTS ============

    [Theory, ModeData]
    public void TimingSafeEqual_StringInputs_Equal(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = 'hello';
                const b = 'hello';
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_StringInputs_NotEqual(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_StringInputs_DifferentLengths_Throws(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory, ModeData]
    public void TimingSafeEqual_MixedInputs_BufferAndString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const a = Buffer.from('hello');
                const b = 'hello';
                console.log(crypto.timingSafeEqual(a, b));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }
}
