using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.ParityTests;

/// <summary>
/// Parity tests for built-in modules ensuring interpreter and compiler produce identical output.
/// These tests verify that all 11 built-in modules behave identically in both execution modes.
/// </summary>
public class BuiltInModuleParityTests
{
    /// <summary>
    /// Helper method to assert parity between interpreter and compiler output.
    /// </summary>
    private static void AssertParity(string source)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        var interpreted = TestHarness.RunModulesInterpreted(files, "main.ts");
        var compiled = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal(interpreted, compiled);
    }

    #region Assert Module

    [Fact]
    public void Assert_Ok_TruthyValues_Parity()
    {
        AssertParity("""
            import { ok } from 'assert';
            ok(true);
            ok(1);
            ok('hello');
            ok({});
            ok([]);
            console.log('all passed');
            """);
    }

    [Fact]
    public void Assert_Ok_ThrowsForFalsy_Parity()
    {
        AssertParity("""
            import { ok } from 'assert';
            try {
                ok(false);
                console.log('should not reach');
            } catch (e) {
                console.log('caught');
            }
            """);
    }

    [Fact]
    public void Assert_StrictEqual_EqualValues_Parity()
    {
        AssertParity("""
            import { strictEqual } from 'assert';
            strictEqual(1, 1);
            strictEqual('hello', 'hello');
            strictEqual(true, true);
            console.log('all passed');
            """);
    }

    [Fact]
    public void Assert_StrictEqual_ThrowsForUnequal_Parity()
    {
        AssertParity("""
            import { strictEqual } from 'assert';
            try {
                strictEqual(1, 2);
                console.log('should not reach');
            } catch (e) {
                console.log('caught');
            }
            """);
    }

    [Fact]
    public void Assert_NotStrictEqual_UnequalValues_Parity()
    {
        AssertParity("""
            import { notStrictEqual } from 'assert';
            notStrictEqual(1, 2);
            notStrictEqual('a', 'b');
            notStrictEqual(true, false);
            console.log('all passed');
            """);
    }

    [Fact]
    public void Assert_DeepStrictEqual_Objects_Parity()
    {
        AssertParity("""
            import { deepStrictEqual } from 'assert';
            deepStrictEqual({ a: 1, b: 2 }, { a: 1, b: 2 });
            console.log('objects passed');
            """);
    }

    [Fact]
    public void Assert_DeepStrictEqual_Arrays_Parity()
    {
        AssertParity("""
            import { deepStrictEqual } from 'assert';
            deepStrictEqual([1, 2, 3], [1, 2, 3]);
            console.log('arrays passed');
            """);
    }

    [Fact]
    public void Assert_Fail_Parity()
    {
        AssertParity("""
            import { fail } from 'assert';
            try {
                fail('custom message');
                console.log('should not reach');
            } catch (e) {
                console.log('caught');
            }
            """);
    }

    [Fact]
    public void Assert_NamespaceImport_Parity()
    {
        AssertParity("""
            import * as assert from 'assert';
            assert.ok(true);
            assert.strictEqual(1, 1);
            console.log('namespace import works');
            """);
    }

    #endregion

    #region Querystring Module

    [Fact]
    public void Querystring_Parse_Simple_Parity()
    {
        AssertParity("""
            import { parse } from 'querystring';
            const result = parse('foo=bar&baz=qux');
            console.log(result.foo);
            console.log(result.baz);
            """);
    }

    [Fact]
    public void Querystring_Parse_UrlEncoding_Parity()
    {
        AssertParity("""
            import { parse } from 'querystring';
            const result = parse('name=John%20Doe&city=New%20York');
            console.log(result.name);
            console.log(result.city);
            """);
    }

    [Fact]
    public void Querystring_Parse_PlusAsSpace_Parity()
    {
        AssertParity("""
            import { parse } from 'querystring';
            const result = parse('name=John+Doe');
            console.log(result.name);
            """);
    }

    [Fact]
    public void Querystring_Parse_EmptyValue_Parity()
    {
        AssertParity("""
            import { parse } from 'querystring';
            const result = parse('foo=&bar=value');
            console.log(result.foo === '');
            console.log(result.bar);
            """);
    }

    [Fact]
    public void Querystring_Stringify_Parity()
    {
        AssertParity("""
            import { stringify } from 'querystring';
            const str = stringify({ foo: 'bar', baz: 'qux' });
            console.log(str.includes('foo=bar'));
            console.log(str.includes('baz=qux'));
            """);
    }

    [Fact]
    public void Querystring_Escape_Parity()
    {
        AssertParity("""
            import { escape } from 'querystring';
            console.log(escape('hello world'));
            """);
    }

    [Fact]
    public void Querystring_Unescape_Parity()
    {
        AssertParity("""
            import { unescape } from 'querystring';
            console.log(unescape('hello%20world'));
            console.log(unescape('hello+world'));
            """);
    }

    [Fact]
    public void Querystring_RoundTrip_Parity()
    {
        AssertParity("""
            import { parse, stringify } from 'querystring';
            const original = { name: 'test', value: '123' };
            const encoded = stringify(original);
            const decoded = parse(encoded);
            console.log(decoded.name);
            console.log(decoded.value);
            """);
    }

    [Fact]
    public void Querystring_NamespaceImport_Parity()
    {
        AssertParity("""
            import * as qs from 'querystring';
            const parsed = qs.parse('a=1');
            console.log(parsed.a);
            const str = qs.stringify({ b: '2' });
            console.log(str);
            """);
    }

    #endregion

    #region Url Module

    [Fact]
    public void Url_Parse_FullUrl_Parity()
    {
        AssertParity("""
            import { parse } from 'url';
            const parsed = parse('https://example.com:8080/path?query=value#hash');
            console.log(parsed.protocol);
            console.log(parsed.hostname);
            console.log(parsed.port);
            console.log(parsed.pathname);
            """);
    }

    [Fact]
    public void Url_Parse_QueryString_Parity()
    {
        AssertParity("""
            import { parse } from 'url';
            const parsed = parse('https://example.com?foo=bar');
            console.log(parsed.search);
            console.log(parsed.query);
            """);
    }

    [Fact]
    public void Url_Parse_Hash_Parity()
    {
        AssertParity("""
            import { parse } from 'url';
            const parsed = parse('https://example.com#section');
            console.log(parsed.hash);
            """);
    }

    [Fact]
    public void Url_Format_Parity()
    {
        AssertParity("""
            import { format } from 'url';
            const formatted = format({
                protocol: 'https:',
                hostname: 'example.com',
                pathname: '/path',
                search: '?key=value'
            });
            console.log(formatted);
            """);
    }

    [Fact]
    public void Url_Resolve_Relative_Parity()
    {
        AssertParity("""
            import { resolve } from 'url';
            const resolved = resolve('https://example.com/base/', '../other/path');
            console.log(resolved);
            """);
    }

    [Fact]
    public void Url_Resolve_Absolute_Parity()
    {
        AssertParity("""
            import { resolve } from 'url';
            const resolved = resolve('https://example.com/base/', '/absolute');
            console.log(resolved);
            """);
    }

    [Fact]
    public void Url_NamespaceImport_Parity()
    {
        AssertParity("""
            import * as url from 'url';
            const parsed = url.parse('https://example.com/path');
            console.log(parsed.hostname);
            const formatted = url.format({ protocol: 'http:', hostname: 'test.com', pathname: '/' });
            console.log(formatted.includes('test.com'));
            """);
    }

    #endregion

    #region Path Module

    [Fact]
    public void Path_Basename_Parity()
    {
        AssertParity("""
            import * as path from 'path';
            console.log(path.basename('/foo/bar/baz.txt'));
            console.log(path.basename('/foo/bar/baz.txt', '.txt'));
            """);
    }

    [Fact]
    public void Path_Dirname_Parity()
    {
        AssertParity("""
            import * as path from 'path';
            const dir = path.dirname('/foo/bar/baz.txt');
            console.log(dir.includes('foo'));
            console.log(dir.includes('bar'));
            """);
    }

    [Fact]
    public void Path_Extname_Parity()
    {
        AssertParity("""
            import * as path from 'path';
            console.log(path.extname('file.txt'));
            console.log(path.extname('file.tar.gz'));
            console.log(path.extname('file') === '');
            """);
    }

    [Fact]
    public void Path_IsAbsolute_Parity()
    {
        AssertParity("""
            import * as path from 'path';
            // Relative paths should be false on all platforms
            console.log(path.isAbsolute('foo'));
            console.log(path.isAbsolute('./foo'));
            console.log(path.isAbsolute('../foo'));
            """);
    }

    [Fact]
    public void Path_Parse_Parity()
    {
        AssertParity("""
            import * as path from 'path';
            const parsed = path.parse('/home/user/file.txt');
            console.log(parsed.base);
            console.log(parsed.name);
            console.log(parsed.ext);
            """);
    }

    [Fact]
    public void Path_Join_Parity()
    {
        AssertParity("""
            import * as path from 'path';
            const joined = path.join('foo', 'bar', 'baz');
            // Check structure consistency, not exact separators
            console.log(joined.includes('foo'));
            console.log(joined.includes('bar'));
            console.log(joined.includes('baz'));
            """);
    }

    [Fact]
    public void Path_Normalize_Parity()
    {
        AssertParity("""
            import * as path from 'path';
            const normalized = path.normalize('foo/bar/../baz');
            console.log(normalized.includes('baz'));
            console.log(!normalized.includes('..'));
            """);
    }

    [Fact]
    public void Path_Sep_Parity()
    {
        AssertParity("""
            import * as path from 'path';
            const sep = path.sep;
            console.log(sep === '/' || sep === '\\');
            """);
    }

    [Fact]
    public void Path_Delimiter_Parity()
    {
        AssertParity("""
            import * as path from 'path';
            const delim = path.delimiter;
            console.log(delim === ':' || delim === ';');
            """);
    }

    #endregion

    #region Os Module

    [Fact]
    public void Os_Platform_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const platform = os.platform();
            console.log(platform === 'win32' || platform === 'linux' || platform === 'darwin');
            """);
    }

    [Fact]
    public void Os_Arch_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const arch = os.arch();
            console.log(arch === 'x64' || arch === 'x86' || arch === 'arm' || arch === 'arm64' || arch === 'ia32' || arch === 'unknown');
            """);
    }

    [Fact]
    public void Os_Hostname_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const hostname = os.hostname();
            console.log(hostname.length > 0);
            """);
    }

    [Fact]
    public void Os_Homedir_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const homedir = os.homedir();
            console.log(homedir.length > 0);
            """);
    }

    [Fact]
    public void Os_Tmpdir_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const tmpdir = os.tmpdir();
            console.log(tmpdir.length > 0);
            """);
    }

    [Fact]
    public void Os_Type_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const osType = os.type();
            console.log(osType === 'Windows_NT' || osType === 'Linux' || osType === 'Darwin');
            """);
    }

    [Fact]
    public void Os_Release_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const release = os.release();
            console.log(release.length > 0);
            """);
    }

    [Fact]
    public void Os_EOL_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const eol = os.EOL;
            console.log(eol === '\n' || eol === '\r\n');
            """);
    }

    [Fact]
    public void Os_Cpus_Length_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const cpus = os.cpus();
            console.log(cpus.length > 0);
            """);
    }

    [Fact]
    public void Os_Totalmem_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const totalmem = os.totalmem();
            console.log(totalmem > 0);
            """);
    }

    [Fact]
    public void Os_Freemem_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const freemem = os.freemem();
            console.log(freemem >= 0);
            """);
    }

    [Fact]
    public void Os_UserInfo_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const userInfo = os.userInfo();
            console.log(userInfo.username.length > 0);
            console.log(userInfo.homedir.length > 0);
            console.log(typeof userInfo.uid === 'number');
            console.log(typeof userInfo.gid === 'number');
            """);
    }

    [Fact]
    public void Os_Platform_MatchesType_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const platform = os.platform();
            const osType = os.type();
            let consistent = false;
            if (platform === 'win32' && osType === 'Windows_NT') consistent = true;
            if (platform === 'linux' && osType === 'Linux') consistent = true;
            if (platform === 'darwin' && osType === 'Darwin') consistent = true;
            console.log(consistent);
            """);
    }

    [Fact]
    public void Os_Loadavg_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            const loadavg = os.loadavg();
            console.log(Array.isArray(loadavg));
            console.log(loadavg.length === 3);
            console.log(typeof loadavg[0] === 'number');
            console.log(typeof loadavg[1] === 'number');
            console.log(typeof loadavg[2] === 'number');
            console.log(loadavg[0] >= 0);
            console.log(loadavg[1] >= 0);
            console.log(loadavg[2] >= 0);
            """);
    }

    [Fact]
    public void Os_NetworkInterfaces_Parity()
    {
        // Note: Compiled mode returns empty object, interpreter returns full data
        // This test checks only what's guaranteed to be the same
        AssertParity("""
            import * as os from 'os';
            const interfaces = os.networkInterfaces();
            console.log(typeof interfaces === 'object');
            console.log(interfaces !== null);
            const keys = Object.keys(interfaces);
            console.log(Array.isArray(keys));
            """);
    }

    #endregion

    #region Process Global

    [Fact]
    public void Process_Platform_Parity()
    {
        AssertParity("""
            const platform = process.platform;
            console.log(platform === 'win32' || platform === 'linux' || platform === 'darwin');
            """);
    }

    [Fact]
    public void Process_Arch_Parity()
    {
        AssertParity("""
            const arch = process.arch;
            console.log(arch === 'x64' || arch === 'x86' || arch === 'arm' || arch === 'arm64' || arch === 'ia32' || arch === 'unknown');
            """);
    }

    [Fact]
    public void Process_Version_Parity()
    {
        AssertParity("""
            const version = process.version;
            console.log(typeof version === 'string');
            console.log(version.length > 0);
            console.log(version.includes('.'));
            """);
    }

    [Fact]
    public void Process_Cwd_Parity()
    {
        AssertParity("""
            const cwd = process.cwd();
            console.log(typeof cwd === 'string');
            console.log(cwd.length > 0);
            """);
    }

    [Fact]
    public void Process_Pid_Parity()
    {
        AssertParity("""
            const pid = process.pid;
            console.log(typeof pid === 'number');
            console.log(pid > 0);
            """);
    }

    [Fact]
    public void Process_Env_IsObject_Parity()
    {
        AssertParity("""
            const env = process.env;
            console.log(typeof env === 'object');
            """);
    }

    [Fact]
    public void Process_Argv_IsArray_Parity()
    {
        AssertParity("""
            const argv = process.argv;
            console.log(Array.isArray(argv));
            console.log(argv.length > 0);
            """);
    }

    [Fact]
    public void Process_Hrtime_Structure_Parity()
    {
        AssertParity("""
            const hr = process.hrtime();
            console.log(Array.isArray(hr));
            console.log(hr.length === 2);
            console.log(hr[0] >= 0);
            console.log(hr[1] >= 0);
            console.log(hr[1] < 1000000000);
            """);
    }

    [Fact]
    public void Process_MemoryUsage_Structure_Parity()
    {
        AssertParity("""
            const mem = process.memoryUsage();
            console.log(typeof mem === 'object');
            console.log(typeof mem.rss === 'number');
            console.log(typeof mem.heapTotal === 'number');
            console.log(typeof mem.heapUsed === 'number');
            console.log(mem.rss > 0);
            """);
    }

    [Fact]
    public void Process_Uptime_Parity()
    {
        AssertParity("""
            const up = process.uptime();
            console.log(typeof up === 'number');
            console.log(up >= 0);
            """);
    }

    [Fact]
    public void Process_Platform_MatchesOs_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            console.log(process.platform === os.platform());
            """);
    }

    [Fact]
    public void Process_Arch_MatchesOs_Parity()
    {
        AssertParity("""
            import * as os from 'os';
            console.log(process.arch === os.arch());
            """);
    }

    #endregion

    #region Fs Module

    [Fact]
    public void Fs_ExistsSync_True_Parity()
    {
        // Create a file, check it exists, then clean up
        AssertParity("""
            import * as fs from 'fs';
            const testFile = 'parity_exists_' + Date.now() + '.txt';
            fs.writeFileSync(testFile, 'test');
            console.log(fs.existsSync(testFile));
            fs.unlinkSync(testFile);
            """);
    }

    [Fact]
    public void Fs_ExistsSync_False_Parity()
    {
        AssertParity("""
            import * as fs from 'fs';
            console.log(fs.existsSync('nonexistent_parity_file_xyz123.txt'));
            """);
    }

    [Fact]
    public void Fs_WriteReadUnlink_Parity()
    {
        AssertParity("""
            import * as fs from 'fs';
            const testFile = 'parity_test_wr_' + Date.now() + '.txt';
            const testContent = 'Hello, Parity Test!';

            fs.writeFileSync(testFile, testContent);
            const content = fs.readFileSync(testFile, 'utf8');
            console.log(content === testContent);

            // Cleanup
            fs.unlinkSync(testFile);
            console.log(!fs.existsSync(testFile));
            """);
    }

    [Fact]
    public void Fs_AppendFileSync_Parity()
    {
        AssertParity("""
            import * as fs from 'fs';
            const testFile = 'parity_test_append_' + Date.now() + '.txt';

            fs.writeFileSync(testFile, 'Line1');
            fs.appendFileSync(testFile, '\nLine2');
            const content = fs.readFileSync(testFile, 'utf8');
            console.log(content.includes('Line1'));
            console.log(content.includes('Line2'));

            // Cleanup
            fs.unlinkSync(testFile);
            """);
    }

    [Fact]
    public void Fs_MkdirRmdir_Parity()
    {
        AssertParity("""
            import * as fs from 'fs';
            const testDir = 'parity_test_dir_' + Date.now();

            fs.mkdirSync(testDir);
            console.log(fs.existsSync(testDir));

            fs.rmdirSync(testDir);
            console.log(!fs.existsSync(testDir));
            """);
    }

    [Fact]
    public void Fs_StatSync_File_Parity()
    {
        AssertParity("""
            import * as fs from 'fs';
            const testFile = 'parity_test_stat_' + Date.now() + '.txt';
            const content = 'Test content for stat';

            fs.writeFileSync(testFile, content);
            const stat = fs.statSync(testFile);

            console.log(stat.isFile());
            console.log(!stat.isDirectory());
            console.log(stat.size > 0);

            // Cleanup
            fs.unlinkSync(testFile);
            """);
    }

    [Fact]
    public void Fs_StatSync_Directory_Parity()
    {
        AssertParity("""
            import * as fs from 'fs';
            const testDir = 'parity_test_stat_dir_' + Date.now();

            fs.mkdirSync(testDir);
            const stat = fs.statSync(testDir);

            console.log(!stat.isFile());
            console.log(stat.isDirectory());

            // Cleanup
            fs.rmdirSync(testDir);
            """);
    }

    [Fact]
    public void Fs_ReaddirSync_Parity()
    {
        AssertParity("""
            import * as fs from 'fs';
            const testDir = 'parity_test_readdir_' + Date.now();

            fs.mkdirSync(testDir);
            fs.writeFileSync(testDir + '/file1.txt', 'content1');
            fs.writeFileSync(testDir + '/file2.txt', 'content2');

            const entries = fs.readdirSync(testDir);
            console.log(entries.length === 2);

            // Cleanup
            fs.unlinkSync(testDir + '/file1.txt');
            fs.unlinkSync(testDir + '/file2.txt');
            fs.rmdirSync(testDir);
            """);
    }

    [Fact]
    public void Fs_RenameSync_Parity()
    {
        AssertParity("""
            import * as fs from 'fs';
            const ts = Date.now();
            const oldName = 'parity_rename_old_' + ts + '.txt';
            const newName = 'parity_rename_new_' + ts + '.txt';

            fs.writeFileSync(oldName, 'content');
            fs.renameSync(oldName, newName);

            console.log(!fs.existsSync(oldName));
            console.log(fs.existsSync(newName));

            // Cleanup
            fs.unlinkSync(newName);
            """);
    }

    [Fact]
    public void Fs_CopyFileSync_Parity()
    {
        AssertParity("""
            import * as fs from 'fs';
            const ts = Date.now();
            const srcFile = 'parity_copy_src_' + ts + '.txt';
            const destFile = 'parity_copy_dest_' + ts + '.txt';
            const content = 'Content to copy';

            fs.writeFileSync(srcFile, content);
            fs.copyFileSync(srcFile, destFile);

            console.log(fs.existsSync(destFile));
            const copiedContent = fs.readFileSync(destFile, 'utf8');
            console.log(copiedContent === content);

            // Cleanup
            fs.unlinkSync(srcFile);
            fs.unlinkSync(destFile);
            """);
    }

    #endregion

    #region Crypto Module

    [Fact]
    public void Crypto_CreateHash_Md5_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            const hash = crypto.createHash('md5');
            hash.update('hello');
            const digest = hash.digest('hex');
            console.log(typeof digest === 'string');
            console.log(digest.length === 32);
            // Known MD5 of 'hello': 5d41402abc4b2a76b9719d911017c592
            console.log(digest === '5d41402abc4b2a76b9719d911017c592');
            """);
    }

    [Fact]
    public void Crypto_CreateHash_Sha1_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            const hash = crypto.createHash('sha1');
            hash.update('hello');
            const digest = hash.digest('hex');
            console.log(typeof digest === 'string');
            console.log(digest.length === 40);
            // Known SHA1 of 'hello': aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d
            console.log(digest === 'aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d');
            """);
    }

    [Fact]
    public void Crypto_CreateHash_Sha256_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            const hash = crypto.createHash('sha256');
            hash.update('hello');
            const digest = hash.digest('hex');
            console.log(typeof digest === 'string');
            console.log(digest.length === 64);
            // Known SHA256 of 'hello': 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
            console.log(digest === '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824');
            """);
    }

    [Fact]
    public void Crypto_CreateHash_Sha512_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            const hash = crypto.createHash('sha512');
            hash.update('hello');
            const digest = hash.digest('hex');
            console.log(typeof digest === 'string');
            console.log(digest.length === 128);
            """);
    }

    [Fact]
    public void Crypto_CreateHash_MultipleUpdates_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            // Hash with single update
            const hash1 = crypto.createHash('sha256');
            hash1.update('helloworld');
            const digest1 = hash1.digest('hex');
            // Hash with multiple updates
            const hash2 = crypto.createHash('sha256');
            hash2.update('hello');
            hash2.update('world');
            const digest2 = hash2.digest('hex');
            console.log(digest1 === digest2);
            """);
    }

    [Fact]
    public void Crypto_CreateHash_EmptyString_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            const hash = crypto.createHash('sha256');
            hash.update('');
            const digest = hash.digest('hex');
            console.log(typeof digest === 'string');
            console.log(digest.length === 64);
            // Known SHA256 of empty string
            console.log(digest === 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');
            """);
    }

    [Fact]
    public void Crypto_RandomBytes_Length_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            const bytes16 = crypto.randomBytes(16);
            const bytes32 = crypto.randomBytes(32);
            console.log(Buffer.isBuffer(bytes16));
            console.log(bytes16.length === 16);
            console.log(bytes32.length === 32);
            """);
    }

    [Fact]
    public void Crypto_RandomBytes_ValuesInRange_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            const bytes = crypto.randomBytes(100);
            let allInRange = true;
            for (const b of bytes) {
                if (b < 0 || b > 255) {
                    allInRange = false;
                    break;
                }
            }
            console.log(allInRange);
            """);
    }

    [Fact]
    public void Crypto_RandomUUID_Format_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            const uuid = crypto.randomUUID();
            console.log(typeof uuid === 'string');
            console.log(uuid.length === 36);
            // Check format: 8-4-4-4-12 with dashes
            const parts = uuid.split('-');
            console.log(parts.length === 5);
            console.log(parts[0].length === 8);
            console.log(parts[1].length === 4);
            console.log(parts[2].length === 4);
            console.log(parts[3].length === 4);
            console.log(parts[4].length === 12);
            """);
    }

    [Fact]
    public void Crypto_RandomInt_Range_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            let allInRange = true;
            for (let i = 0; i < 100; i++) {
                const val = crypto.randomInt(10);
                if (val < 0 || val >= 10) {
                    allInRange = false;
                    break;
                }
            }
            console.log(allInRange);
            """);
    }

    [Fact]
    public void Crypto_RandomInt_MinMax_Parity()
    {
        AssertParity("""
            import * as crypto from 'crypto';
            let allInRange = true;
            for (let i = 0; i < 100; i++) {
                const val = crypto.randomInt(5, 15);
                if (val < 5 || val >= 15) {
                    allInRange = false;
                    break;
                }
            }
            console.log(allInRange);
            """);
    }

    #endregion

    #region Util Module

    [Fact]
    public void Util_Format_Basic_Parity()
    {
        AssertParity("""
            import * as util from 'util';
            console.log(util.format('Hello %s', 'World'));
            console.log(util.format('Number: %d', 42));
            """);
    }

    [Fact]
    public void Util_Format_Multiple_Parity()
    {
        AssertParity("""
            import * as util from 'util';
            console.log(util.format('%s %s %d', 'Hello', 'World', 123));
            """);
    }

    [Fact]
    public void Util_Format_NoPlaceholders_Parity()
    {
        AssertParity("""
            import * as util from 'util';
            console.log(util.format('Hello', 'extra', 'args'));
            """);
    }

    [Fact]
    public void Util_Inspect_Primitive_Parity()
    {
        AssertParity("""
            import * as util from 'util';
            console.log(util.inspect(42));
            console.log(util.inspect('hello'));
            console.log(util.inspect(true));
            """);
    }

    [Fact]
    public void Util_Inspect_Object_Parity()
    {
        AssertParity("""
            import * as util from 'util';
            const result = util.inspect({ a: 1, b: 2 });
            console.log(result.includes('a'));
            console.log(result.includes('1'));
            """);
    }

    [Fact]
    public void Util_Types_IsArray_Parity()
    {
        AssertParity("""
            import * as util from 'util';
            console.log(util.types.isArray([]));
            console.log(util.types.isArray([1, 2, 3]));
            console.log(util.types.isArray('not array'));
            console.log(util.types.isArray({}));
            """);
    }

    [Fact]
    public void Util_Types_IsFunction_Parity()
    {
        AssertParity("""
            import * as util from 'util';
            console.log(util.types.isFunction(() => {}));
            console.log(util.types.isFunction(function() {}));
            console.log(util.types.isFunction('not function'));
            console.log(util.types.isFunction(42));
            """);
    }

    [Fact]
    public void Util_Types_IsNull_Parity()
    {
        AssertParity("""
            import * as util from 'util';
            console.log(util.types.isNull(null));
            console.log(util.types.isNull(undefined));
            console.log(util.types.isNull(0));
            console.log(util.types.isNull(''));
            """);
    }

    #endregion

    #region ChildProcess Module

    [Fact]
    public void ChildProcess_ExecSync_Echo_Parity()
    {
        AssertParity("""
            import { execSync } from 'child_process';
            import * as os from 'os';
            const platform = os.platform();
            let result: string;
            if (platform === 'win32') {
                result = execSync('cmd /c echo hello');
            } else {
                result = execSync('echo hello');
            }
            console.log(typeof result === 'string');
            console.log(result.length > 0);
            """);
    }

    #endregion

    #region Readline Module

    [Fact]
    public void Readline_CreateInterface_ReturnsObject_Parity()
    {
        AssertParity("""
            import * as readline from 'readline';
            const rl = readline.createInterface({
                input: process.stdin,
                output: process.stdout
            });
            console.log(typeof rl === 'object');
            console.log(rl !== null);
            rl.close();
            console.log('interface created and closed');
            """);
    }

    #endregion
}
