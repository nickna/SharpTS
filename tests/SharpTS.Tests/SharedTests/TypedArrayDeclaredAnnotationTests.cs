using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayDeclaredAnnotationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct-signed-annotation", "const a:Int8Array=new Int8Array(0);console.log(a.length);", "0\n"];
        yield return ["direct-unsigned-annotation", "const a:Uint8Array=new Uint8Array([7]);console.log(a[0]);", "7\n"];
        yield return ["explicit-union-original", "function read(a:Int8Array|Uint8Array):number{return a[1]}console.log(read(new Int8Array(1))===undefined,read(new Uint8Array(1))===undefined);", "true true\n"];
        yield return ["explicit-buffer-argument", "const a:Uint8Array<ArrayBuffer>=new Uint8Array(1);console.log(a.length);", "1\n"];
        yield return ["number-parameter-missing", "function read(a:Int8Array):number{return a[1]}console.log(read(new Int8Array(1))===undefined,typeof read(new Int8Array(1)));", "true undefined\n"];
        yield return ["number-argument-missing", "function show(v:number){console.log(typeof v,v===undefined,Number.isNaN(v))}function read(a:Uint8Array){show(a[1])}read(new Uint8Array(1));", "undefined true false\n"];
        yield return ["number-forwarded-closure", "function read(a:Uint16Array):number{return a[1]}function forward(a:Uint16Array){return ()=>read(a)}console.log(forward(new Uint16Array(1))()===undefined);", "true\n"];
        yield return ["number-local-and-operation", "function read(a:Float64Array){let v:number=a[1];console.log(v===undefined,Number.isNaN(v+1),v??7)}read(new Float64Array(1));", "true true 7\n"];
        yield return ["declared-contract-is-not-number-storage", "function read(a:Int8Array):number{return a[0]}const fake:any={0:\"raw\"};const value:number=read(fake);console.log(typeof value,value,read(new Int8Array([7])));", "string raw 7\n"];
        yield return ["declared-contract-is-not-bigint-storage", "function read(a:BigInt64Array):bigint{return a[0]}const fake:any={0:7};console.log(typeof read(fake));try{console.log(read(fake)+1n)}catch(e){console.log(e instanceof TypeError)}", "number\ntrue\n"];
        yield return ["bigint-parameter-missing", "function read(a:BigInt64Array):bigint{return a[1]}console.log(read(new BigInt64Array(1))===undefined,typeof read(new BigInt64Array(1)));", "true undefined\n"];
        yield return ["bigint-argument-missing", "function add(v:bigint){try{console.log(v+1n)}catch(e){console.log(e instanceof TypeError)}}function read(a:BigUint64Array){add(a[1]);add(a[0])}read(new BigUint64Array([7n]));", "true\n8n\n"];
        yield return ["bigint-forwarded-return", "function read(a:BigInt64Array):bigint{return a[1]}function forward(a:BigInt64Array):bigint{return read(a)}console.log(forward(new BigInt64Array(1))===undefined);", "true\n"];
        yield return ["mixed-declared-union", "function read(a:Int8Array|BigInt64Array):number|bigint{return a[1]}console.log(read(new Int8Array(1))===undefined,read(new BigInt64Array(1))===undefined);", "true true\n"];
        yield return ["all-number-families", "const a:Int8Array=new Int8Array([1]);const b:Uint8Array=new Uint8Array([2]);const c:Uint8ClampedArray=new Uint8ClampedArray([3]);const d:Int16Array=new Int16Array([4]);const e:Uint16Array=new Uint16Array([5]);const f:Int32Array=new Int32Array([6]);const g:Uint32Array=new Uint32Array([7]);const h:Float32Array=new Float32Array([8]);const i:Float64Array=new Float64Array([9]);console.log(a[0],b[0],c[0],d[0],e[0],f[0],g[0],h[0],i[0]);", "1 2 3 4 5 6 7 8 9\n"];
        yield return ["buffer-subview-facet", "const buffer=new ArrayBuffer(8);const a:Uint16Array<ArrayBuffer>=new Uint16Array(buffer,2,2);a[0]=7;console.log(a.buffer===buffer,a.byteOffset,a.length,a[0]);", "true 2 2 7\n"];
        yield return ["shared-buffer-facet", "const buffer=new SharedArrayBuffer(4);const a:Uint8Array<SharedArrayBuffer>=new Uint8Array(buffer);a[0]=7;console.log(a.buffer===buffer,a[0]);", "true 7\n"];
        yield return ["copy-allocates-arraybuffer", "const original:Uint8Array<SharedArrayBuffer>=new Uint8Array(new SharedArrayBuffer(2));original[0]=7;const copy:Uint8Array<ArrayBuffer>=new Uint8Array(original);console.log(copy[0],!Object.is(copy.buffer,original.buffer));", "7 true\n"];
        yield return ["alias-annotation", "type View=Uint8Array;function read(a:View):number{return a[1]}console.log(read(new Uint8Array(1))===undefined);", "true\n"];
        yield return ["constrained-annotation", "function read<T extends Uint8Array>(a:T):number{return a[1]}console.log(read(new Uint8Array(1))===undefined);", "true\n"];
        yield return ["annotated-write-and-method", "const a:Uint8Array=new Uint8Array([1,2]);a[0]=7;const b:Uint8Array=a.subarray(0,1);console.log(a[0],b[0],b.length);", "7 7 1\n"];
        yield return ["local-constructor-shadow", "function run(){class Uint8Array{value=7}const a:Uint8Array=new Uint8Array();console.log(a.value)}run();", "7\n"];
        yield return ["namespace-type-shadow", "namespace Local{export interface Uint8Array{value:number}}const actual:Uint8Array=new Uint8Array([7]);const local:Local.Uint8Array={value:9};console.log(actual[0],local.value);", "7 9\n"];
        yield return ["ordinary-array-control", "const a:number[]=[7];console.log(a[0]);", "7\n"];
        yield return ["bigint-forwarded-raw-arithmetic", "function read(a:BigInt64Array):bigint{return a[0]}function forward(a:BigInt64Array):bigint{return read(a)}const fake:any={0:7};console.log(typeof forward(fake));try{console.log(forward(fake)+1n)}catch(e){console.log(e instanceof TypeError)}", "number\ntrue\n"];
        yield return ["bigint-mutual-recursive-raw-return", "function first(a:BigInt64Array,n:number):bigint{return n===0?a[0]:second(a,n-1)}function second(a:BigInt64Array,n:number):bigint{return first(a,n)}const fake:any={0:7};try{console.log(second(fake,2)+1n)}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["bigint-raw-argument-identity", "function read(a:BigInt64Array):bigint{return a[0]}function identity(value:bigint):bigint{return value}const fake:any={0:7};try{console.log(identity(read(fake))+1n)}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["unrelated-pure-bigint-recursion", "function read(a:BigInt64Array):bigint{return a[0]}function pure(n:number):bigint{return n===0?7n:pure(n-1)}const fake:any={0:7};console.log(typeof read(fake),String(pure(2)+1n));", "number 8\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DeclaredAnnotationsPreserveActualValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
