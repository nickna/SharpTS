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

    #region Multi-byte Read Tests

    [Fact]
    public void Buffer_ReadInt8_Basic()
    {
        var source = """
            const buf = Buffer.from([127, 128, 255, 0]);
            console.log(buf.readInt8(0));   // 127
            console.log(buf.readInt8(1));   // -128
            console.log(buf.readInt8(2));   // -1
            console.log(buf.readInt8(3));   // 0
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("127\n-128\n-1\n0\n", output);
    }

    [Fact]
    public void Buffer_ReadInt8_Parity()
    {
        var source = """
            const buf = Buffer.from([127, 128, 255, 0]);
            console.log(buf.readInt8(0));
            console.log(buf.readInt8(1));
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void Buffer_ReadUInt16LE_Basic()
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(120, 0);
            buf.writeUInt8(86, 1);
            buf.writeUInt8(52, 2);
            buf.writeUInt8(18, 3);
            console.log(buf.readUInt16LE(0));  // 22136
            console.log(buf.readUInt16LE(2));  // 4660
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("22136\n4660\n", output);
    }

    [Fact]
    public void Buffer_ReadUInt16BE_Basic()
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(18, 0);
            buf.writeUInt8(52, 1);
            buf.writeUInt8(86, 2);
            buf.writeUInt8(120, 3);
            console.log(buf.readUInt16BE(0));  // 4660
            console.log(buf.readUInt16BE(2));  // 22136
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("4660\n22136\n", output);
    }

    [Fact]
    public void Buffer_ReadUInt16_Parity()
    {
        var source = """
            const buf = Buffer.from([18, 52, 86, 120]);
            console.log(buf.readUInt16LE(0));
            console.log(buf.readUInt16BE(0));
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void Buffer_ReadUInt32LE_Basic()
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(120, 0);
            buf.writeUInt8(86, 1);
            buf.writeUInt8(52, 2);
            buf.writeUInt8(18, 3);
            console.log(buf.readUInt32LE(0));  // 305419896
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("305419896\n", output);
    }

    [Fact]
    public void Buffer_ReadUInt32BE_Basic()
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(18, 0);
            buf.writeUInt8(52, 1);
            buf.writeUInt8(86, 2);
            buf.writeUInt8(120, 3);
            console.log(buf.readUInt32BE(0));  // 305419896
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("305419896\n", output);
    }

    [Fact]
    public void Buffer_ReadInt16LE_Signed()
    {
        var source = """
            const buf = Buffer.alloc(2);
            buf.writeUInt8(255, 0);
            buf.writeUInt8(255, 1);
            console.log(buf.readInt16LE(0));  // -1
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-1\n", output);
    }

    [Fact]
    public void Buffer_ReadInt32LE_Signed()
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(255, 0);
            buf.writeUInt8(255, 1);
            buf.writeUInt8(255, 2);
            buf.writeUInt8(255, 3);
            console.log(buf.readInt32LE(0));  // -1
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-1\n", output);
    }

    [Fact]
    public void Buffer_ReadFloat_Parity()
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeFloatLE(3.14, 0);
            const val = buf.readFloatLE(0);
            console.log(Math.abs(val - 3.14) < 0.001);
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void Buffer_ReadDouble_Parity()
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeDoubleLE(3.141592653589793, 0);
            const val = buf.readDoubleLE(0);
            console.log(Math.abs(val - 3.141592653589793) < 0.0000001);
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    #endregion

    #region Multi-byte Write Tests

    [Fact]
    public void Buffer_WriteInt8_Basic()
    {
        var source = """
            const buf = Buffer.alloc(2);
            buf.writeInt8(-1, 0);
            buf.writeInt8(127, 1);
            console.log(buf.readUInt8(0));  // 255
            console.log(buf.readUInt8(1));  // 127
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("255\n127\n", output);
    }

    [Fact]
    public void Buffer_WriteUInt16LE_Basic()
    {
        var source = """
            const buf = Buffer.alloc(4);
            const next = buf.writeUInt16LE(4660, 0);
            buf.writeUInt16LE(22136, next);
            console.log(buf.readUInt8(0));  // 52
            console.log(buf.readUInt8(1));  // 18
            console.log(buf.readUInt8(2));  // 120
            console.log(buf.readUInt8(3));  // 86
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("52\n18\n120\n86\n", output);
    }

    [Fact]
    public void Buffer_WriteUInt16BE_Basic()
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt16BE(4660, 0);
            buf.writeUInt16BE(22136, 2);
            console.log(buf.readUInt8(0));  // 18
            console.log(buf.readUInt8(1));  // 52
            console.log(buf.readUInt8(2));  // 86
            console.log(buf.readUInt8(3));  // 120
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("18\n52\n86\n120\n", output);
    }

    [Fact]
    public void Buffer_WriteUInt32_RoundTrip()
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeUInt32LE(305419896, 0);  // 0x12345678
            buf.writeUInt32BE(305419896, 4);  // 0x12345678
            console.log(buf.readUInt32LE(0));
            console.log(buf.readUInt32BE(4));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("305419896\n305419896\n", output);
    }

    [Fact]
    public void Buffer_WriteInt16_RoundTrip()
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeInt16LE(-1000, 0);
            buf.writeInt16BE(-1000, 2);
            console.log(buf.readInt16LE(0));
            console.log(buf.readInt16BE(2));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-1000\n-1000\n", output);
    }

    [Fact]
    public void Buffer_WriteInt32_RoundTrip()
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeInt32LE(-100000, 0);
            buf.writeInt32BE(-100000, 4);
            console.log(buf.readInt32LE(0));
            console.log(buf.readInt32BE(4));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-100000\n-100000\n", output);
    }

    [Fact]
    public void Buffer_WriteFloat_RoundTrip()
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeFloatLE(3.14, 0);
            buf.writeFloatBE(2.71, 4);
            const val1 = buf.readFloatLE(0);
            const val2 = buf.readFloatBE(4);
            console.log(Math.abs(val1 - 3.14) < 0.001);
            console.log(Math.abs(val2 - 2.71) < 0.001);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Buffer_WriteDouble_RoundTrip()
    {
        var source = """
            const buf = Buffer.alloc(16);
            buf.writeDoubleLE(Math.PI, 0);
            buf.writeDoubleBE(Math.E, 8);
            console.log(buf.readDoubleLE(0) === Math.PI);
            console.log(buf.readDoubleBE(8) === Math.E);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Buffer_Write_ReturnsNextOffset()
    {
        var source = """
            const buf = Buffer.alloc(10);
            let offset = 0;
            offset = buf.writeUInt8(1, offset);
            offset = buf.writeUInt16LE(2, offset);
            offset = buf.writeUInt32LE(3, offset);
            console.log(offset);  // 1 + 2 + 4 = 7
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("7\n", output);
    }

    #endregion

    #region Search Method Tests

    [Fact]
    public void Buffer_IndexOf_ByteValue()
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4, 5, 3, 6]);
            console.log(buf.indexOf(3));     // 2
            console.log(buf.indexOf(3, 3));  // 5
            console.log(buf.indexOf(99));    // -1
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n5\n-1\n", output);
    }

    [Fact]
    public void Buffer_IndexOf_Parity()
    {
        var source = """
            const buf = Buffer.from([65, 66, 67, 68, 69]);
            console.log(buf.indexOf(67));
            console.log(buf.indexOf(70));
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void Buffer_Includes_Basic()
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4, 5]);
            console.log(buf.includes(3));    // true
            console.log(buf.includes(99));   // false
            console.log(buf.includes(1, 1)); // false (starts after 1)
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Buffer_Includes_Parity()
    {
        var source = """
            const buf = Buffer.from([10, 20, 30]);
            console.log(buf.includes(20));
            console.log(buf.includes(40));
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    #endregion

    #region Swap Method Tests

    [Fact]
    public void Buffer_Swap16_Basic()
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4]);
            buf.swap16();
            console.log(buf.readUInt8(0));  // 2
            console.log(buf.readUInt8(1));  // 1
            console.log(buf.readUInt8(2));  // 4
            console.log(buf.readUInt8(3));  // 3
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n1\n4\n3\n", output);
    }

    [Fact]
    public void Buffer_Swap32_Basic()
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4, 5, 6, 7, 8]);
            buf.swap32();
            console.log(buf.readUInt8(0));  // 4
            console.log(buf.readUInt8(1));  // 3
            console.log(buf.readUInt8(2));  // 2
            console.log(buf.readUInt8(3));  // 1
            console.log(buf.readUInt8(4));  // 8
            console.log(buf.readUInt8(5));  // 7
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("4\n3\n2\n1\n8\n7\n", output);
    }

    [Fact]
    public void Buffer_Swap64_Basic()
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4, 5, 6, 7, 8]);
            buf.swap64();
            console.log(buf.readUInt8(0));  // 8
            console.log(buf.readUInt8(1));  // 7
            console.log(buf.readUInt8(6));  // 2
            console.log(buf.readUInt8(7));  // 1
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("8\n7\n2\n1\n", output);
    }

    [Fact]
    public void Buffer_Swap_Chaining()
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4]).swap16().swap16();
            console.log(buf.readUInt8(0));  // 1 (back to original)
            console.log(buf.readUInt8(1));  // 2
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void Buffer_Swap_Parity()
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4, 5, 6, 7, 8]);
            buf.swap16();
            console.log(buf.readUInt8(0));
            console.log(buf.readUInt8(1));
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    #endregion

    #region Endianness Tests

    [Fact]
    public void Buffer_Endianness_Conversion()
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt32LE(305419896, 0);
            console.log(buf.readUInt32BE(0).toString(16));  // 78563412
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("78563412\n", output);
    }

    [Fact]
    public void Buffer_Mixed_Endianness_Parity()
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeUInt32LE(305419896, 0);
            buf.writeUInt32BE(287454020, 4);
            console.log(buf.readUInt32LE(0).toString(16));
            console.log(buf.readUInt32BE(0).toString(16));
            console.log(buf.readUInt32LE(4).toString(16));
            console.log(buf.readUInt32BE(4).toString(16));
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    #endregion

    #region BigInt Tests

    [Fact]
    public void Buffer_ReadBigInt64LE_Basic()
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeUInt8(1, 0);
            buf.writeUInt8(0, 1);
            buf.writeUInt8(0, 2);
            buf.writeUInt8(0, 3);
            buf.writeUInt8(0, 4);
            buf.writeUInt8(0, 5);
            buf.writeUInt8(0, 6);
            buf.writeUInt8(0, 7);
            console.log(buf.readBigInt64LE(0));  // 1n
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1n\n", output);  // BigInt outputs with 'n' suffix
    }

    [Fact]
    public void Buffer_WriteBigInt64LE_RoundTrip()
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeBigInt64LE(12345n, 0);
            const val = buf.readBigInt64LE(0);
            console.log(val === 12345n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Buffer_BigInt_Parity()
    {
        var source = """
            const buf = Buffer.alloc(16);
            buf.writeBigInt64LE(12345n, 0);
            buf.writeBigInt64BE(67890n, 8);
            console.log(buf.readBigInt64LE(0));
            console.log(buf.readBigInt64BE(8));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("12345n\n67890n\n", output);
    }

    #endregion
}
