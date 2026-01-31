using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'string_decoder' module: StringDecoder class.
/// </summary>
public class StringDecoderModuleTests
{
    #region Import Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_Import_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                console.log(typeof StringDecoder === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_Import_Namespace(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as sd from 'string_decoder';
                console.log(typeof sd.StringDecoder === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Constructor Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_Constructor_DefaultEncoding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder();
                console.log(decoder.encoding);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("utf8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_Constructor_Utf8Encoding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('utf8');
                console.log(decoder.encoding);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("utf8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_Constructor_Latin1Encoding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('latin1');
                console.log(decoder.encoding);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("latin1\n", output);
    }

    #endregion

    #region Write Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_Write_SimpleAscii(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Hello, World!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_Write_Utf8(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region End Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_End_NoArgument(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_End_WithBuffer(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("end data\n", output);
    }

    #endregion

    #region Chained Writes Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringDecoder_Write_MultipleChunks(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Hello World\n", output);
    }

    #endregion
}
