using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayOwnedBufferTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["allocated-buffer-brand-and-identity", "const a=new Uint8Array(2);console.log(a.buffer instanceof ArrayBuffer,a.buffer===a.buffer);", "true true\n"];
        yield return ["reduced-direct-view-read", "const backing=new Uint8Array([1,2,3,4]);const view=new Uint8Array(backing.buffer,1,2);console.log(view[0],view[1],view.length);", "2 3 2\n"];
        yield return ["explicit-array-buffer-companion", "const buffer=new ArrayBuffer(4);const bytes=new Uint8Array(buffer);bytes[0]=1;bytes[1]=2;bytes[2]=3;bytes[3]=4;const view=new Uint8Array(buffer,1,2);console.log(view[0],view[1],view.length);", "2 3 2\n"];
        yield return ["allocated-owner-and-offset-sharing", "const a=new Uint16Array([1,2,3]);const buffer=a.buffer;const view=new Uint16Array(buffer,2,2);view[0]=7;console.log(buffer instanceof ArrayBuffer,buffer===a.buffer,buffer.byteLength,view.byteOffset,view.length,a[1]);", "true true 6 2 2 7\n"];
        yield return ["subarray-preserves-owner", "const a=new Uint8Array([1,2,3,4]),b=a.subarray(1,3);b[0]=9;console.log(b.buffer===a.buffer,b.byteOffset,b.length,a[1]);", "true 1 2 9\n"];
        yield return ["nested-subarray-offset", "const a=new Uint16Array([1,2,3,4]),b=a.subarray(1),c=b.subarray(1,2);console.log(c.buffer===a.buffer,c.byteOffset,c.length,c[0]);", "true 4 1 3\n"];
        yield return ["slice-independent-owner", "const a=new Uint8Array([1,2,3]),b=a.slice(1,3);b[0]=8;console.log(b.buffer instanceof ArrayBuffer,b.buffer!==a.buffer,b[0],a[1]);", "true true 8 2\n"];
        yield return ["copy-constructor-independent-owner", "const a=new Uint8Array([1,2]),b=new Uint8Array(a);b[0]=9;console.log(b.buffer instanceof ArrayBuffer,b.buffer!==a.buffer,b[0],a[0]);", "true true 9 1\n"];
        yield return ["zero-length-owner", "const a=new Uint8Array(0);console.log(a.buffer instanceof ArrayBuffer,a.buffer===a.buffer,a.buffer.byteLength,a.length);", "true true 0 0\n"];
        yield return ["all-eleven-element-kinds", "const arrays:any[]=[new Int8Array(2),new Uint8Array(2),new Uint8ClampedArray(2),new Int16Array(2),new Uint16Array(2),new Int32Array(2),new Uint32Array(2),new Float32Array(2),new Float64Array(2),new BigInt64Array(2),new BigUint64Array(2)];console.log(arrays.every(a=>a.buffer instanceof ArrayBuffer&&a.buffer===a.buffer&&a.buffer.byteLength===a.byteLength));", "true\n"];
        yield return ["passed-array-buffer-identity", "const buffer=new ArrayBuffer(8),a=new Uint16Array(buffer,2,2);console.log(a.buffer===buffer,a.byteOffset,a.length);", "true 2 2\n"];
        yield return ["passed-shared-buffer-identity", "const buffer=new SharedArrayBuffer(8),a=new Uint16Array(buffer,2,2);console.log(a.buffer===buffer,a.byteOffset,a.length);", "true 2 2\n"];
        yield return ["shared-subarray-retains-owner", "const buffer=new SharedArrayBuffer(4),a=new Uint8Array(buffer),b=a.subarray(1,3);b[0]=7;console.log(b.buffer===buffer,b.byteOffset,b.length,a[1]);", "true 1 2 7\n"];
        yield return ["data-view-sees-owned-storage", "const a=new Uint8Array([1,2,3]),view=new DataView(a.buffer);view.setUint8(1,9);console.log(view.buffer===a.buffer,view.byteLength,a[1]);", "true 3 9\n"];
        yield return ["allocations-have-independent-owners", "const a=new Uint8Array(2),b=new Uint8Array(2);a[0]=7;console.log(a.buffer!==b.buffer,a.buffer===a.buffer,a[0],b[0]);", "true true 7 0\n"];
        yield return ["end-offset-zero-length", "const a=new Uint8Array([1,2,3,4]),b=new Uint8Array(a.buffer,4,0);console.log(b.buffer===a.buffer,b.byteOffset,b.length);", "true 4 0\n"];
        yield return ["owned-buffer-alignment-refusal", "const a=new Uint8Array(4);try{new Int16Array(a.buffer,1)}catch(e){console.log(e instanceof RangeError)}console.log(a.length);", "true\n4\n"];
        yield return ["owned-buffer-overflow-refusal", "const a=new Uint8Array(4);try{new Uint8Array(a.buffer,3,2)}catch(e){console.log(e instanceof RangeError)}console.log(a.length);", "true\n4\n"];
        yield return ["arraylike-input-owner", "const source:any={0:1,1:2,length:2},a=new Uint8Array(source),b=new Uint8Array(a.buffer,1,1);b[0]=9;console.log(a.buffer instanceof ArrayBuffer,b.buffer===a.buffer,a[1],a.length);", "true true 9 2\n"];
        yield return ["slice-buffer-constructs-independent-view", "const a=new Uint8Array([1,2,3]),copy=a.slice(),b=new Uint8Array(copy.buffer,1,1);b[0]=8;console.log(b.buffer===copy.buffer,b.buffer!==a.buffer,copy[1],a[1]);", "true true 8 2\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void BufferOwnsTheActualSharedStorage(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Fact]
    public void RuntimeByteArrayViewPreservesItsBufferOwnerAcrossSubarray()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var array = new SharpTSUint8Array(bytes, 1, 2);
        var owner = Assert.IsType<SharpTSArrayBuffer>(array.GetMember("buffer"));
        var view = array.Subarray(1);
        Assert.Same(owner, array.GetMember("buffer"));
        Assert.Same(owner, view.GetMember("buffer"));
        Assert.Equal(4, owner.ByteLength);
        Assert.Equal(2, view.ByteOffset);
        view[0] = 9.0;
        Assert.Equal((byte)9, bytes[2]);
    }
}
