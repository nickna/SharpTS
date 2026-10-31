using System.Text;
using Xunit;

namespace SharpTS.Test262;

public sealed class Test262TraceWriterTests
{
    [Theory]
    [InlineData(0xDF06)]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void InvalidCodeUnitsAreEscapedAndSubsequentRecordsRemainWritable(int codeUnit)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true), leaveOpen: true)
            { AutoFlush = true })
        {
            Test262TraceWriter.WriteLine(writer, "result: {0}", "bad" + (char)codeUnit + "tail");
            Test262TraceWriter.WriteLine(writer, "next: {0}", 2);
        }
        string text = new UTF8Encoding(false, true).GetString(stream.ToArray());
        string[] lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith($"result: bad\\u{codeUnit:X4}tail", lines[0]);
        Assert.EndsWith("next: 2", lines[1]);
        Assert.DoesNotContain('\uFFFD', text);
    }

    [Fact]
    public void ValidUnicodeAndLiteralBackslashTextAreUnchanged()
    {
        const string payload = "C:\\trace\\case.js / \U0001F603 / e\u0301 / \u4E2D / literal \\uDF06";
        using var writer = new StringWriter();
        Test262TraceWriter.WriteLine(writer, "{0}", payload);
        Assert.EndsWith(payload + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public void ValidPairsBetweenInvalidUnitsKeepTheirOriginalCharacters()
    {
        string payload = new(['\uD800', '\uD83D', '\uDE03', '\uDF06']);
        using var writer = new StringWriter();
        Test262TraceWriter.WriteLine(writer, "{0}", payload);
        Assert.EndsWith("\\uD800\U0001F603\\uDF06" + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public void DisabledTraceDoesNotFormatArguments()
    {
        Test262TraceWriter.WriteLine(null, "invalid format {");
    }
}