using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Coverage for the in-place / copying TypedArray bulk methods — fill, set, copyWithin,
/// reverse, slice, subarray, indexOf, lastIndexOf, includes, join, toString. These exercise the
/// byte[]-level fast paths in <c>SharpTSTypedArray</c> (#928 ②): the methods convert/copy
/// at the raw-buffer level (Buffer.BlockCopy / Span memmove) instead of boxing every
/// element through the <c>object?</c> indexer, and must observe identical JS semantics.
///
/// Run in BOTH modes (#940): the compiled emitter now emits these methods on the pure-IL
/// <c>$TypedArray</c> types and dispatches them through a <c>$BoundTypedArrayMethod</c> wrapper,
/// mirroring the interpreter — so interpreter and compiled output must match.
/// </summary>
public class TypedArrayBulkMethodTests
{
    private static string Run(string source, ExecutionMode mode) => TestHarness.Run(source, mode);

    // ----- fill -----

    [Theory, ModeData]
    public void Fill_WholeArray(ExecutionMode mode)
    {
        Assert.Equal("2.5,2.5,2.5,2.5\n", Run(@"
            let a = new Float64Array(4);
            a.fill(2.5);
            console.log(a.join(','));", mode));
    }

    [Theory, ModeData]
    public void Fill_Range_LeavesOthersZero(ExecutionMode mode)
    {
        Assert.Equal("0,7,7,7,0\n", Run(@"
            let a = new Int32Array(5);
            a.fill(7, 1, 4);
            console.log(a.join(','));", mode));
    }

    [Theory, ModeData]
    public void Fill_Int32_TruncatesTowardZero(ExecutionMode mode)
    {
        Assert.Equal("3,3,3\n", Run(@"
            let a = new Int32Array(3);
            a.fill(3.9);
            console.log(a.join(','));", mode));
    }

    [Theory, ModeData]
    public void Fill_Uint8Clamped_ClampsHigh(ExecutionMode mode)
    {
        Assert.Equal("255,255,255\n", Run(@"
            let a = new Uint8ClampedArray(3);
            a.fill(300);
            console.log(a.join(','));", mode));
    }

    [Theory, ModeData]
    public void Fill_Uint8Clamped_ClampsLow(ExecutionMode mode)
    {
        Assert.Equal("0,0,0\n", Run(@"
            let a = new Uint8ClampedArray(3);
            a.fill(-5);
            console.log(a.join(','));", mode));
    }

    // ----- set -----

    [Theory, ModeData]
    public void Set_SameType_AtOffset(ExecutionMode mode)
    {
        // Same element type → Buffer.BlockCopy fast path.
        Assert.Equal("0,1.5,2.5,0\n", Run(@"
            let a = new Float64Array(2);
            a[0] = 1.5; a[1] = 2.5;
            let b = new Float64Array(4);
            b.set(a, 1);
            console.log(b.join(','));", mode));
    }

    [Theory, ModeData]
    public void Set_CrossType_ConvertsPerElement(ExecutionMode mode)
    {
        // Different element type → value-converting element-wise path (truncates to int8).
        Assert.Equal("1,2\n", Run(@"
            let f = new Float64Array(2);
            f[0] = 1.9; f[1] = 2.1;
            let i = new Int8Array(2);
            i.set(f);
            console.log(i.join(','));", mode));
    }

    [Theory, ModeData]
    public void Set_FromRegularArray(ExecutionMode mode)
    {
        Assert.Equal("10,20,30\n", Run(@"
            let a = new Int32Array(3);
            a.set([10, 20, 30]);
            console.log(a.join(','));", mode));
    }

    [Theory, ModeData]
    public void Set_SameType_NegativeOffset_Throws(ExecutionMode mode)
    {
        // The same-type fast path must NOT silently accept a negative offset — it falls
        // through to the element setter, which raises a RangeError as before.
        Assert.Equal("threw\n", Run(@"
            let src = new Int32Array(2);
            src[0] = 5; src[1] = 6;
            let dst = new Int32Array(4);
            try { dst.set(src, -1); console.log('no-error'); }
            catch (e) { console.log('threw'); }", mode));
    }

    [Theory, ModeData]
    public void Set_SameType_TooLarge_Throws(ExecutionMode mode)
    {
        Assert.Equal("threw\n", Run(@"
            let src = new Int32Array(3);
            let dst = new Int32Array(2);
            try { dst.set(src); console.log('no-error'); }
            catch (e) { console.log('threw'); }", mode));
    }

    // ----- copyWithin -----

    [Theory, ModeData]
    public void CopyWithin_Forward(ExecutionMode mode)
    {
        Assert.Equal("3,4,2,3,4\n", Run(@"
            let a = new Int32Array(5);
            for (let i = 0; i < 5; i++) { a[i] = i; }
            a.copyWithin(0, 3);
            console.log(a.join(','));", mode));
    }

    [Theory, ModeData]
    public void CopyWithin_OverlappingRegions(ExecutionMode mode)
    {
        // src [0..3) copied onto [2..5): overlapping; memmove must read-before-overwrite.
        Assert.Equal("0,1,0,1,2\n", Run(@"
            let a = new Int32Array(5);
            for (let i = 0; i < 5; i++) { a[i] = i; }
            a.copyWithin(2, 0, 3);
            console.log(a.join(','));", mode));
    }

    // ----- reverse -----

    [Theory, ModeData]
    public void Reverse_EvenLength(ExecutionMode mode)
    {
        Assert.Equal("4,3,2,1\n", Run(@"
            let a = new Int32Array(4);
            for (let i = 0; i < 4; i++) { a[i] = i + 1; }
            a.reverse();
            console.log(a.join(','));", mode));
    }

    [Theory, ModeData]
    public void Reverse_OddLength_MiddleFixed(ExecutionMode mode)
    {
        Assert.Equal("3,2,1\n", Run(@"
            let a = new Int32Array(3);
            for (let i = 0; i < 3; i++) { a[i] = i + 1; }
            a.reverse();
            console.log(a.join(','));", mode));
    }

    [Theory, ModeData]
    public void Reverse_Float64_PreservesValues(ExecutionMode mode)
    {
        Assert.Equal("3.5,2.5,1.5\n", Run(@"
            let a = new Float64Array(3);
            a[0] = 1.5; a[1] = 2.5; a[2] = 3.5;
            a.reverse();
            console.log(a.join(','));", mode));
    }

    // ----- slice -----

    [Theory, ModeData]
    public void Slice_Range(ExecutionMode mode)
    {
        Assert.Equal("1,2,3\n", Run(@"
            let a = new Int32Array(5);
            for (let i = 0; i < 5; i++) { a[i] = i; }
            console.log(a.slice(1, 4).join(','));", mode));
    }

    [Theory, ModeData]
    public void Slice_NegativeBegin(ExecutionMode mode)
    {
        Assert.Equal("3,4\n", Run(@"
            let a = new Int32Array(5);
            for (let i = 0; i < 5; i++) { a[i] = i; }
            console.log(a.slice(-2).join(','));", mode));
    }

    [Theory, ModeData]
    public void Slice_IsIndependentCopy(ExecutionMode mode)
    {
        // Mutating the slice must not affect the source (slice copies, subarray would alias).
        Assert.Equal("9,1,2|0,1,2\n", Run(@"
            let a = new Int32Array(3);
            for (let i = 0; i < 3; i++) { a[i] = i; }
            let s = a.slice(0, 3);
            s[0] = 9;
            console.log(s.join(',') + '|' + a.join(','));", mode));
    }

    // ----- subarray -----

    [Theory, ModeData]
    public void Subarray_AliasesBackingBuffer(ExecutionMode mode)
    {
        // subarray returns a VIEW over the same buffer: mutating it is visible in the parent.
        Assert.Equal("0,9,2|9,2\n", Run(@"
            let a = new Int32Array(3);
            for (let i = 0; i < 3; i++) { a[i] = i; }
            let s = a.subarray(1, 3);
            s[0] = 9;
            console.log(a.join(',') + '|' + s.join(','));", mode));
    }

    [Theory, ModeData]
    public void Subarray_NegativeBegin(ExecutionMode mode)
    {
        Assert.Equal("3,4\n", Run(@"
            let a = new Int32Array(5);
            for (let i = 0; i < 5; i++) { a[i] = i; }
            console.log(a.subarray(-2).join(','));", mode));
    }

    // ----- indexOf / lastIndexOf / includes -----

    [Theory, ModeData]
    public void IndexOf_FoundMissingAndFromIndex(ExecutionMode mode)
    {
        // found=1, missing=-1, and fromIndex skips earlier matches.
        Assert.Equal("1,-1,-1\n", Run(@"
            let a = new Int32Array(3);
            a[0] = 5; a[1] = 6; a[2] = 7;
            console.log(a.indexOf(6) + ',' + a.indexOf(42) + ',' + a.indexOf(5, 1));", mode));
    }

    [Theory, ModeData]
    public void LastIndexOf_ScansBackward(ExecutionMode mode)
    {
        Assert.Equal("2,1,-1\n", Run(@"
            let a = new Int32Array(3);
            a[0] = 1; a[1] = 2; a[2] = 1;
            console.log(a.lastIndexOf(1) + ',' + a.lastIndexOf(2) + ',' + a.lastIndexOf(9));", mode));
    }

    [Theory, ModeData]
    public void Includes_TrueAndFalse(ExecutionMode mode)
    {
        Assert.Equal("true,false\n", Run(@"
            let a = new Int32Array(3);
            a[0] = 1; a[1] = 2; a[2] = 1;
            console.log(a.includes(2) + ',' + a.includes(9));", mode));
    }

    [Theory, ModeData]
    public void Includes_NaN_SameValueZero(ExecutionMode mode)
    {
        // includes uses SameValueZero, so NaN matches NaN (unlike indexOf strict equality).
        Assert.Equal("true\n", Run(@"
            let a = new Float64Array(2);
            a.fill(NaN);
            console.log(a.includes(NaN));", mode));
    }

    // ----- join / toString -----

    [Theory, ModeData]
    public void Join_CustomAndDefaultSeparator(ExecutionMode mode)
    {
        Assert.Equal("1-2-3 1,2,3\n", Run(@"
            let a = new Uint8Array(3);
            a[0] = 1; a[1] = 2; a[2] = 3;
            console.log(a.join('-') + ' ' + a.join());", mode));
    }

    [Theory, ModeData]
    public void ToString_CommaSeparated(ExecutionMode mode)
    {
        Assert.Equal("1,2,3\n", Run(@"
            let a = new Uint8Array(3);
            a[0] = 1; a[1] = 2; a[2] = 3;
            console.log(a.toString());", mode));
    }
}
