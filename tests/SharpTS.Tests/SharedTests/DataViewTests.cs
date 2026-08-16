using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for DataView API: constructor, properties, getter/setter methods, and endianness.
/// </summary>
public class DataViewTests
{
    #region Constructor Tests

    [Theory, ModeData]
    public void DataView_Constructor_CreatesViewOverArrayBuffer(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab);
            console.log(dv.byteLength);
            console.log(dv.byteOffset);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("16\n0\n", output);
    }

    [Theory, ModeData]
    public void DataView_Constructor_WithByteOffset(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab, 4);
            console.log(dv.byteLength);
            console.log(dv.byteOffset);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("12\n4\n", output);
    }

    [Theory, ModeData]
    public void DataView_Constructor_WithByteOffsetAndLength(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab, 4, 8);
            console.log(dv.byteLength);
            console.log(dv.byteOffset);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n4\n", output);
    }

    [Theory, ModeData]
    public void DataView_Constructor_OverSharedArrayBuffer(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let dv = new DataView(sab);
            console.log(dv.byteLength);
            console.log(dv.byteOffset);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("16\n0\n", output);
    }

    [Theory, ModeData]
    public void DataView_Constructor_InvalidOffset_ThrowsRangeError(ExecutionMode mode)
    {
        var source = @"
            try {
                let ab = new ArrayBuffer(8);
                let dv = new DataView(ab, 16);
                console.log('no error');
            } catch (e) {
                console.log('error');
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("error\n", output);
    }

    [Theory, ModeData]
    public void DataView_Constructor_InvalidLength_ThrowsRangeError(ExecutionMode mode)
    {
        var source = @"
            try {
                let ab = new ArrayBuffer(8);
                let dv = new DataView(ab, 4, 8);
                console.log('no error');
            } catch (e) {
                console.log('error');
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("error\n", output);
    }

    #endregion

    #region Buffer Property Tests

    [Theory, ModeData]
    public void DataView_Buffer_ReturnsUnderlyingArrayBuffer(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab);
            console.log(dv.buffer === ab);
            console.log(dv.buffer.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n16\n", output);
    }

    #endregion

    #region Int8/Uint8 Tests

    [Theory, ModeData]
    public void DataView_SetGetInt8(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(4);
            let dv = new DataView(ab);
            dv.setInt8(0, 127);
            dv.setInt8(1, -128);
            console.log(dv.getInt8(0));
            console.log(dv.getInt8(1));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("127\n-128\n", output);
    }

    [Theory, ModeData]
    public void DataView_SetGetUint8(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(4);
            let dv = new DataView(ab);
            dv.setUint8(0, 255);
            dv.setUint8(1, 0);
            console.log(dv.getUint8(0));
            console.log(dv.getUint8(1));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("255\n0\n", output);
    }

    #endregion

    #region Int16/Uint16 Tests

    [Theory, ModeData]
    public void DataView_SetGetInt16_BigEndian(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(4);
            let dv = new DataView(ab);
            dv.setInt16(0, 0x1234, false);
            console.log(dv.getInt16(0, false));
            console.log(dv.getUint8(0));
            console.log(dv.getUint8(1));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4660\n18\n52\n", output); // 0x1234 = 4660, big-endian: [0x12, 0x34]
    }

    [Theory, ModeData]
    public void DataView_SetGetInt16_LittleEndian(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(4);
            let dv = new DataView(ab);
            dv.setInt16(0, 0x1234, true);
            console.log(dv.getInt16(0, true));
            console.log(dv.getUint8(0));
            console.log(dv.getUint8(1));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4660\n52\n18\n", output); // 0x1234 = 4660, little-endian: [0x34, 0x12]
    }

    [Theory, ModeData]
    public void DataView_SetGetUint16(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(4);
            let dv = new DataView(ab);
            dv.setUint16(0, 65535, false);
            console.log(dv.getUint16(0, false));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("65535\n", output);
    }

    #endregion

    #region Int32/Uint32 Tests

    [Theory, ModeData]
    public void DataView_SetGetInt32_BigEndian(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(8);
            let dv = new DataView(ab);
            dv.setInt32(0, 0x12345678, false);
            console.log(dv.getInt32(0, false));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("305419896\n", output); // 0x12345678 = 305419896
    }

    [Theory, ModeData]
    public void DataView_SetGetInt32_LittleEndian(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(8);
            let dv = new DataView(ab);
            dv.setInt32(0, 0x12345678, true);
            console.log(dv.getInt32(0, true));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("305419896\n", output);
    }

    [Theory, ModeData]
    public void DataView_SetGetUint32(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(8);
            let dv = new DataView(ab);
            dv.setUint32(0, 4294967295, false);
            console.log(dv.getUint32(0, false));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4294967295\n", output);
    }

    [Theory, ModeData]
    public void DataView_NegativeInt32(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(8);
            let dv = new DataView(ab);
            dv.setInt32(0, -1, false);
            console.log(dv.getInt32(0, false));
            console.log(dv.getUint32(0, false));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n4294967295\n", output);
    }

    #endregion

    #region Float32/Float64 Tests

    [Theory, ModeData]
    public void DataView_SetGetFloat32(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(8);
            let dv = new DataView(ab);
            dv.setFloat32(0, 3.14, false);
            let val = dv.getFloat32(0, false);
            console.log(val > 3.13 && val < 3.15);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void DataView_SetGetFloat64(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab);
            dv.setFloat64(0, 3.141592653589793, false);
            let val = dv.getFloat64(0, false);
            console.log(Math.abs(val - 3.141592653589793) < 0.0000000000001);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void DataView_Float64_Endianness(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab);
            dv.setFloat64(0, 1.234, true);
            dv.setFloat64(8, 1.234, false);
            // Reading with same endianness should give same result
            console.log(dv.getFloat64(0, true) === dv.getFloat64(8, false));
            // Reading with different endianness should give different result
            console.log(dv.getFloat64(0, false) !== dv.getFloat64(0, true));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region BigInt64/BigUint64 Tests

    [Theory, ModeData]
    public void DataView_SetGetBigInt64(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab);
            dv.setBigInt64(0, 9007199254740993n, false);
            let val = dv.getBigInt64(0, false);
            console.log(val === 9007199254740993n);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void DataView_SetGetBigUint64(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab);
            dv.setBigUint64(0, 18446744073709551615n, false);
            let val = dv.getBigUint64(0, false);
            console.log(val === 18446744073709551615n);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void DataView_NegativeBigInt64(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab);
            dv.setBigInt64(0, -1n, false);
            let val = dv.getBigInt64(0, false);
            console.log(val === -1n);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Out of Bounds Tests

    [Theory, ModeData]
    public void DataView_GetInt8_OutOfBounds_ThrowsRangeError(ExecutionMode mode)
    {
        var source = @"
            try {
                let ab = new ArrayBuffer(4);
                let dv = new DataView(ab);
                dv.getInt8(4);
                console.log('no error');
            } catch (e) {
                console.log('error');
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("error\n", output);
    }

    [Theory, ModeData]
    public void DataView_GetInt32_OutOfBounds_ThrowsRangeError(ExecutionMode mode)
    {
        var source = @"
            try {
                let ab = new ArrayBuffer(4);
                let dv = new DataView(ab);
                dv.getInt32(1);
                console.log('no error');
            } catch (e) {
                console.log('error');
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("error\n", output);
    }

    [Theory, ModeData]
    public void DataView_SetFloat64_OutOfBounds_ThrowsRangeError(ExecutionMode mode)
    {
        var source = @"
            try {
                let ab = new ArrayBuffer(8);
                let dv = new DataView(ab);
                dv.setFloat64(1, 3.14);
                console.log('no error');
            } catch (e) {
                console.log('error');
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("error\n", output);
    }

    #endregion

    #region ArrayBuffer.isView Tests

    [Theory, ModeData]
    public void ArrayBuffer_IsView_ReturnsTrueForDataView(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let dv = new DataView(ab);
            console.log(ArrayBuffer.isView(dv));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Shared Memory Tests

    [Theory, ModeData]
    public void DataView_SharesMemoryWithTypedArray(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(4);
            let dv = new DataView(ab);
            let u8 = new Uint8Array(ab);

            dv.setInt32(0, 0x12345678, false);
            console.log(u8[0]);
            console.log(u8[1]);
            console.log(u8[2]);
            console.log(u8[3]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("18\n52\n86\n120\n", output); // 0x12, 0x34, 0x56, 0x78
    }

    [Theory, ModeData]
    public void DataView_WithOffset_SharesMemory(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(8);
            let dv1 = new DataView(ab, 0, 4);
            let dv2 = new DataView(ab, 4, 4);

            dv1.setInt32(0, 100, false);
            dv2.setInt32(0, 200, false);

            console.log(dv1.getInt32(0, false));
            console.log(dv2.getInt32(0, false));

            // Full view should see both
            let fullDv = new DataView(ab);
            console.log(fullDv.getInt32(0, false));
            console.log(fullDv.getInt32(4, false));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n100\n200\n", output);
    }

    #endregion

    #region Default Endianness Tests

    [Theory, ModeData]
    public void DataView_DefaultEndianness_IsBigEndian(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(4);
            let dv = new DataView(ab);
            dv.setInt16(0, 0x0102);  // No endianness = big-endian
            console.log(dv.getUint8(0));  // Should be 0x01
            console.log(dv.getUint8(1));  // Should be 0x02
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    #endregion
}
