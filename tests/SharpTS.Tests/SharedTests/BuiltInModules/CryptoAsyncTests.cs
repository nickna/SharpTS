using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for async (callback-based) crypto methods: pbkdf2, scrypt, generateKeyPair, hkdf.
/// </summary>
public class CryptoAsyncTests
{
    [Theory, ModeData]
    public void Pbkdf2_Async_ReturnsBuffer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                crypto.pbkdf2('password', 'salt', 1000, 32, 'sha256', (err: any, key: any) => {
                    console.log(err === null);
                    console.log(Buffer.isBuffer(key));
                    console.log(key.length === 32);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Pbkdf2_Async_MatchesSync(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const syncKey = crypto.pbkdf2Sync('password', 'salt', 1000, 32, 'sha256');
                crypto.pbkdf2('password', 'salt', 1000, 32, 'sha256', (err: any, asyncKey: any) => {
                    console.log(err === null);
                    console.log(syncKey.toString('hex') === asyncKey.toString('hex'));
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Scrypt_Async_ReturnsBuffer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                crypto.scrypt('password', 'salt', 32, (err: any, key: any) => {
                    console.log(err === null);
                    console.log(Buffer.isBuffer(key));
                    console.log(key.length === 32);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Scrypt_Async_WithOptions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                crypto.scrypt('password', 'salt', 64, { N: 1024, r: 8, p: 1 }, (err: any, key: any) => {
                    console.log(err === null);
                    console.log(key.length === 64);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void GenerateKeyPair_Async_Rsa(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                crypto.generateKeyPair('rsa', { modulusLength: 2048 }, (err: any, publicKey: any, privateKey: any) => {
                    console.log(err === null);
                    console.log(typeof publicKey === 'string');
                    console.log(typeof privateKey === 'string');
                    console.log(publicKey.includes('PUBLIC KEY'));
                    console.log(privateKey.includes('PRIVATE KEY'));
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void GenerateKeyPair_Async_Ec(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                crypto.generateKeyPair('ec', { namedCurve: 'prime256v1' }, (err: any, publicKey: any, privateKey: any) => {
                    console.log(err === null);
                    console.log(typeof publicKey === 'string');
                    console.log(typeof privateKey === 'string');
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Hkdf_Async_ReturnsBuffer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                crypto.hkdf('sha256', 'secret', 'salt', 'info', 32, (err: any, key: any) => {
                    console.log(err === null);
                    console.log(Buffer.isBuffer(key));
                    console.log(key.length === 32);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Hkdf_Async_MatchesSync(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const syncKey = crypto.hkdfSync('sha256', 'secret', 'salt', 'info', 32);
                crypto.hkdf('sha256', 'secret', 'salt', 'info', 32, (err: any, asyncKey: any) => {
                    console.log(err === null);
                    console.log(syncKey.toString('hex') === asyncKey.toString('hex'));
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }
}
