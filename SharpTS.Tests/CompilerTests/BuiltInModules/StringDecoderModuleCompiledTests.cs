using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'string_decoder' module (compiled mode).
/// </summary>
public class StringDecoderModuleCompiledTests
{
    // ============ IMPORT TESTS ============

    [Fact]
    public void Compiled_StringDecoder_Import_Named()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                console.log(typeof StringDecoder === 'function');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_StringDecoder_Import_Namespace()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as sd from 'string_decoder';
                console.log(typeof sd.StringDecoder === 'function');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    // ============ CONSTRUCTOR TESTS ============

    [Fact]
    public void Compiled_StringDecoder_Constructor_DefaultEncoding()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder();
                console.log(decoder.encoding);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("utf8", output.ToLower());
    }

    // ============ WRITE TESTS ============

    [Fact]
    public void Compiled_StringDecoder_Write_SimpleAscii()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("Hello, World!", output);
    }

    // ============ END TESTS ============

    [Fact]
    public void Compiled_StringDecoder_End_WithBuffer()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("end data", output);
    }

    // ============ CHAINED WRITES ============

    [Fact]
    public void Compiled_StringDecoder_Write_MultipleChunks()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("Hello World", output);
    }
}
