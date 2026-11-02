using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayAnnotatedReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["declared-contract-is-not-number-storage", "function read(a:Int8Array):number{return a[0]}const fake:any={0:\"raw\"};const value:number=read(fake);console.log(typeof value,value,read(new Int8Array([7])));", "string raw 7\n"];
        yield return ["declared-contract-is-not-bigint-storage", "function read(a:BigInt64Array):bigint{return a[0]}const fake:any={0:7};console.log(typeof read(fake));try{console.log(read(fake)+1n)}catch(e){console.log(e instanceof TypeError)}", "number\ntrue\n"];
        yield return ["ordinary-array-receiver", "function read(a:Int8Array):number{return a[0]}const fake:any=[9];console.log(read(fake),read(new Int8Array([7])));", "9 7\n"];
        yield return ["other-typed-array-kind", "function read(a:Int8Array):number{return a[0]}const other:any=new Uint8Array([255]);console.log(read(other));", "255\n"];
        yield return ["numeric-consumer", "let n=0;function read(a:Int8Array):number{return -a[0]}const fake:any={0:{valueOf(){n++;return 4}}};console.log(read(fake),n);", "-4 1\n"];
        yield return ["getter-once", "let n=0;function read(a:Int8Array):number{return a[0]}const fake:any={get 0(){n++;return 8}};console.log(read(fake),n);", "8 1\n"];
        yield return ["loop-receiver", "function sum(a:Int8Array):number{let n=0;for(let i=0;i<2;i++)n+=a[i];return n}const fake:any={0:7,1:8};console.log(sum(fake),sum(new Int8Array([1,2])));", "15 3\n"];
        yield return ["numeric-write-receiver", "function put(a:Int8Array){a[0]=7}const fake:any={0:3};put(fake);console.log(fake[0]);const real=new Int8Array([3]);put(real);console.log(real[0]);", "7\n7\n"];
        yield return ["loop-write-receiver", "function put(a:Int8Array){for(let i=0;i<2;i++)a[i]=i+7}const fake:any={0:3,1:4};put(fake);console.log(fake[0],fake[1]);", "7 8\n"];
        yield return ["raw-value-write-receiver", "function put(a:Int8Array,value:number):number{return a[0]=value}const fake:any={0:3};const raw:any=\"raw\";console.log(put(fake,raw),fake[0]);", "raw raw\n"];
        yield return ["numeric-index-on-ordinary-object", "function read(a:Int8Array):number{return a[0.5]}const fake:any={\"0.5\":\"raw\"};console.log(read(fake));", "raw\n"];
        yield return ["receiver-before-index-effect", "function read(a:Int8Array,b:Int8Array):number{function index(){a=b;return 0}return a[index()]}const a:any={0:7};const b:any={0:9};console.log(read(a,b));", "7\n"];
        yield return ["proxy-ordinary-receiver", "let n=0;const fake:any=new Proxy({0:\"raw\"},{get(target:any,key:any,receiver:any){n++;return Reflect.get(target,key,receiver)}});function read(a:Int8Array):number{return a[0]}console.log(read(fake),n);", "raw 1\n"];
        yield return ["compound-raw-result", "function put(a:Int8Array):number{return a[0]+=2}const fake:any={0:\"raw\"};console.log(put(fake),fake[0]);const real=new Int8Array([3]);console.log(put(real),real[0]);", "raw2 raw2\n5 5\n"];
        yield return ["compound-loop-receiver", "function put(a:Int8Array){for(let i=0;i<2;i++)a[i]+=2}const fake:any={0:3,1:4};put(fake);console.log(fake[0],fake[1]);const real=new Int8Array([3,4]);put(real);console.log(real[0],real[1]);", "5 6\n5 6\n"];
        yield return ["strict-readonly-write", "\"use strict\";function put(a:Int8Array){a[0]=7}const fake:any={};Object.defineProperty(fake,\"0\",{value:3,writable:false});try{put(fake)}catch(e){console.log(e instanceof TypeError)}console.log(fake[0]);", "true\n3\n"];
        yield return ["strict-nonextensible-write", "\"use strict\";function put(a:Int8Array){a[0]=7}const fake:any=Object.preventExtensions({});try{put(fake)}catch(e){console.log(e instanceof TypeError)}console.log(Object.prototype.hasOwnProperty.call(fake,\"0\"));", "true\nfalse\n"];
        yield return ["sloppy-readonly-write", "function put(a:Int8Array):number{return a[0]=7}const fake:any={};Object.defineProperty(fake,\"0\",{value:3,writable:false});console.log(put(fake),fake[0]);", "7 3\n"];
        yield return ["compound-other-typed-array", "function put(a:Int8Array):number{return a[0]+=2}const other:any=new Uint8Array([255]);console.log(put(other),other[0]);", "257 1\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void AnnotationDoesNotEstablishRuntimeCarrier(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
