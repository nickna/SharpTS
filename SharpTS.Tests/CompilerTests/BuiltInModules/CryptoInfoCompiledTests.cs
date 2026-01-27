using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Compiled mode tests for crypto.getHashes() and crypto.getCiphers().
/// </summary>
public class CryptoInfoCompiledTests
{
    [Fact]
    public void Crypto_GetHashes_ReturnsArray_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GetHashes_ContainsSha256_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GetCiphers_ReturnsArray_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GetCiphers_ContainsAesCbc_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}
