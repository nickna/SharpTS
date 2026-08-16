using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'crypto' module: createHash, createHmac,
/// randomBytes, randomFillSync, randomUUID, and randomInt.
/// Runs in both interpreter and compiled modes.
/// </summary>
public class CryptoModuleTests
{
    [Theory, ModeData]
    public void Crypto_CreateHash(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                // MD5: 32-char hex, known value
                const md5 = crypto.createHash('md5');
                md5.update('hello');
                const md5d = md5.digest('hex');
                console.log(typeof md5d === 'string');
                console.log(md5d.length === 32);
                console.log(md5d === '5d41402abc4b2a76b9719d911017c592');

                // SHA1: 40-char hex, known value
                const sha1 = crypto.createHash('sha1');
                sha1.update('hello');
                const sha1d = sha1.digest('hex');
                console.log(typeof sha1d === 'string');
                console.log(sha1d.length === 40);
                console.log(sha1d === 'aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d');

                // SHA256: 64-char hex, known value
                const sha256 = crypto.createHash('sha256');
                sha256.update('hello');
                const sha256d = sha256.digest('hex');
                console.log(typeof sha256d === 'string');
                console.log(sha256d.length === 64);
                console.log(sha256d === '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824');

                // SHA512: 128-char hex
                const sha512 = crypto.createHash('sha512');
                sha512.update('hello');
                const sha512d = sha512.digest('hex');
                console.log(typeof sha512d === 'string');
                console.log(sha512d.length === 128);

                // Multiple updates = single update
                const h1 = crypto.createHash('sha256');
                h1.update('helloworld');
                const d1 = h1.digest('hex');
                const h2 = crypto.createHash('sha256');
                h2.update('hello');
                h2.update('world');
                const d2 = h2.digest('hex');
                console.log(d1 === d2);

                // Empty input: known SHA256 of empty string
                const empty = crypto.createHash('sha256');
                empty.update('');
                const emptyD = empty.digest('hex');
                console.log(typeof emptyD === 'string');
                console.log(emptyD.length === 64);
                console.log(emptyD === 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');

                // Base64 encoding
                const b64 = crypto.createHash('sha256');
                b64.update('hello');
                const b64d = b64.digest('base64');
                console.log(typeof b64d === 'string');
                console.log(b64d.length === 44);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        var expected =
            "true\ntrue\ntrue\n" +   // MD5
            "true\ntrue\ntrue\n" +   // SHA1
            "true\ntrue\ntrue\n" +   // SHA256
            "true\ntrue\n" +         // SHA512
            "true\n" +               // multiple updates
            "true\ntrue\ntrue\n" +   // empty input
            "true\ntrue\n";          // base64
        Assert.Equal(expected, output);
    }

    [Theory, ModeData]
    public void Crypto_RandomBytes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                // Returns correct length and is a Buffer
                const bytes16 = crypto.randomBytes(16);
                const bytes32 = crypto.randomBytes(32);
                console.log(Buffer.isBuffer(bytes16));
                console.log(bytes16.length === 16);
                console.log(bytes32.length === 32);

                // All values in range 0-255
                const bytes100 = crypto.randomBytes(100);
                let allInRange = true;
                for (let i = 0; i < bytes100.length; i++) {
                    const b = bytes100.readUInt8(i);
                    if (b < 0 || b > 255) {
                        allInRange = false;
                        break;
                    }
                }
                console.log(allInRange);

                // Not all zeros
                const bytes = crypto.randomBytes(32);
                let hasNonZero = false;
                for (let i = 0; i < bytes.length; i++) {
                    if (bytes.readUInt8(i) !== 0) {
                        hasNonZero = true;
                        break;
                    }
                }
                console.log(hasNonZero);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Crypto_RandomFillSync(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                // Fills entire buffer, returns same buffer, has non-zero bytes
                const buf1 = Buffer.alloc(16);
                const res1 = crypto.randomFillSync(buf1);
                console.log(res1 === buf1);
                console.log(buf1.length === 16);
                let hasNonZero = false;
                for (let i = 0; i < buf1.length; i++) {
                    if (buf1.readUInt8(i) !== 0) {
                        hasNonZero = true;
                        break;
                    }
                }
                console.log(hasNonZero);

                // With offset: first 4 bytes untouched
                const buf2 = Buffer.alloc(16);
                crypto.randomFillSync(buf2, 4);
                console.log(buf2.readUInt8(0) === 0);
                console.log(buf2.readUInt8(1) === 0);
                console.log(buf2.readUInt8(2) === 0);
                console.log(buf2.readUInt8(3) === 0);

                // With offset and size: only bytes 4-7 filled
                const buf3 = Buffer.alloc(16);
                crypto.randomFillSync(buf3, 4, 4);
                console.log(buf3.readUInt8(0) === 0);
                console.log(buf3.readUInt8(3) === 0);
                console.log(buf3.readUInt8(8) === 0);
                console.log(buf3.readUInt8(15) === 0);

                // Returns buffer and is a Buffer
                const buf4 = Buffer.alloc(8);
                const res4 = crypto.randomFillSync(buf4);
                console.log(Buffer.isBuffer(res4));
                console.log(res4 === buf4);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        var expected =
            "true\ntrue\ntrue\n" +       // fills entire buffer
            "true\ntrue\ntrue\ntrue\n" + // with offset
            "true\ntrue\ntrue\ntrue\n" + // with offset and size
            "true\ntrue\n";              // returns buffer
        Assert.Equal(expected, output);
    }

    [Theory, ModeData]
    public void Crypto_RandomUUID(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                // Format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
                const uuid = crypto.randomUUID();
                console.log(typeof uuid === 'string');
                console.log(uuid.length === 36);
                const parts = uuid.split('-');
                console.log(parts.length === 5);
                console.log(parts[0].length === 8);
                console.log(parts[1].length === 4);
                console.log(parts[2].length === 4);
                console.log(parts[3].length === 4);
                console.log(parts[4].length === 12);

                // Two UUIDs should be unique
                const uuid2 = crypto.randomUUID();
                console.log(uuid !== uuid2);

                // Only valid hex chars and dashes
                const validChars = '0123456789abcdef-';
                let allValid = true;
                for (const c of uuid) {
                    if (!validChars.includes(c)) {
                        allValid = false;
                        break;
                    }
                }
                console.log(allValid);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        var expected =
            "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n" + // format
            "true\n" +  // unique
            "true\n";   // valid chars
        Assert.Equal(expected, output);
    }

    [Theory, ModeData]
    public void Crypto_RandomInt(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                // randomInt(max) returns values in [0, max)
                let allInRange1 = true;
                for (let i = 0; i < 100; i++) {
                    const val = crypto.randomInt(10);
                    if (val < 0 || val >= 10) {
                        allInRange1 = false;
                        break;
                    }
                }
                console.log(allInRange1);

                // randomInt(min, max) returns values in [min, max)
                let allInRange2 = true;
                for (let i = 0; i < 100; i++) {
                    const val = crypto.randomInt(5, 15);
                    if (val < 5 || val >= 15) {
                        allInRange2 = false;
                        break;
                    }
                }
                console.log(allInRange2);

                // Returns whole numbers
                const val = crypto.randomInt(100);
                console.log(typeof val === 'number');
                console.log(Math.floor(val) === val);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Crypto_CreateHmac(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                // Known HMAC-SHA256 value
                const hmac1 = crypto.createHmac('sha256', 'key');
                hmac1.update('The quick brown fox jumps over the lazy dog');
                const d1 = hmac1.digest('hex');
                console.log(typeof d1 === 'string');
                console.log(d1.length === 64);
                console.log(d1 === 'f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8');

                // All algorithms produce correct output lengths
                const md5 = crypto.createHmac('md5', 'secret');
                md5.update('test');
                console.log(md5.digest('hex').length === 32);
                const sha1 = crypto.createHmac('sha1', 'secret');
                sha1.update('test');
                console.log(sha1.digest('hex').length === 40);
                const sha256 = crypto.createHmac('sha256', 'secret');
                sha256.update('test');
                console.log(sha256.digest('hex').length === 64);
                const sha384 = crypto.createHmac('sha384', 'secret');
                sha384.update('test');
                console.log(sha384.digest('hex').length === 96);
                const sha512 = crypto.createHmac('sha512', 'secret');
                sha512.update('test');
                console.log(sha512.digest('hex').length === 128);

                // Multiple updates = single update
                const hm1 = crypto.createHmac('sha256', 'secret');
                hm1.update('helloworld');
                const hd1 = hm1.digest('hex');
                const hm2 = crypto.createHmac('sha256', 'secret');
                hm2.update('hello');
                hm2.update('world');
                const hd2 = hm2.digest('hex');
                console.log(hd1 === hd2);

                // Base64 encoding
                const b64 = crypto.createHmac('sha256', 'secret');
                b64.update('hello');
                const b64d = b64.digest('base64');
                console.log(typeof b64d === 'string');
                console.log(b64d.length === 44);

                // Different keys produce different results
                const dk1 = crypto.createHmac('sha256', 'key1');
                dk1.update('message');
                const dkd1 = dk1.digest('hex');
                const dk2 = crypto.createHmac('sha256', 'key2');
                dk2.update('message');
                const dkd2 = dk2.digest('hex');
                console.log(dkd1 !== dkd2);

                // Method chaining
                const chain = crypto.createHmac('sha256', 'secret').update('test').digest('hex');
                console.log(typeof chain === 'string');
                console.log(chain.length === 64);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        var expected =
            "true\ntrue\ntrue\n" +               // known SHA256
            "true\ntrue\ntrue\ntrue\ntrue\n" +   // all algorithms
            "true\n" +                            // multiple updates
            "true\ntrue\n" +                      // base64
            "true\n" +                            // different keys
            "true\ntrue\n";                       // method chaining
        Assert.Equal(expected, output);
    }
}
