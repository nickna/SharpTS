using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for worker_threads-related APIs: SharedArrayBuffer, Atomics,
/// MessageChannel, and structuredClone.
/// </summary>
public class WorkerThreadsTests
{
    #region SharedArrayBuffer Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SharedArrayBuffer_Constructor_CreatesBufferWithSize(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            console.log(sab.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("16\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SharedArrayBuffer_Slice_CreatesNewBuffer(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let sliced = sab.slice(4, 12);
            console.log(sliced.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    #endregion

    #region TypedArray over SharedArrayBuffer Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Int32Array_OverSharedArrayBuffer_SharesMemory(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view1 = new Int32Array(sab);
            let view2 = new Int32Array(sab);
            view1[0] = 42;
            console.log(view2[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypedArray_WithByteOffset_CreatesCorrectView(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab, 4, 2);
            console.log(view.byteOffset);
            console.log(view.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Uint8Array_OverSharedArrayBuffer_WorksCorrectly(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(4);
            let view = new Uint8Array(sab);
            view[0] = 255;
            view[1] = 128;
            console.log(view[0]);
            console.log(view[1]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("255\n128\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void TypedArray_FromLength_CreatesArray(ExecutionMode mode)
    {
        var source = @"
            let arr = new Int32Array(4);
            arr[0] = 10;
            arr[1] = 20;
            arr[2] = 30;
            arr[3] = 40;
            console.log(arr[0]);
            console.log(arr[3]);
            console.log(arr.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n40\n4\n", output);
    }

    #endregion

    #region Atomics Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_Load_ReadsValue(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 42;
            console.log(Atomics.load(view, 0));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_Store_WritesValue(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            Atomics.store(view, 0, 100);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_Add_AddsAndReturnsOldValue(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 10;
            let oldValue = Atomics.add(view, 0, 5);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_Sub_SubtractsAndReturnsOldValue(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 10;
            let oldValue = Atomics.sub(view, 0, 3);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_Exchange_SwapsValues(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 42;
            let oldValue = Atomics.exchange(view, 0, 100);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_CompareExchange_Success(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 42;
            let result = Atomics.compareExchange(view, 0, 42, 100);
            console.log(result);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_CompareExchange_Failure(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 42;
            let result = Atomics.compareExchange(view, 0, 99, 100);
            console.log(result);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_And_PerformsBitwiseAnd(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 0b1111;
            let oldValue = Atomics.and(view, 0, 0b0101);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_Or_PerformsBitwiseOr(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 0b1010;
            let oldValue = Atomics.or(view, 0, 0b0101);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_Xor_PerformsBitwiseXor(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 0b1111;
            let oldValue = Atomics.xor(view, 0, 0b0101);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Atomics_IsLockFree_ReturnsBooleanForSize(ExecutionMode mode)
    {
        var source = @"
            console.log(Atomics.isLockFree(4));
            console.log(Atomics.isLockFree(8));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region MessageChannel Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MessageChannel_Constructor_CreatesTwoPorts(ExecutionMode mode)
    {
        var source = @"
            let channel = new MessageChannel();
            console.log(channel.port1 !== null);
            console.log(channel.port2 !== null);
            console.log(channel.port1 !== channel.port2);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region StructuredClone Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StructuredClone_ClonesObject(ExecutionMode mode)
    {
        var source = @"
            let obj = { a: 1, b: 'hello', c: [1, 2, 3] };
            let cloned = structuredClone(obj);
            cloned.a = 999;
            console.log(obj.a);
            console.log(cloned.a);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n999\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StructuredClone_ClonesNestedObjects(ExecutionMode mode)
    {
        var source = @"
            let obj = { nested: { value: 42 } };
            let cloned = structuredClone(obj);
            cloned.nested.value = 100;
            console.log(obj.nested.value);
            console.log(cloned.nested.value);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StructuredClone_ClonesArrays(ExecutionMode mode)
    {
        var source = @"
            let arr = [1, 2, [3, 4]];
            let cloned = structuredClone(arr);
            cloned[0] = 999;
            console.log(arr[0]);
            console.log(cloned[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n999\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StructuredClone_SharesSharedArrayBuffer(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view1 = new Int32Array(sab);
            view1[0] = 42;

            let clonedSab = structuredClone(sab);
            let view2 = new Int32Array(clonedSab);

            // SharedArrayBuffer is shared by reference, not cloned
            console.log(view2[0]);
            view2[0] = 100;
            console.log(view1[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StructuredClone_ClonesMap(ExecutionMode mode)
    {
        var source = @"
            let map = new Map<string, number>([['a', 1], ['b', 2]]);
            let cloned = structuredClone(map);
            cloned.set('a', 999);
            console.log(map.get('a'));
            console.log(cloned.get('a'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n999\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StructuredClone_ClonesSet(ExecutionMode mode)
    {
        var source = @"
            let mySet = new Set([1, 2, 3]);
            let cloned = structuredClone(mySet);
            cloned.add(4);
            console.log(mySet.size);
            console.log(cloned.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n4\n", output);
    }

    #endregion
}
