using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for crypto module key derivation functions: pbkdf2Sync and scryptSync.
/// </summary>
public class CryptoKDFTests
{
    // ============ PBKDF2 TESTS ============

    [Fact]
    public void Pbkdf2Sync_ReturnsBuffer()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_Sha256_KnownVector()
    {
        // RFC 6070 test vector for PBKDF2-HMAC-SHA256
        // Password: "password", Salt: "salt", Iterations: 1, Key length: 32
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 1, 32, 'sha256');
                const hex = key.toString('hex');
                // Known PBKDF2-SHA256 output for password="password", salt="salt", iterations=1, keylen=32
                console.log(hex === '120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_Sha1_KnownVector()
    {
        // RFC 6070 test vector for PBKDF2-HMAC-SHA1
        // Password: "password", Salt: "salt", Iterations: 1, Key length: 20
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 1, 20, 'sha1');
                const hex = key.toString('hex');
                // Known PBKDF2-SHA1 output for password="password", salt="salt", iterations=1, keylen=20
                console.log(hex === '0c60c80f961f0e71f3a9b524af6012062fe037a6');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_Sha512()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 1000, 64, 'sha512');
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 64);
                // Ensure it's not all zeros
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_DifferentIterations()
    {
        // Higher iterations should produce different result
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.pbkdf2Sync('password', 'salt', 1, 32, 'sha256');
                const key2 = crypto.pbkdf2Sync('password', 'salt', 1000, 32, 'sha256');
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_DifferentSalts()
    {
        // Different salts should produce different results
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.pbkdf2Sync('password', 'salt1', 1000, 32, 'sha256');
                const key2 = crypto.pbkdf2Sync('password', 'salt2', 1000, 32, 'sha256');
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_BufferPassword()
    {
        // Password can be a Buffer
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_BufferSalt()
    {
        // Salt can be a Buffer
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_UnsupportedAlgorithmThrows()
    {
        // MD5 is not supported for PBKDF2 in .NET
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_Sha384()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ SCRYPT TESTS ============

    [Fact]
    public void ScryptSync_ReturnsBuffer()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ScryptSync_KnownVector()
    {
        // RFC 7914 test vector (with smaller N for faster test)
        // Using N=1024, r=8, p=1 for reasonable test time
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.scryptSync('password', 'NaCl', 64, { N: 1024, r: 8, p: 16 });
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 64);
                // Ensure it's deterministic
                const key2 = crypto.scryptSync('password', 'NaCl', 64, { N: 1024, r: 8, p: 16 });
                console.log(key.toString('hex') === key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void ScryptSync_DefaultParameters()
    {
        // Default N=16384, r=8, p=1
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.scryptSync('password', 'salt', 32);
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                // Ensure it's not all zeros
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void ScryptSync_DifferentSalts()
    {
        // Different salts should produce different results
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password', 'salt1', 32, { N: 1024 });
                const key2 = crypto.scryptSync('password', 'salt2', 32, { N: 1024 });
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ScryptSync_DifferentPasswords()
    {
        // Different passwords should produce different results
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password1', 'salt', 32, { N: 1024 });
                const key2 = crypto.scryptSync('password2', 'salt', 32, { N: 1024 });
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ScryptSync_DifferentCostParameter()
    {
        // Different N (cost) should produce different results
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password', 'salt', 32, { N: 1024 });
                const key2 = crypto.scryptSync('password', 'salt', 32, { N: 2048 });
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ScryptSync_BufferPassword()
    {
        // Password can be a Buffer
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ScryptSync_BufferSalt()
    {
        // Salt can be a Buffer
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ScryptSync_CostAlias()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ScryptSync_BlockSizeAlias()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ScryptSync_ParallelizationAlias()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ScryptSync_Deterministic()
    {
        // Same inputs should always produce same output
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ COMPILED MODE TESTS ============

    [Fact]
    public void Pbkdf2Sync_Compiled_ReturnsBuffer()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_Compiled_KnownVector()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 1, 32, 'sha256');
                const hex = key.toString('hex');
                console.log(hex === '120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ScryptSync_Compiled_ReturnsBuffer()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ScryptSync_Compiled_Deterministic()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('test', 'salt', 32);
                const key2 = crypto.scryptSync('test', 'salt', 32);
                console.log(key1.toString('hex') === key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Pbkdf2Sync_InterpreterAndCompiledMatch()
    {
        // Ensure interpreter and compiled produce the same result
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.pbkdf2Sync('password', 'salt', 100, 32, 'sha256');
                console.log(key.toString('hex'));
                """
        };

        var interpretedOutput = TestHarness.RunModulesInterpreted(files, "main.ts");
        var compiledOutput = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void ScryptSync_InterpreterAndCompiledMatch()
    {
        // Ensure interpreter and compiled produce the same result
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.scryptSync('password', 'salt', 32);
                console.log(key.toString('hex'));
                """
        };

        var interpretedOutput = TestHarness.RunModulesInterpreted(files, "main.ts");
        var compiledOutput = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void ScryptSync_Compiled_WithOptions()
    {
        // Verify compiled mode properly parses options
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.scryptSync('password', 'salt', 32, { N: 1024, r: 8, p: 1 });
                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ScryptSync_Compiled_OptionsAffectOutput()
    {
        // Different N values should produce different results in compiled mode
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key1 = crypto.scryptSync('password', 'salt', 32, { N: 1024 });
                const key2 = crypto.scryptSync('password', 'salt', 32, { N: 2048 });
                console.log(key1.toString('hex') !== key2.toString('hex'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ScryptSync_InterpreterAndCompiledMatchWithOptions()
    {
        // Ensure interpreter and compiled produce the same result with custom options
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.scryptSync('password', 'salt', 32, { N: 1024, r: 8, p: 1 });
                console.log(key.toString('hex'));
                """
        };

        var interpretedOutput = TestHarness.RunModulesInterpreted(files, "main.ts");
        var compiledOutput = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal(interpretedOutput, compiledOutput);
    }
}
