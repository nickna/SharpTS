using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for ArrayBuffer API: constructor, byteLength, slice, isView, and TypedArray integration.
/// </summary>
public class ArrayBufferTests
{
    #region Constructor Tests

    [Theory, ModeData]
    public void ArrayBuffer_Constructor_CreatesBufferWithSize(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            console.log(ab.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("16\n", output);
    }

    [Theory, ModeData]
    public void ArrayBuffer_Constructor_ZeroLength(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(0);
            console.log(ab.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void ArrayBuffer_Constructor_NegativeLength_ThrowsRangeError(ExecutionMode mode)
    {
        var source = @"
            try {
                let ab = new ArrayBuffer(-1);
                console.log('no error');
            } catch (e) {
                console.log('error');
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("error\n", output);
    }

    #endregion

    [Theory, ModeData]
    public void ArrayBuffer_UsesItsIntrinsicPrototype(ExecutionMode mode)
    {
        const string source = """
            const value = new ArrayBuffer(4);
            const prototype = ArrayBuffer.prototype;
            console.log(Object.getPrototypeOf(value) === prototype, Reflect.getPrototypeOf(value) === prototype);
            console.log(prototype.constructor === ArrayBuffer, Object.getPrototypeOf(prototype) === Object.prototype);
            console.log(Object.prototype.isPrototypeOf.call(prototype, value));
            console.log(Object.getPrototypeOf(value.slice(1)) === prototype);
            """;
        Assert.Equal("true true\ntrue true\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    #region ByteLength Property Tests

    [Theory, ModeData]
    public void ArrayBuffer_PrototypeExposesTheIntrinsicDescriptors(ExecutionMode mode)
    {
        const string source = """
            const value = new ArrayBuffer(4);
            const prototype = ArrayBuffer.prototype;
            console.log(Object.hasOwn(prototype, 'byteLength'), Object.hasOwn(prototype, 'slice'));
            console.log(value.constructor === ArrayBuffer, value.slice === prototype.slice);
            const length = Object.getOwnPropertyDescriptor(prototype, 'byteLength');
            console.log(length.get.name, length.enumerable, length.configurable,
                length.get.call(value), Reflect.get(prototype, 'byteLength', value));
            const slice = Object.getOwnPropertyDescriptor(prototype, 'slice');
            console.log(slice.value.length, slice.writable, slice.enumerable, slice.configurable);
            console.log(slice.value.call(value, 1, 3).byteLength, value.slice(0, null).byteLength,
                value.slice(0, Infinity).byteLength, value.slice().byteLength);
            for (const member of [length.get, slice.value]) {
                try { member.call({}); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            prototype.slice = function () { return this === value; };
            console.log(value.slice(), delete prototype.slice, typeof value.slice);
            prototype.slice = slice.value;
            """;
        Assert.Equal("true true\ntrue true\nget byteLength false true 4 4\n2 true false true\n2 0 4 4\n" +
            "true\ntrue\ntrue true undefined\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ArrayBuffer_ByteLength_ReturnsCorrectValue(ExecutionMode mode)
    {
        var source = @"
            let ab1 = new ArrayBuffer(8);
            let ab2 = new ArrayBuffer(1024);
            console.log(ab1.byteLength);
            console.log(ab2.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n1024\n", output);
    }

    [Theory, ModeData]
    public void ArrayBuffer_ByteLengthOwnDescriptorShadowsIntrinsicGetter(ExecutionMode mode)
    {
        const string source = """
            const value = new ArrayBuffer(4);
            Object.defineProperty(value, 'byteLength', {
                configurable: true,
                get() { return this === value ? 'custom' : 'wrong receiver'; }
            });
            console.log(value.byteLength, value['byteLength'], Reflect.get(value, 'byteLength'));
            console.log(Object.hasOwn(value, 'byteLength'), delete value.byteLength, value.byteLength);
            Object.defineProperty(value, 'byteLength', { configurable: true, value: null });
            console.log(value.byteLength === null);
            Object.defineProperty(value, 'byteLength', { set(value) {} });
            console.log(value.byteLength === undefined);
            """;
        Assert.Equal("custom custom custom\ntrue true 4\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    #endregion

    #region Slice Method Tests

    [Theory, ModeData]
    public void ArrayBuffer_Slice_CreatesNewBuffer(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let sliced = ab.slice(4, 12);
            console.log(sliced.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory, ModeData]
    public void ArrayBuffer_Slice_WithNegativeIndices(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let sliced = ab.slice(-8);
            console.log(sliced.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory, ModeData]
    public void ArrayBuffer_Slice_CopiesDataIndependently(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(4);
            let view = new Uint8Array(ab);
            view[0] = 100;
            view[1] = 200;

            let sliced = ab.slice(0, 2);
            let slicedView = new Uint8Array(sliced);

            // Modify original
            view[0] = 50;

            // Sliced should not be affected (independent copy)
            console.log(slicedView[0]);
            console.log(slicedView[1]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n", output);
    }

    [Theory, ModeData]
    public void ArrayBuffer_Slice_DefaultEnd(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let sliced = ab.slice(8);
            console.log(sliced.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    #endregion

    #region IsView Static Method Tests

    [Theory, ModeData]
    public void ArrayBuffer_IsView_ReturnsTrueForTypedArray(ExecutionMode mode)
    {
        var source = @"
            let arr = new Int32Array(4);
            console.log(ArrayBuffer.isView(arr));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ArrayBuffer_IsView_ReturnsFalseForNonView(ExecutionMode mode)
    {
        var source = @"
            console.log(ArrayBuffer.isView(null));
            console.log(ArrayBuffer.isView(undefined));
            console.log(ArrayBuffer.isView(42));
            console.log(ArrayBuffer.isView('string'));
            console.log(ArrayBuffer.isView([]));
            console.log(ArrayBuffer.isView({}));
            console.log(ArrayBuffer.isView(new ArrayBuffer(8)));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\nfalse\nfalse\nfalse\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void ArrayBuffer_IsView_AllTypedArrays(ExecutionMode mode)
    {
        var source = @"
            console.log(ArrayBuffer.isView(new Int8Array(1)));
            console.log(ArrayBuffer.isView(new Uint8Array(1)));
            console.log(ArrayBuffer.isView(new Uint8ClampedArray(1)));
            console.log(ArrayBuffer.isView(new Int16Array(1)));
            console.log(ArrayBuffer.isView(new Uint16Array(1)));
            console.log(ArrayBuffer.isView(new Int32Array(1)));
            console.log(ArrayBuffer.isView(new Uint32Array(1)));
            console.log(ArrayBuffer.isView(new Float32Array(1)));
            console.log(ArrayBuffer.isView(new Float64Array(1)));
            console.log(ArrayBuffer.isView(new BigInt64Array(1)));
            console.log(ArrayBuffer.isView(new BigUint64Array(1)));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    #endregion

    #region TypedArray over ArrayBuffer Tests

    [Theory, ModeData]
    public void Int32Array_OverArrayBuffer_SharesMemory(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let view1 = new Int32Array(ab);
            let view2 = new Int32Array(ab);
            view1[0] = 42;
            console.log(view2[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void TypedArray_OverArrayBuffer_WithByteOffset(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let view = new Int32Array(ab, 4, 2);
            console.log(view.byteOffset);
            console.log(view.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n2\n", output);
    }

    [Theory, ModeData]
    public void Uint8Array_OverArrayBuffer_WorksCorrectly(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(4);
            let view = new Uint8Array(ab);
            view[0] = 255;
            view[1] = 128;
            console.log(view[0]);
            console.log(view[1]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("255\n128\n", output);
    }

    [Theory, ModeData]
    public void AllTypedArrays_OverArrayBuffer(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(64);

            // Test each TypedArray type over the ArrayBuffer
            let i8 = new Int8Array(ab, 0, 1);
            let u8 = new Uint8Array(ab, 1, 1);
            let u8c = new Uint8ClampedArray(ab, 2, 1);
            let i16 = new Int16Array(ab, 4, 1);
            let u16 = new Uint16Array(ab, 8, 1);
            let i32 = new Int32Array(ab, 12, 1);
            let u32 = new Uint32Array(ab, 16, 1);
            let f32 = new Float32Array(ab, 20, 1);
            let f64 = new Float64Array(ab, 24, 1);

            i8[0] = -1;
            u8[0] = 255;
            u8c[0] = 300; // should clamp to 255
            i16[0] = -1000;
            u16[0] = 65535;
            i32[0] = -100000;
            u32[0] = 4294967295;
            f32[0] = 3.14;
            f64[0] = 3.141592653589793;

            console.log(i8[0]);
            console.log(u8[0]);
            console.log(u8c[0]);
            console.log(i16[0]);
            console.log(u16[0]);
            console.log(i32[0]);
            console.log(u32[0]);
        ";
        var output = TestHarness.Run(source, mode);
        var lines = output.Split('\n');
        Assert.Equal("-1", lines[0]);
        Assert.Equal("255", lines[1]);
        Assert.Equal("255", lines[2]); // clamped
        Assert.Equal("-1000", lines[3]);
        Assert.Equal("65535", lines[4]);
        Assert.Equal("-100000", lines[5]);
        Assert.Equal("4294967295", lines[6]);
    }

    [Theory, ModeData]
    public void TypedArray_Buffer_ReturnsArrayBuffer(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let view = new Int32Array(ab);
            console.log(view.buffer === ab);
            console.log(view.buffer.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n16\n", output);
    }

    #endregion

    #region TypedArray Array-literal Constructor Tests (#782)

    [Theory, ModeData]
    public void TypedArray_FromArrayLiteral_CopiesElements(ExecutionMode mode)
    {
        var source = @"
            const x = new Uint8Array([1, 2, 3]);
            console.log(x[0], x[1], x[2], x.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 2 3 3\n", output);
    }

    [Theory, ModeData]
    public void TypedArray_Int32_FromArrayLiteral_CopiesElements(ExecutionMode mode)
    {
        var source = @"
            const y = new Int32Array([100, 200, -300]);
            console.log(y[0], y[2], y.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100 -300 3\n", output);
    }

    [Theory, ModeData]
    public void TypedArray_CopyFromTypedArray(ExecutionMode mode)
    {
        var source = @"
            const a = new Uint8Array([10, 20, 30]);
            const b = new Uint8Array(a);
            console.log(b[0], b[1], b[2]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10 20 30\n", output);
    }

    [Theory, CompiledOnlyData]
    public void TypedArray_Spread_ProducesArray(ExecutionMode mode)
    {
        var source = @"
            const arr = new Uint8Array([5, 10, 15]);
            const spread = [...arr];
            console.log(spread[0], spread[1], spread[2]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5 10 15\n", output);
    }

    #endregion

    #region Mixed ArrayBuffer and SharedArrayBuffer Tests

    [Theory, ModeData]
    public void ArrayBuffer_IsNotShared(ExecutionMode mode)
    {
        var source = @"
            let ab = new ArrayBuffer(16);
            let sab = new SharedArrayBuffer(16);

            let viewAb = new Int32Array(ab);
            let viewSab = new Int32Array(sab);

            // ArrayBuffer-backed views are not shared
            console.log(viewAb.buffer === ab);
            console.log(viewSab.buffer === sab);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion
}
