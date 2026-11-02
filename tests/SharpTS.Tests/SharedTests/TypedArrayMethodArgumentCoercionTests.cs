using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayMethodArgumentCoercionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["indexof-coercion-detaches", "const a:any=new Uint8Array([7,8]);const b:any=a.buffer;let calls=0;const arg:any={valueOf(){calls++;structuredClone(b,{transfer:[b]});return 0}};try{console.log(a.indexOf(7,arg),a.length,b.byteLength,calls)}catch(e){console.log('throw',e instanceof TypeError,a.length,b.byteLength,calls)}", "-1 0 0 1\n"];
        yield return ["join-coercion-detaches", "const a:any=new Uint8Array([7,8]);const b:any=a.buffer;let calls=0;const arg:any={toString(){calls++;structuredClone(b,{transfer:[b]});return '-'}};try{console.log(a.join(arg),a.length,b.byteLength,calls)}catch(e){console.log('throw',e instanceof TypeError,a.length,b.byteLength,calls)}", "- 0 0 1\n"];
        yield return ["slice-coercion-detaches", "const a:any=new Uint8Array([7,8]);const b:any=a.buffer;let calls=0;const arg:any={valueOf(){calls++;structuredClone(b,{transfer:[b]});return 0}};try{console.log(a.slice(arg).length,a.length,b.byteLength,calls)}catch(e){console.log('throw',e instanceof TypeError,a.length,b.byteLength,calls)}", "throw true 0 0 1\n"];
        yield return ["subarray-coercion-detaches", "const a:any=new Uint8Array([7,8]);const b:any=a.buffer;let calls=0;const arg:any={valueOf(){calls++;structuredClone(b,{transfer:[b]});return 0}};try{console.log(a.subarray(arg).length,a.length,b.byteLength,calls)}catch(e){console.log('throw',e instanceof TypeError,a.length,b.byteLength,calls)}", "throw true 0 0 1\n"];
        yield return ["fill-coercion-detaches", "const a:any=new Uint8Array([7,8]);const b:any=a.buffer;let calls=0;const arg:any={valueOf(){calls++;structuredClone(b,{transfer:[b]});return 9}};try{console.log(a.fill(arg)===a,a.length,b.byteLength,calls)}catch(e){console.log('throw',e instanceof TypeError,a.length,b.byteLength,calls)}", "throw true 0 0 1\n"];
        yield return ["indexof-object", "const a:any=new Uint8Array([7,8,7]);let n=0;const arg:any={valueOf(){n++;return 1}};console.log(a.indexOf(7,arg),n);", "2 1\n"];
        yield return ["lastindexof-object", "const a:any=new Uint8Array([7,8,7]);let n=0;const arg:any={valueOf(){n++;return 1}};console.log(a.lastIndexOf(7,arg),n);", "0 1\n"];
        yield return ["includes-object", "const a:any=new Uint8Array([7,8,9]);let n=0;const arg:any={valueOf(){n++;return 2}};console.log(a.includes(8,arg),n);", "false 1\n"];
        yield return ["slice-object", "const a:any=new Uint8Array([7,8,9]);let n=0;const arg:any={valueOf(){n++;return 1}};console.log(a.slice(arg).join(\",\"),n);", "8,9 1\n"];
        yield return ["subarray-object", "const a:any=new Uint8Array([7,8,9]);let n=0;const arg:any={valueOf(){n++;return 1}};console.log(a.subarray(arg).join(\",\"),n);", "8,9 1\n"];
        yield return ["copywithin-order", "const a:any=new Uint8Array([7,8,9]);let trace=\"\";function arg(n:number,label:string):any{return {valueOf(){trace+=label;return n}}}console.log(a.copyWithin(arg(1,\"t\"),arg(0,\"s\"),arg(2,\"e\")).join(\",\"),trace);", "7,7,8 tse\n"];
        yield return ["set-object-offset", "const a:any=new Uint8Array([7,8,9]);let n=0;const arg:any={valueOf(){n++;return 1}};a.set([1,2],arg);console.log(a.join(\",\"),n);", "7,1,2 1\n"];
        yield return ["fill-order", "const a:any=new Uint8Array([7,8,9]);let trace=\"\";function arg(n:number,label:string):any{return {valueOf(){trace+=label;return n}}}console.log(a.fill(arg(3,\"v\"),arg(1,\"s\"),arg(2,\"e\")).join(\",\"),trace);", "7,3,9 vse\n"];
        yield return ["null-undefined-end", "const a:any=new Uint8Array([7,8,9]);console.log(a.slice(0,null).length,a.slice(0,undefined).length,a.subarray(0,null).length,a.subarray(0,undefined).length);", "0 3 0 3\n"];
        yield return ["search-index-values", "const a:any=new Uint8Array([7,8,7]);for(const i of [undefined,null,NaN,Infinity,-Infinity,1099511627776,-1099511627776,-1.7,\"1\",true])console.log(a.indexOf(7,i),a.lastIndexOf(7,i),a.includes(8,i));", "0 0 true\n0 0 true\n0 0 true\n-1 2 false\n0 -1 true\n-1 2 false\n0 -1 true\n2 2 false\n2 0 true\n2 0 true\n"];
        yield return ["lastindexof-omitted-vs-undefined", "const a:any=new Uint8Array([7,8,7]);console.log(a.lastIndexOf(7),a.lastIndexOf(7,undefined),a.lastIndexOf(7,null));", "2 0 0\n"];
        yield return ["join-object", "const a:any=new Uint8Array([7,8]);let n=0;const arg:any={toString(){n++;return \"-\"}};console.log(a.join(arg),n);", "7-8 1\n"];
        yield return ["join-null-undefined", "const a:any=new Uint8Array([7,8]);console.log(a.join(null),a.join(undefined),a.join());", "7null8 7,8 7,8\n"];
        yield return ["conversion-receiver", "const a:any=new Uint8Array([7,8]);let n=0;const arg:any={valueOf(){n++;console.log(this===arg);return 1}};console.log(a.slice(arg)[0],n);", "true\n8 1\n"];
        yield return ["ordinary-primitive-fallback", "const a:any=new Uint8Array([7,8]);let trace=\"\";const arg:any={valueOf(){trace+=\"v\";return {}},toString(){trace+=\"s\";return \"1\"}};console.log(a.slice(arg)[0],trace);", "8 vs\n"];
        yield return ["numeric-abrupt-identity", "const a:any=new Uint8Array([7,8]);const reason={};try{a.slice({valueOf(){throw reason}})}catch(e){console.log(e===reason)}", "true\n"];
        yield return ["numeric-nonprimitive-typeerror", "const a:any=new Uint8Array([7,8]);try{a.slice({valueOf(){return {}},toString(){return {}}})}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["numeric-symbol-bigint-refused", "const a:any=new Uint8Array([7,8]);for(const arg of [Symbol(\"x\"),1n])try{a.slice(arg)}catch(e){console.log(e instanceof TypeError)}", "true\ntrue\n"];
        yield return ["numeric-exotic-hint", "const a:any=new Uint8Array([7,8]);const arg:any={[Symbol.toPrimitive](hint:any){console.log(hint);return 1}};console.log(a.slice(arg)[0]);", "number\n8\n"];
        yield return ["separator-exotic-hint", "const a:any=new Uint8Array([7,8]);const arg:any={[Symbol.toPrimitive](hint:any){console.log(hint);return \"-\"}};console.log(a.join(arg));", "string\n7-8\n"];
        yield return ["separator-symbol-refused", "const a:any=new Uint8Array([7,8]);try{a.join(Symbol(\"x\"))}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["empty-join-converts", "const a:any=new Uint8Array(0);let n=0;console.log(a.join({toString(){n++;return \"-\"}}),n);", " 1\n"];
        yield return ["empty-search-skips-index-conversion", "const a:any=new Uint8Array(0);let n=0;const arg:any={valueOf(){n++;return 1}};console.log(a.indexOf(7,arg),a.lastIndexOf(7,arg),a.includes(7,arg),n);", "-1 -1 false 0\n"];
        yield return ["inferred-receiver", "const a=new Uint8Array([7,8]);const arg:any={valueOf(){return 1}};console.log(a.slice(arg)[0],a.indexOf(7,arg));", "8 -1\n"];
        yield return ["bigint-fill-conversion", "const a:any=new BigInt64Array([1n,2n]);let n=0;const value:any={valueOf(){n++;return 3n}};console.log(a.fill(value)===a,String(a[0]),String(a[1]),n);", "true 3 3 1\n"];
        yield return ["bigint-fill-number-refused", "const a:any=new BigInt64Array([1n]);let n=0;try{a.fill({valueOf(){n++;return 3}})}catch(e){console.log(e instanceof TypeError,n,String(a[0]))}", "true 1 1\n"];
        yield return ["empty-fill-converts", "const a:any=new Uint8Array(0);const reason={};let n=0;try{a.fill({valueOf(){n++;throw reason}})}catch(e){console.log(e===reason,n)}", "true 1\n"];
        yield return ["detached-receiver-before-method-conversion", "const a:any=new Uint8Array([7]);structuredClone(a.buffer,{transfer:[a.buffer]});let n=0;const arg:any={valueOf(){n++;return 0},toString(){n++;return \"-\"}};for(const name of [\"indexOf\",\"join\",\"slice\",\"subarray\",\"fill\"])try{if(name===\"indexOf\")a[name](7,arg);else a[name](arg)}catch(e){console.log(e instanceof TypeError,n)}", "true 0\ntrue 0\ntrue 0\ntrue 1\ntrue 1\n"];
        yield return ["set-offset-before-detached-validation", "const a:any=new Uint8Array([7]);structuredClone(a.buffer,{transfer:[a.buffer]});let n=0;const arg:any={valueOf(){n++;return 0}};try{a.set([],arg)}catch(e){console.log(e instanceof TypeError,n)}", "true 1\n"];
        yield return ["set-offset-range", "const a:any=new Uint8Array(2);for(const offset of [-1,Infinity,1099511627776])try{a.set([],offset);console.log(\"returned\")}catch(e){console.log(e instanceof RangeError)}", "true\ntrue\ntrue\n"];
        yield return ["zero-copy-detachment", "for(const name of [\"slice\",\"copyWithin\"]){const a:any=new Uint8Array([7,8]);let n=0;const arg:any={valueOf(){n++;structuredClone(a.buffer,{transfer:[a.buffer]});return 0}};try{if(name===\"slice\")console.log(a.slice(arg,0).length,a.length,n);else console.log(a.copyWithin(arg,0,0)===a,a.length,n)}catch(e){console.log(\"throw\",e instanceof TypeError,a.length,n)}}", "0 0 1\ntrue 0 1\n"];
        yield return ["post-detach-search-undefined", "for(const name of [\"indexOf\",\"lastIndexOf\",\"includes\"]){const a:any=new Uint8Array([7,8]);let n=0;const arg:any={valueOf(){n++;structuredClone(a.buffer,{transfer:[a.buffer]});return 0}};console.log(a[name](undefined,arg),a.length,n)}", "-1 0 1\n-1 0 1\ntrue 0 1\n"];
        yield return ["relative-fill-copy-bounds", "const a:any=new Uint8Array([7,8,9]);const n:any={valueOf(){return -1}};console.log(a.fill(3,n).join(\",\"));const b:any=new Uint8Array([7,8,9]);console.log(b.copyWithin(n,0,1).join(\",\"));", "7,8,3\n7,8,7\n"];
        yield return ["direct-detached-receiver-conversion", "const a:any=new Uint8Array([7]);structuredClone(a.buffer,{transfer:[a.buffer]});let n=0;const arg:any={valueOf(){n++;return 0},toString(){n++;return \"-\"}};try{a.indexOf(7,arg)}catch(e){console.log(e instanceof TypeError,n)}try{a.join(arg)}catch(e){console.log(e instanceof TypeError,n)}try{a.slice(arg)}catch(e){console.log(e instanceof TypeError,n)}try{a.subarray(arg)}catch(e){console.log(e instanceof TypeError,n)}try{a.fill(arg)}catch(e){console.log(e instanceof TypeError,n)}", "true 0\ntrue 0\ntrue 0\ntrue 1\ntrue 1\n"];
        yield return ["direct-post-detach-search-undefined", "function check(run:(a:any,arg:any)=>any){const a:any=new Uint8Array([7,8]);let n=0;const arg:any={valueOf(){n++;structuredClone(a.buffer,{transfer:[a.buffer]});return 0}};console.log(run(a,arg),a.length,n)}check((a,arg)=>a.indexOf(undefined,arg));check((a,arg)=>a.lastIndexOf(undefined,arg));check((a,arg)=>a.includes(undefined,arg));", "-1 0 1\n-1 0 1\ntrue 0 1\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Where(mode => mode == ExecutionMode.Interpreted
                || (string)row[0] is not "detached-receiver-before-method-conversion" and not "post-detach-search-undefined")
            .Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void MethodArgumentsUseGuestCoercion(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
