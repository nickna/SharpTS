using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayBufferViewValidationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["sync-typedarray-offset-host", "/// <reference lib=\"esnext.disposable\" />\ntry{using resource=null;new Int32Array(new ArrayBuffer(8),1)}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["int32-no-using", "try{new Int32Array(new ArrayBuffer(8),1);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["int16-misaligned", "try{new Int16Array(new ArrayBuffer(8),1);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["bigint64-misaligned", "try{new BigInt64Array(new ArrayBuffer(16),4);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["shared-misaligned", "try{new Int32Array(new SharedArrayBuffer(8),1);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["offset-past-buffer", "try{new Int32Array(new ArrayBuffer(8),12,0);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["negative-offset", "try{new Int32Array(new ArrayBuffer(8),-4,1);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["oversized-explicit-length", "try{new Int32Array(new ArrayBuffer(8),4,2);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["omitted-unaligned-byte-length", "try{new Int16Array(new ArrayBuffer(3));console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["shared-omitted-unaligned-byte-length", "try{new Float64Array(new SharedArrayBuffer(12));console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["negative-explicit-length", "try{new Int32Array(new ArrayBuffer(8),0,-1);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["overflowing-explicit-length", "try{new Int32Array(new ArrayBuffer(8),0,1073741824);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["offset-past-buffer-omitted-length", "try{new Int32Array(new ArrayBuffer(8),12);console.log('accepted')}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["valid-view-boundaries", "const b=new ArrayBuffer(9);const end=new Int32Array(b,8,0);const short=new Int32Array(b,4,1);const shared=new Int32Array(new SharedArrayBuffer(8),4,1);short[0]=123;console.log(end.length,short.length,short[0],shared.length);", "0 1 123 1\n"];
    }

    public static IEnumerable<object[]> DualCases()
    {
        foreach (var row in Cases())
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [.. row, mode];
    }

    [Theory, MemberData(nameof(DualCases))]
    public void BufferViewsValidateAlignmentAndBounds(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
