using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

// Exercises the emitted boxed Set implementations; interpreter logical ingress is #3408.
[Collection("CompilationService")]
public sealed class TypedArrayValueCoercionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[]
        {
            "original",
            "const a:any=new Uint8Array([0]);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']||=value;console.log(result===value,a[0],calls);",
            "true 2 1\n"
        };
        yield return new object[]
        {
            "Int8Array-boxed-object-store",
            "const a:any=new Int8Array(1);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']=value;console.log(result===value,a[0],calls);",
            "true 2 1\n"
        };
        yield return new object[]
        {
            "Uint8Array-boxed-object-store",
            "const a:any=new Uint8Array(1);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']=value;console.log(result===value,a[0],calls);",
            "true 2 1\n"
        };
        yield return new object[]
        {
            "Uint8ClampedArray-boxed-object-store",
            "const a:any=new Uint8ClampedArray(1);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']=value;console.log(result===value,a[0],calls);",
            "true 255 1\n"
        };
        yield return new object[]
        {
            "Int16Array-boxed-object-store",
            "const a:any=new Int16Array(1);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']=value;console.log(result===value,a[0],calls);",
            "true 258 1\n"
        };
        yield return new object[]
        {
            "Uint16Array-boxed-object-store",
            "const a:any=new Uint16Array(1);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']=value;console.log(result===value,a[0],calls);",
            "true 258 1\n"
        };
        yield return new object[]
        {
            "Int32Array-boxed-object-store",
            "const a:any=new Int32Array(1);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']=value;console.log(result===value,a[0],calls);",
            "true 258 1\n"
        };
        yield return new object[]
        {
            "Uint32Array-boxed-object-store",
            "const a:any=new Uint32Array(1);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']=value;console.log(result===value,a[0],calls);",
            "true 258 1\n"
        };
        yield return new object[]
        {
            "Float32Array-boxed-object-store",
            "const a:any=new Float32Array(1);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']=value;console.log(result===value,a[0],calls);",
            "true 258 1\n"
        };
        yield return new object[]
        {
            "Float64Array-boxed-object-store",
            "const a:any=new Float64Array(1);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']=value;console.log(result===value,a[0],calls);",
            "true 258 1\n"
        };
        yield return new object[]
        {
            "symbol-to-primitive-preferred",
            "const a:any=new Uint8Array(1);let log='';const value:any={[Symbol.toPrimitive](hint:string){log+=hint;return 258},valueOf(){log+='wrong';return 9}};a['0']=value;console.log(a[0],log);",
            "2 number\n"
        };
        yield return new object[]
        {
            "inherited-valueof-receiver",
            "const a:any=new Uint8Array(1);let calls=0;const p:any={valueOf(){calls++;return this.x}};const value:any=Object.create(p);value.x=258;a['0']=value;console.log(a[0],calls);",
            "2 1\n"
        };
        yield return new object[]
        {
            "valueof-then-tostring",
            "const a:any=new Uint8Array(1);let log='';const value:any={valueOf(){log+='v';return {}},toString(){log+='s';return '258'}};a['0']=value;console.log(a[0],log);",
            "2 vs\n"
        };
        yield return new object[]
        {
            "abrupt-hook-does-not-store",
            "const a:any=new Uint8Array([7]);let calls=0;const value:any={valueOf(){calls++;throw 'stop'}};try{a['0']=value}catch(e){console.log(e==='stop',a[0],calls)}",
            "true 7 1\n"
        };
        yield return new object[]
        {
            "primitive-number-coercions",
            "const a:any=new Float64Array(1);a['0']=undefined;console.log(Number.isNaN(a[0]));a['0']=null;console.log(a[0]);a['0']=true;console.log(a[0]);a['0']='0x10';console.log(a[0]);",
            "true\n0\n1\n16\n"
        };
        yield return new object[]
        {
            "symbol-rejects-as-guest-typeerror",
            "const a:any=new Uint8Array([7]);try{a['0']=Symbol('value')}catch(e){console.log(e instanceof TypeError,a[0])}",
            "true 7\n"
        };
        yield return new object[]
        {
            "bigint-rejects-number-array",
            "const a:any=new Uint8Array([7]);try{a['0']=1n}catch(e){console.log(e instanceof TypeError,a[0])}",
            "true 7\n"
        };
        yield return new object[]
        {
            "bigint-setter-control",
            "const a:any=new BigInt64Array(1);a['0']=258n;console.log(a[0]===258n);",
            "true\n"
        };
        yield return new object[]
        {
            "unboxed-numeric-fastpath-control",
            "const a=new Float64Array(1);a[0]=7;console.log(a[0]);",
            "7\n"
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EmittedNumericSetUsesGuestValueConversion(string name, string source, string expected)
    {
        _ = name;
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
