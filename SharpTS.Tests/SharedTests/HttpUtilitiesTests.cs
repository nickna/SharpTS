using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for http/https utilities + constants (#1052): validateHeaderName/validateHeaderValue,
/// maxHeaderSize, setMaxIdleHTTPParsers, and the completeness of METHODS. All dual-mode (pure
/// functions/constants emitted in IL).
/// </summary>
public class HttpUtilitiesTests
{
    [Theory, ModeData]
    public void MaxHeaderSize_Is16384(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(http.maxHeaderSize);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("16384\n", output);
    }

    [Theory, ModeData]
    public void ValidateHeaderName_AcceptsValid_RejectsInvalid(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                http.validateHeaderName('X-Custom-Header');
                console.log('valid-ok');
                try {
                    http.validateHeaderName('Bad Header');
                    console.log('NO-THROW');
                } catch (e: any) {
                    console.log('threw:' + e.code);
                }
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("valid-ok", output);
        Assert.Contains("threw:ERR_INVALID_HTTP_TOKEN", output);
        Assert.DoesNotContain("NO-THROW", output);
    }

    [Theory, ModeData]
    public void ValidateHeaderValue_AcceptsValid_RejectsInvalid(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                http.validateHeaderValue('X-A', 'fine value');
                console.log('valid-ok');
                try {
                    http.validateHeaderValue('X-A', 'bad\nvalue');
                    console.log('NO-THROW');
                } catch (e: any) {
                    console.log('threw:' + e.code);
                }
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("valid-ok", output);
        Assert.Contains("threw:ERR_INVALID_CHAR", output);
        Assert.DoesNotContain("NO-THROW", output);
    }

    [Theory, ModeData]
    public void Methods_IncludesFullList(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(http.METHODS.includes('ACL'));
                console.log(http.METHODS.includes('M-SEARCH'));
                console.log(http.METHODS.includes('PROPFIND'));
                console.log(http.METHODS.includes('GET'));
                console.log(http.METHODS.length > 30);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void SetMaxIdleHTTPParsers_IsCallable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                http.setMaxIdleHTTPParsers(5);
                console.log('ok');
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("ok\n", output);
    }

    [Theory, ModeData]
    public void Https_MirrorsUtilities(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as https from 'https';
                console.log(https.maxHeaderSize);
                console.log(typeof https.validateHeaderName);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("16384", output);
        Assert.Contains("function", output);
    }
}
