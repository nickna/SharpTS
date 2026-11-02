using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayMethodTypeofTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const a:any=new Uint16Array(3);console.log(a.length,a.byteLength,a.byteOffset,a.BYTES_PER_ELEMENT,typeof a.slice);", "3 6 0 2 function\n"];
        yield return ["method-values", "const a:any=new Uint8Array([1,2]);const f=a.slice;console.log(typeof f,typeof a.subarray,typeof a.set,typeof a.fill);", "function function function function\n"];
        yield return ["method-array", "const a:any=new Int16Array([1,2]);const methods:any[]=[a.slice,a.subarray,a.set,a.fill];console.log(methods.map(fn=>typeof fn).join(\" \"));", "function function function function\n"];
        yield return ["conditional-method", "const a:any=new Uint8Array([1,2]);const flag=true;const f:any=flag?a.slice:a.subarray;console.log(typeof f);", "function\n"];
        yield return ["returned-method", "function pick(value:any){return value.slice}console.log(typeof pick(new Uint8Array([1])));", "function\n"];
        yield return ["alias-metadata", "const a=new Uint16Array([1,2]);const b:any=a;console.log(typeof b.slice,b.length,b.byteLength,b.byteOffset,b.BYTES_PER_ELEMENT);", "function 2 4 0 2\n"];
        yield return ["all-element-kinds", "const values:any[]=[new Int8Array(1),new Uint8Array(1),new Uint8ClampedArray(1),new Int16Array(1),new Uint16Array(1),new Int32Array(1),new Uint32Array(1),new Float32Array(1),new Float64Array(1),new BigInt64Array(1),new BigUint64Array(1)];for(const a of values)console.log(typeof a.slice,typeof a.subarray,typeof a.set,typeof a.fill);", "function function function function\nfunction function function function\nfunction function function function\nfunction function function function\nfunction function function function\nfunction function function function\nfunction function function function\nfunction function function function\nfunction function function function\nfunction function function function\nfunction function function function\n"];
        yield return ["ordinary-object-negative", "const a:any={Invoke(){return 1},slice:4};console.log(typeof a,typeof a.slice,typeof a.Invoke);", "object number function\n"];
        yield return ["existing-callable-shapes", "const a:any=[1];const m:any=new Map();const s:any=new Set();function f(){}console.log(typeof a.slice,typeof m.get,typeof s.has,typeof f,typeof f.bind(null));", "function function function function function\n"];
        yield return ["primitive-controls", "const a:any=new Uint8Array(2);console.log(typeof a,typeof a.length,typeof null,typeof undefined,typeof 1,typeof \"x\");", "object number object undefined number string\n"];
        yield return ["getter-once", "let reads=0;const a:any=new Uint8Array([1]);const obj:any={get method(){reads++;return a.slice}};console.log(typeof obj.method,reads);", "function 1\n"];
        yield return ["direct-call-still-works", "const a:any=new Uint8Array([3,4]);console.log(typeof a.slice,a.slice(0)[0],typeof a.subarray,a.subarray(1)[0]);", "function 3 function 4\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void SupportedTypedArrayMethodsAreFunctions(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
