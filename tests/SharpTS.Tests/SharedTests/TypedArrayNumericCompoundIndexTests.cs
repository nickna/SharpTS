using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayNumericCompoundIndexTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["fraction-original", "const a=new Int32Array([9]);console.log(a[0.5]+=2,a[0],a.length);", "NaN 9 1\n"];
        yield return ["function-half-original", "function run(i:number){const a=new Int32Array([10,20,30]);console.log(a[i/2]+=1,a[1],a.length)}run(3);", "NaN 20 3\n"];
        yield return ["parameter-invalid-domains", "function run(index:number){const a=new Int32Array([9]);console.log(a[index]+=2,a[0])}for(const i of [0.5,-0.5,NaN,Infinity,-Infinity,-1,1,4294967296,2147483648])run(i);", "NaN 9\nNaN 9\nNaN 9\nNaN 9\nNaN 9\nNaN 9\nNaN 9\nNaN 9\nNaN 9\n"];
        yield return ["wide-native-counter", "function run(){const a=new Int32Array([9]);for(let i:number=4294967297;i<4294967298;i++)console.log(a[i]+=2,a[0]);}run();", "NaN 9\n"];
        yield return ["all-unboxed-kinds", "{const a=new Int8Array([9]);console.log(a[0.5]+=2,a[0]);}{const a=new Uint8Array([9]);console.log(a[0.5]+=2,a[0]);}{const a=new Int16Array([9]);console.log(a[0.5]+=2,a[0]);}{const a=new Uint16Array([9]);console.log(a[0.5]+=2,a[0]);}{const a=new Int32Array([9]);console.log(a[0.5]+=2,a[0]);}{const a=new Uint32Array([9]);console.log(a[0.5]+=2,a[0]);}{const a=new Float32Array([9]);console.log(a[0.5]+=2,a[0]);}{const a=new Float64Array([9]);console.log(a[0.5]+=2,a[0]);}", "NaN 9\nNaN 9\nNaN 9\nNaN 9\nNaN 9\nNaN 9\nNaN 9\nNaN 9\n"];
        yield return ["invalid-arithmetic-operators", "const a=new Int32Array([9]);console.log(Number.isNaN(a[0.5]+=2),Number.isNaN(a[0.5]-=2),Number.isNaN(a[0.5]*=2),Number.isNaN(a[0.5]/=2),Number.isNaN(a[0.5]%=2),a[0]);", "true true true true true 9\n"];
        yield return ["invalid-bitwise-operators", "const a=new Int32Array([9]);console.log(a[0.5]&=7,a[0.5]|=2,a[0.5]^=3,a[0.5]<<=1,a[0.5]>>=1,a[0]);", "0 2 3 0 0 9\n"];
        yield return ["invalid-bitwise-nonfinite-rhs", "const a=new Float64Array([9]);console.log(a[0.5]|=NaN,a[0.5]^=Infinity,a[0.5]&=4294967297,a[0]);", "0 0 0 9\n"];
        yield return ["negative-zero-valid", "const a=new Int32Array([9]);console.log(a[-0]+=2,a[0]);", "11 11\n"];
        yield return ["last-valid-index", "const a=new Int16Array([4,9]);console.log(a[1]+=2,a[0],a[1]);", "11 4 11\n"];
        yield return ["result-before-element-narrowing", "const a=new Uint8Array([255]);console.log(a[0]+=2,a[0]);", "257 1\n"];
        yield return ["index-value-retained-through-rhs", "function run(index:number){const a=new Int32Array([9]);const value=a[index]+=(index=index===0?0.5:0,2);console.log(value,a[0],index)}run(0.5);run(0);", "NaN 9 0\n11 11 0.5\n"];
        yield return ["receiver-retained-through-rhs", "function run(index:number){let a=new Int32Array([9]);const old=a;const value=a[index]+=(a=new Int32Array([30]),2);console.log(value,old[0],a[0])}run(0.5);run(0);", "NaN 9 30\n11 11 30\n"];
        yield return ["rhs-abrupt-after-missing-read", "let calls=0;function fail():number{calls++;throw \"boom\"}const a=new Int32Array([9]);try{a[-1]+=fail()}catch(e){console.log(e)}console.log(calls,a[0]);", "boom\n1 9\n"];
        yield return ["intrinsic-length-no-getter-read", "const a=new Int32Array([9]);let reads=0;Object.defineProperty(a,\"length\",{get(){reads++;return 10}});console.log(a[1]+=2,a[0],reads);", "NaN 9 0\n"];
        yield return ["subview-physical-length", "const buffer=new ArrayBuffer(12),whole=new Int32Array(buffer);whole[1]=9;whole[2]=30;const a=new Int32Array(buffer,4,1);console.log(a[1]+=2,a[0],whole[2]);", "NaN 9 30\n"];
        yield return ["direct-backing-valid-then-missing", "function run(){const a=new Int32Array([9]);let sum=0;for(let i=0;i<2;i++)sum+=a[i]+=2;console.log(Number.isNaN(sum),a[0])}run();", "true 11\n"];
        yield return ["nonextensible-missing-slot", "const a=new Int32Array([9]);Object.preventExtensions(a);console.log(a[0.5]+=2,a[0],Object.hasOwn(a,\"0.5\"));", "NaN 9 false\n"];
        yield return ["strict-missing-slot", "\"use strict\";const a=new Int32Array([9]);console.log(a[0.5]+=2,a[0]);", "NaN 9\n"];
        yield return ["numeric-rhs-once", "let calls=0;function rhs():number{calls++;return 2}const a=new Int32Array([9]);console.log(a[0.5]+=rhs(),calls,a[0]);", "NaN 1 9\n"];
        yield return ["object-key-fallback-per-phase", "const a=new Int32Array([9]);let calls=0;const key:any={toString(){return ++calls===1?\"0.5\":\"0\"}};console.log(a[key]+=2,a[0],calls);", "NaN 0 2\n"];
        yield return ["bigint-fallback-typeerror", "const a=new BigInt64Array([9n]);try{a[0.5]+=1n}catch(e){console.log(e instanceof TypeError)}console.log(String(a[0]));", "true\n9\n"];
        yield return ["ordinary-array-control", "const a:any=[9];a[0.5]=5;console.log(a[0.5]+=2,a[0],a.length);", "7 9 1\n"];
        yield return ["valid-nonfinite-value", "function run(index:number){const a=new Float64Array([Infinity]);console.log(Number.isNaN(a[index]-=Infinity),Number.isNaN(a[0]))}run(0);", "true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NumericCompoundKeysAreValidatedBeforePhysicalAccess(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
