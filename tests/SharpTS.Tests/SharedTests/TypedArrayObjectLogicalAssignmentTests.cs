using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayObjectLogicalAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "let calls=0; const v:any=new Uint8Array([0,9]); const k:any={ [Symbol.toPrimitive](hint:any){ console.log(hint); return ++calls===1 ? \"0\" : \"1\"; } }; const r=(v[k] ||= 7); console.log(v[0],v[1],r,calls);\n", "string\nstring\n0 7 7 2\n"];
        yield return ["and-changes-key", "let calls=0;const a:any=new Uint8Array([1,9]);const k:any={[Symbol.toPrimitive](hint:any){return ++calls===1?\"0\":\"1\"}};const r=a[k]&&=4;console.log(a[0],a[1],r,calls);\n", "1 4 4 2\n"];
        yield return ["nullish-short-circuit", "let calls=0,rhs=0;const a:any=new Uint8Array([0]);const k:any={toString(){calls++;return \"0\"}};console.log(a[k]??=++rhs,a[0],calls,rhs);\n", "0 0 1 0\n"];
        yield return ["or-short-circuit", "let calls=0,rhs=0;const a:any=new Uint8Array([3]);const k:any={toString(){calls++;return \"0\"}};console.log(a[k]||=++rhs,a[0],calls,rhs);\n", "3 3 1 0\n"];
        yield return ["target-key-rhs-order", "let log=\"\";const a:any=new Uint8Array([0]);const k:any={toString(){log+=\"k\";return \"0\"}};function target(){log+=\"t\";return a}function key(){log+=\"x\";return k}function rhs(){log+=\"r\";return 260}const r=target()[key()]||=rhs();console.log(r,a[0],log);\n", "260 4 txkrk\n"];
        yield return ["rhs-abrupt", "let calls=0,rhs=0;const marker={};const a:any=new Uint8Array([0]);const k:any={toString(){calls++;return \"0\"}};function fail(){rhs++;throw marker}try{a[k]||=fail()}catch(e){console.log(e===marker,a[0],calls,rhs)}\n", "true 0 1 1\n"];
        yield return ["get-key-abrupt", "let calls=0,rhs=0;const marker={};const a:any=new Uint8Array([0]);const k:any={toString(){calls++;throw marker}};try{a[k]||=++rhs}catch(e){console.log(e===marker,a[0],calls,rhs)}\n", "true 0 1 0\n"];
        yield return ["put-key-abrupt", "let calls=0,rhs=0;const marker={};const a:any=new Uint8Array([0]);const k:any={toString(){if(++calls===2)throw marker;return \"0\"}};try{a[k]||=++rhs}catch(e){console.log(e===marker,a[0],calls,rhs)}\n", "true 0 2 1\n"];
        yield return ["fractional-key", "let calls=0;const a:any=new Uint8Array([2,4]);const k:any={toString(){calls++;return \"1.5\"}};const r=a[k]??=6;console.log(r,a[0],a[1],a[\"1.5\"],calls);\n", "6 2 4 undefined 2\n"];
        yield return ["clamped-conversion", "let calls=0;const a:any=new Uint8ClampedArray([0]);const k:any={toString(){calls++;return \"0\"}};const r=a[k]||=300;console.log(r,a[0],calls);\n", "300 255 2\n"];
        yield return ["bigint-conversion", "let calls=0;const a:any=new BigInt64Array([0n]);const k:any={toString(){calls++;return \"0\"}};const r=a[k]||=3n;console.log(r===3n,a[0]===3n,calls);\n", "true true 2\n"];
        yield return ["boxed-number-key", "const a:any=new Uint8Array([0]);const k:any=new Number(0);console.log(a[k]||=7,a[0]);\n", "7 7\n"];
        yield return ["symbol-key", "let calls=0;const s=Symbol(\"s\");const a:any=new Uint8Array([0]);Object.defineProperty(a,s,{value:0,writable:true,enumerable:true,configurable:true});const k:any={[Symbol.toPrimitive](){calls++;return s}};console.log(a[k]||=5,a[s],calls);\n", "5 5 2\n"];
        yield return ["async-rhs", "async function run(){let calls=0;const a:any=new Uint8Array([0,0]);const k:any={toString(){return ++calls===1?\"0\":\"1\"}};async function rhs(){await Promise.resolve(0);return 9}const r=a[k]||=await rhs();console.log(r,a[0],a[1],calls)}run();\n", "9 0 9 2\n"];
        yield return ["ordinary-array-control", "let calls=0;const a:any=[0,0];const k:any={toString(){return ++calls===1?\"0\":\"1\"}};console.log(a[k]||=8,a[0],a[1],calls);\n", "8 0 8 2\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ObjectKeysKeepLogicalReferenceOrder(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
