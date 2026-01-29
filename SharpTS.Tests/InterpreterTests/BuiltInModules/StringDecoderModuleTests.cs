using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'string_decoder' module (interpreter mode).
/// </summary>
public class StringDecoderModuleTests
{
    // ============ IMPORT TESTS ============

    [Fact]
    public void StringDecoder_Import_Named()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                console.log(typeof StringDecoder === 'function');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void StringDecoder_Import_Namespace()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as sd from 'string_decoder';
                console.log(typeof sd.StringDecoder === 'function');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ CONSTRUCTOR TESTS ============

    [Fact]
    public void StringDecoder_Constructor_DefaultEncoding()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder();
                console.log(decoder.encoding);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("utf8\n", output);
    }

    [Fact]
    public void StringDecoder_Constructor_Utf8Encoding()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('utf8');
                console.log(decoder.encoding);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("utf8\n", output);
    }

    [Fact]
    public void StringDecoder_Constructor_Latin1Encoding()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('latin1');
                console.log(decoder.encoding);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("latin1\n", output);
    }

    // ============ WRITE TESTS ============

    [Fact]
    public void StringDecoder_Write_SimpleAscii()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('utf8');
                const buf = Buffer.from('Hello, World!');
                const result = decoder.write(buf);
                console.log(result);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("Hello, World!\n", output);
    }

    [Fact]
    public void StringDecoder_Write_Utf8()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('utf8');
                const buf = Buffer.from([0xC3, 0xA9]); // é in UTF-8
                const result = decoder.write(buf);
                console.log(result === 'é');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ END TESTS ============

    [Fact]
    public void StringDecoder_End_NoArgument()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('utf8');
                const buf = Buffer.from('test');
                decoder.write(buf);
                const result = decoder.end();
                console.log(result === '');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void StringDecoder_End_WithBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('utf8');
                const buf = Buffer.from('end data');
                const result = decoder.end(buf);
                console.log(result);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("end data\n", output);
    }

    // ============ CHAINED WRITES ============

    [Fact]
    public void StringDecoder_Write_MultipleChunks()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('utf8');
                let result = '';
                result += decoder.write(Buffer.from('Hello'));
                result += decoder.write(Buffer.from(' '));
                result += decoder.write(Buffer.from('World'));
                result += decoder.end();
                console.log(result);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("Hello World\n", output);
    }
}
