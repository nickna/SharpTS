using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Dual-mode tests for the epic #1054 phase-1/2 crypto additions owned by the
/// one-shot/hash/constants/getCipherInfo/getCurves/primes slice
/// (#1055/#1056/#1057/#1058/#1062).
/// </summary>
public class CryptoOneShotHashTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_Hash_OneShot_MatchesCreateHash(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const oneShot = crypto.hash('sha256', 'hello world');
                const streaming = crypto.createHash('sha256').update('hello world').digest('hex');
                console.log(oneShot === streaming);
                // Known SHA-256('hello world')
                console.log(oneShot === 'b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_Hash_DefaultsToHex(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const h = crypto.hash('sha1', 'abc');
                console.log(h === 'a9993e364706816aba3e25717850c26c9cd0d89d');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_HashCopy_MatchesIndependentDigest(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const h = crypto.createHash('sha256');
                h.update('foo');
                const clone = h.copy();
                h.update('bar');
                clone.update('bar');
                const a = h.digest('hex');
                const b = clone.digest('hex');
                console.log(a === b);
                console.log(a === crypto.hash('sha256', 'foobar'));
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_Sha3_256(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                // Known SHA3-256('abc')
                console.log(crypto.hash('sha3-256', 'abc') === '3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_Shake256_HonorsOutputLength(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const h = crypto.createHash('shake256', { outputLength: 8 }).update('abc').digest('hex');
                console.log(h.length === 16); // 8 bytes -> 16 hex chars
                // Known SHAKE256('abc') truncated to 8 bytes
                console.log(h === '483366601360a877');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Crypto_GetHashes_IncludesSha3(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hashes = crypto.getHashes();
                console.log(hashes.includes('sha3-256'));
                console.log(hashes.includes('shake128'));
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }
}
