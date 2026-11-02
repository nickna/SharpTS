using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayObjectKeyCompoundInterpreterTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "let calls=0;const p=new Uint8Array([1,9]),key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return ++calls===1?'0':'1'}};const result=p[key]+=7;console.log(p[0],p[1],result,calls);", "string\nstring\n1 8 8 2\n"];
        yield return ["primitive-numeric-control", "const p=new Uint8Array([1,9]);console.log(p[0]+=7,p[0],p[1]);", "8 8 9\n"];
        yield return ["object-number-result", "const p=new Uint8Array([1,9]);let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return calls++===0?0:1}};const result=p[key]+=7;console.log(p[0],p[1],result,calls);", "string\nstring\n1 8 8 2\n"];
        yield return ["fallback-primitive-order", "const p=new Uint8Array([1,9]);let calls=0;const log:string[]=[];const key:any={toString(){log.push('toString');return {}},valueOf(){log.push('valueOf');return calls++===0?0:1}};const result=p[key]+=7;console.log(p[0],p[1],result,log.join(','));", "1 8 8 toString,valueOf,toString,valueOf\n"];
        yield return ["operands-once", "const p=new Uint8Array([1,9]);let targets=0,keys=0,rights=0,calls=0;const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return calls++===0?'0':'1'}};function target(){targets++;log.push('target');return p}function index(){keys++;log.push('index');return key}function rhs(){rights++;log.push('rhs');return 7}const result=target()[index()]+=rhs();console.log(p[0],p[1],result,targets,keys,rights,log.join(','));", "1 8 8 1 1 1 target,index,string,rhs,string\n"];
        yield return ["read-key-abrupt", "const p=new Uint8Array([1,9]),marker={},log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);throw marker}};function rhs(){log.push('rhs');return 7}try{p[key]+=rhs()}catch(e){console.log(e===marker,p[0],p[1],log.join(','))}", "true 1 9 string\n"];
        yield return ["rhs-abrupt", "const p=new Uint8Array([1,9]),marker={},log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return '0'}};function rhs():number{log.push('rhs');throw marker}try{p[key]+=rhs()}catch(e){console.log(e===marker,p[0],p[1],log.join(','))}", "true 1 9 string,rhs\n"];
        yield return ["operation-abrupt", "const p=new Uint8Array([1,9]),marker={},log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return '0'}};const value:any={[Symbol.toPrimitive](hint:string){log.push(hint);throw marker}};try{p[key]+=value}catch(e){console.log(e===marker,p[0],p[1],log.join(','))}", "true 1 9 string,default\n"];
        yield return ["put-key-abrupt", "const p=new Uint8Array([1,9]),marker={},log:string[]=[];let calls=0;const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);if(++calls===2)throw marker;return '0'}};function rhs(){log.push('rhs');return 7}try{p[key]+=rhs()}catch(e){console.log(e===marker,p[0],p[1],log.join(','))}", "true 1 9 string,rhs,string\n"];
        yield return ["put-key-nonprimitive", "const p=new Uint8Array([1,9]),log:string[]=[];let calls=0;const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return ++calls===1?'0':{}}};try{p[key]+=7}catch(e){console.log(e instanceof TypeError,p[0],p[1],log.join(','))}", "true 1 9 string,string\n"];
        yield return ["invalid-read-then-valid-put", "const p=new Uint8Array([1,9]);let calls=0;const key:any={[Symbol.toPrimitive](){return calls++===0?'2':'0'}};const result=p[key]+=7;console.log(Number.isNaN(result),p[0],p[1],calls);", "true 0 9 2\n"];
        yield return ["valid-read-invalid-put", "const p=new Uint8Array([1,9]);let calls=0;const key:any={[Symbol.toPrimitive](){return calls++===0?'0':'2'}};const result=p[key]+=7;console.log(result,p[0],p[1],calls);", "8 1 9 2\n"];
        yield return ["negative-zero-read", "const p=new Uint8Array([1,9]);let calls=0;const key:any={[Symbol.toPrimitive](){return calls++===0?'-0':'1'}};const result=p[key]+=7;console.log(Number.isNaN(result),p[0],p[1],calls);", "true 1 0 2\n"];
        yield return ["fractional-read", "const p=new Uint8Array([1,9]);let calls=0;const key:any={[Symbol.toPrimitive](){return calls++===0?'0.5':'1'}};const result=p[key]+=7;console.log(Number.isNaN(result),p[0],p[1],calls);", "true 1 0 2\n"];
        yield return ["bigint-property-key", "const p=new Uint8Array([1,9]);let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return calls++===0?0n:1n}};const result=p[key]+=7;console.log(p[0],p[1],result,calls);", "string\nstring\n1 8 8 2\n"];
        yield return ["bigint-elements", "const p=new BigInt64Array([1n,9n]);let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return calls++===0?'0':'1'}};const result=p[key]+=7n;console.log(String(p[0]),String(p[1]),String(result),typeof result,calls);", "string\nstring\n1 8 8 bigint 2\n"];
        yield return ["clamped-elements", "const p=new Uint8ClampedArray([250,9]);let calls=0;const key:any={[Symbol.toPrimitive](){return calls++===0?'0':'1'}};const result=p[key]+=10;console.log(p[0],p[1],result,calls);", "250 255 260 2\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void RetainsRawNumericKeyThroughIndependentReadAndWrite(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
