using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Shared tests for crypto module key derivation functions: pbkdf2Sync and scryptSync.
/// Migrated from InterpreterTests to run in both interpreter and compiled modes.
/// </summary>
public class CryptoKDFTests
{
    // ============ PBKDF2 TESTS ============

    [Theory, ModeData]
    public void Pbkdf2Sync_ReturnsBuffer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 1000, 32, 'sha256');
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2Sync_Sha256_KnownVector(ExecutionMode mode)
    {
        // RFC 6070 test vector for PBKDF2-HMAC-SHA256
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 1, 32, 'sha256');
                const hex = key.toString('hex');
                console.log(hex === '120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2Sync_Sha1_KnownVector(ExecutionMode mode)
    {
        // RFC 6070 test vector for PBKDF2-HMAC-SHA1
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 1, 20, 'sha1');
                const hex = key.toString('hex');
                console.log(hex === '0c60c80f961f0e71f3a9b524af6012062fe037a6');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2Sync_Sha512(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 1000, 64, 'sha512');
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 64);
                let hasNonZero = false;
                for (const b of key) {
                    if (b !== 0) {
                        hasNonZero = true;
                        break;
                    }
                }
                console.log(hasNonZero);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2Sync_Sha384(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 100, 48, 'sha384');
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 48);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2Sync_DifferentIterations(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.pbkdf2Sync('password', 'salt', 1, 32, 'sha256');
                const key2 = crypto.pbkdf2Sync('password', 'salt', 1000, 32, 'sha256');
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2Sync_DifferentSalts(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.pbkdf2Sync('password', 'salt1', 1000, 32, 'sha256');
                const key2 = crypto.pbkdf2Sync('password', 'salt2', 1000, 32, 'sha256');
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2Sync_BufferPassword(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const password = Buffer.from('password');
                const key = crypto.pbkdf2Sync(password, 'salt', 1000, 32, 'sha256');
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2Sync_BufferSalt(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const salt = Buffer.from('salt');
                const key = crypto.pbkdf2Sync('password', salt, 1000, 32, 'sha256');
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2Sync_UnsupportedAlgorithmThrows(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                try {
                    crypto.pbkdf2Sync('password', 'salt', 100, 16, 'md5');
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    // ============ SCRYPT TESTS ============

    [Theory, ModeData]
    public void ScryptSync_ReturnsBuffer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.scryptSync('password', 'salt', 32);
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_KnownVector(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.scryptSync('password', 'NaCl', 64, { N: 1024, r: 8, p: 16 });
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 64);
                const key2 = crypto.scryptSync('password', 'NaCl', 64, { N: 1024, r: 8, p: 16 });
                console.log(key.toString('hex') === key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_DefaultParameters(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.scryptSync('password', 'salt', 32);
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                let hasNonZero = false;
                for (const b of key) {
                    if (b !== 0) {
                        hasNonZero = true;
                        break;
                    }
                }
                console.log(hasNonZero);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_DifferentSalts(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password', 'salt1', 32, { N: 1024 });
                const key2 = crypto.scryptSync('password', 'salt2', 32, { N: 1024 });
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_DifferentPasswords(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password1', 'salt', 32, { N: 1024 });
                const key2 = crypto.scryptSync('password2', 'salt', 32, { N: 1024 });
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_DifferentCostParameter(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password', 'salt', 32, { N: 1024 });
                const key2 = crypto.scryptSync('password', 'salt', 32, { N: 2048 });
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_BufferPassword(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const password = Buffer.from('password');
                const key = crypto.scryptSync(password, 'salt', 32, { N: 1024 });
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_BufferSalt(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const salt = Buffer.from('salt');
                const key = crypto.scryptSync('password', salt, 32, { N: 1024 });
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_CostAlias(ExecutionMode mode)
    {
        // 'cost' is an alias for 'N'
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password', 'salt', 32, { N: 1024 });
                const key2 = crypto.scryptSync('password', 'salt', 32, { cost: 1024 });
                console.log(key1.toString('hex') === key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_BlockSizeAlias(ExecutionMode mode)
    {
        // 'blockSize' is an alias for 'r'
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password', 'salt', 32, { N: 1024, r: 8 });
                const key2 = crypto.scryptSync('password', 'salt', 32, { N: 1024, blockSize: 8 });
                console.log(key1.toString('hex') === key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_ParallelizationAlias(ExecutionMode mode)
    {
        // 'parallelization' is an alias for 'p'
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password', 'salt', 32, { N: 1024, p: 1 });
                const key2 = crypto.scryptSync('password', 'salt', 32, { N: 1024, parallelization: 1 });
                console.log(key1.toString('hex') === key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_Deterministic(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('test', 'salt', 32, { N: 1024 });
                const key2 = crypto.scryptSync('test', 'salt', 32, { N: 1024 });
                const key3 = crypto.scryptSync('test', 'salt', 32, { N: 1024 });
                console.log(key1.toString('hex') === key2.toString('hex'));
                console.log(key2.toString('hex') === key3.toString('hex'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ScryptSync_WithOptions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.scryptSync('password', 'salt', 32, { N: 1024, r: 8, p: 1 });
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }
}
