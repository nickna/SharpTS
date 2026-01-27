using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for Buffer type compilation - verifies that Buffer instance methods
/// and properties are correctly emitted to IL.
/// </summary>
public class BufferTests
{
    #region Type Annotation and Basic Operations

    [Fact]
    public void Buffer_TypeAnnotation_Works()
    {
        var source = """
            const buf: Buffer = Buffer.from("hello");
            console.log(buf.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Buffer_TypeInference_Works()
    {
        var source = """
            const buf = Buffer.from("test");
            console.log(buf.toString());
            console.log(buf.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("test\n4\n", output);
    }

    [Fact]
    public void Buffer_Alloc_Works()
    {
        var source = """
            const buf: Buffer = Buffer.alloc(5);
            console.log(buf.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void Buffer_Length_Property()
    {
        var source = """
            const buf: Buffer = Buffer.from("hello world");
            console.log(buf.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("11\n", output);
    }

    #endregion

    #region toString Method

    [Fact]
    public void Buffer_ToString_Default()
    {
        var source = """
            const buf: Buffer = Buffer.from("hello");
            console.log(buf.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Buffer_ToString_WithEncoding()
    {
        var source = """
            const buf: Buffer = Buffer.from("hello");
            console.log(buf.toString("utf8"));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    #endregion

    #region slice Method

    [Fact]
    public void Buffer_Slice_WithBothArgs()
    {
        var source = """
            const buf: Buffer = Buffer.from("hello world");
            const sliced = buf.slice(0, 5);
            console.log(sliced.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Buffer_Slice_StartOnly()
    {
        var source = """
            const buf: Buffer = Buffer.from("hello world");
            const sliced = buf.slice(6);
            console.log(sliced.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("world\n", output);
    }

    [Fact]
    public void Buffer_Slice_NoArgs()
    {
        var source = """
            const buf: Buffer = Buffer.from("test");
            const sliced = buf.slice();
            console.log(sliced.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("test\n", output);
    }

    [Fact]
    public void Buffer_Slice_NegativeStart()
    {
        var source = """
            const buf: Buffer = Buffer.from("hello");
            const sliced = buf.slice(-2);
            console.log(sliced.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("lo\n", output);
    }

    #endregion

    #region copy Method

    [Fact]
    public void Buffer_Copy_Basic()
    {
        var source = """
            const src: Buffer = Buffer.from("hello");
            const dest: Buffer = Buffer.alloc(5);
            const copied = src.copy(dest);
            console.log(copied);
            console.log(dest.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\nhello\n", output);
    }

    [Fact]
    public void Buffer_Copy_WithOffsets()
    {
        var source = """
            const src: Buffer = Buffer.from("hello world");
            const dest: Buffer = Buffer.alloc(5);
            const copied = src.copy(dest, 0, 6, 11);
            console.log(copied);
            console.log(dest.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\nworld\n", output);
    }

    [Fact]
    public void Buffer_Copy_Partial()
    {
        var source = """
            const src: Buffer = Buffer.from("hello");
            const dest: Buffer = Buffer.alloc(3);
            const copied = src.copy(dest, 0, 0, 3);
            console.log(copied);
            console.log(dest.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\nhel\n", output);
    }

    #endregion

    #region compare Method

    [Fact]
    public void Buffer_Compare_Less()
    {
        var source = """
            const a: Buffer = Buffer.from("abc");
            const b: Buffer = Buffer.from("abd");
            console.log(a.compare(b));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-1\n", output);
    }

    [Fact]
    public void Buffer_Compare_Equal()
    {
        var source = """
            const a: Buffer = Buffer.from("abc");
            const b: Buffer = Buffer.from("abc");
            console.log(a.compare(b));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Buffer_Compare_Greater()
    {
        var source = """
            const a: Buffer = Buffer.from("abd");
            const b: Buffer = Buffer.from("abc");
            console.log(a.compare(b));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void Buffer_Compare_DifferentLengths()
    {
        var source = """
            const short: Buffer = Buffer.from("ab");
            const long: Buffer = Buffer.from("abc");
            console.log(short.compare(long));
            console.log(long.compare(short));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-1\n1\n", output);
    }

    #endregion

    #region equals Method

    [Fact]
    public void Buffer_Equals_True()
    {
        var source = """
            const a: Buffer = Buffer.from("hello");
            const b: Buffer = Buffer.from("hello");
            console.log(a.equals(b));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Buffer_Equals_False_DifferentContent()
    {
        var source = """
            const a: Buffer = Buffer.from("hello");
            const b: Buffer = Buffer.from("world");
            console.log(a.equals(b));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Buffer_Equals_False_DifferentLength()
    {
        var source = """
            const a: Buffer = Buffer.from("hello");
            const b: Buffer = Buffer.from("hi");
            console.log(a.equals(b));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region fill Method

    [Fact]
    public void Buffer_Fill_WithNumber()
    {
        var source = """
            const buf: Buffer = Buffer.alloc(5);
            buf.fill(65);
            console.log(buf.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("AAAAA\n", output);
    }

    [Fact]
    public void Buffer_Fill_WithString()
    {
        var source = """
            const buf: Buffer = Buffer.alloc(6);
            buf.fill("XY");
            console.log(buf.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("XYXYXY\n", output);
    }

    [Fact]
    public void Buffer_Fill_WithRange()
    {
        var source = """
            const buf: Buffer = Buffer.alloc(5);
            buf.fill(88, 1, 4);
            console.log(buf.readUInt8(0));
            console.log(buf.readUInt8(1));
            console.log(buf.readUInt8(4));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n88\n0\n", output);
    }

    [Fact]
    public void Buffer_Fill_ReturnsThis()
    {
        var source = """
            const buf: Buffer = Buffer.alloc(3).fill(66);
            console.log(buf.toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("BBB\n", output);
    }

    #endregion

    #region write Method

    [Fact]
    public void Buffer_Write_Basic()
    {
        var source = """
            const buf: Buffer = Buffer.alloc(10);
            const written = buf.write("hello");
            console.log(written);
            console.log(buf.slice(0, 5).toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\nhello\n", output);
    }

    [Fact]
    public void Buffer_Write_WithOffset()
    {
        var source = """
            const buf: Buffer = Buffer.alloc(10);
            buf.write("XX", 0);
            buf.write("YY", 5);
            console.log(buf.slice(0, 2).toString());
            console.log(buf.slice(5, 7).toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("XX\nYY\n", output);
    }

    #endregion

    #region readUInt8 Method

    [Fact]
    public void Buffer_ReadUInt8_Basic()
    {
        var source = """
            const buf: Buffer = Buffer.from("AB");
            console.log(buf.readUInt8(0));
            console.log(buf.readUInt8(1));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("65\n66\n", output);
    }

    [Fact]
    public void Buffer_ReadUInt8_DefaultOffset()
    {
        var source = """
            const buf: Buffer = Buffer.from("X");
            console.log(buf.readUInt8());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("88\n", output);
    }

    [Fact]
    public void Buffer_ReadUInt8_InExpression()
    {
        var source = """
            const buf: Buffer = Buffer.from("AB");
            const sum = buf.readUInt8(0) + buf.readUInt8(1);
            console.log(sum);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("131\n", output);
    }

    #endregion

    #region writeUInt8 Method

    [Fact]
    public void Buffer_WriteUInt8_Basic()
    {
        var source = """
            const buf: Buffer = Buffer.alloc(2);
            const pos1 = buf.writeUInt8(255, 0);
            const pos2 = buf.writeUInt8(128, 1);
            console.log(pos1);
            console.log(pos2);
            console.log(buf.readUInt8(0));
            console.log(buf.readUInt8(1));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n255\n128\n", output);
    }

    [Fact]
    public void Buffer_WriteUInt8_DefaultOffset()
    {
        var source = """
            const buf: Buffer = Buffer.alloc(1);
            buf.writeUInt8(42);
            console.log(buf.readUInt8(0));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region toJSON Method

    [Fact]
    public void Buffer_ToJSON_Structure()
    {
        var source = """
            const buf: Buffer = Buffer.from("hi");
            const json = buf.toJSON();
            console.log(json.type);
            console.log(json.data.length);
            console.log(json.data[0]);
            console.log(json.data[1]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Buffer\n2\n104\n105\n", output);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void Buffer_MethodChaining()
    {
        var source = """
            const result = Buffer.alloc(4).fill(68).slice(1, 3);
            console.log(result.toString());
            console.log(result.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("DD\n2\n", output);
    }

    [Fact]
    public void Buffer_AsFunctionParameter()
    {
        var source = """
            function getLength(buf: Buffer): number {
                return buf.length;
            }
            console.log(getLength(Buffer.from("test")));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("4\n", output);
    }

    [Fact]
    public void Buffer_AsFunctionReturnType()
    {
        var source = """
            function createBuffer(): Buffer {
                return Buffer.alloc(3).fill(67);
            }
            console.log(createBuffer().toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("CCC\n", output);
    }

    [Fact]
    public void Buffer_InArray()
    {
        var source = """
            const buffers: Buffer[] = [
                Buffer.from("one"),
                Buffer.from("two")
            ];
            console.log(buffers[0].toString());
            console.log(buffers[1].toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("one\ntwo\n", output);
    }

    [Fact]
    public void Buffer_InConditional()
    {
        var source = """
            function getBuffer(useA: boolean): Buffer {
                if (useA) {
                    return Buffer.from("AAA");
                }
                return Buffer.from("BBB");
            }
            console.log(getBuffer(true).toString());
            console.log(getBuffer(false).toString());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("AAA\nBBB\n", output);
    }

    [Fact]
    public void Buffer_CompareResultInExpression()
    {
        var source = """
            const a: Buffer = Buffer.from("a");
            const b: Buffer = Buffer.from("b");
            const isLess = a.compare(b) < 0;
            console.log(isLess);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    #endregion
}
