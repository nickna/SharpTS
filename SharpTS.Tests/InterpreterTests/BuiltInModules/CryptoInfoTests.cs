using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for crypto.getHashes() and crypto.getCiphers().
/// </summary>
public class CryptoInfoTests
{
    [Fact]
    public void Crypto_GetHashes_ReturnsArray()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hashes = crypto.getHashes();
                console.log(Array.isArray(hashes));
                console.log(hashes.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GetHashes_ContainsSha256()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hashes = crypto.getHashes();
                console.log(hashes.includes('sha256'));
                console.log(hashes.includes('sha512'));
                console.log(hashes.includes('md5'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GetCiphers_ReturnsArray()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ciphers = crypto.getCiphers();
                console.log(Array.isArray(ciphers));
                console.log(ciphers.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GetCiphers_ContainsAesCbc()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ciphers = crypto.getCiphers();
                console.log(ciphers.includes('aes-256-cbc'));
                console.log(ciphers.includes('aes-128-gcm'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}
