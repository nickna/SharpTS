using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayDetachedOwnerTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["allocated-owner-detach-original", "const a:any=new Uint8Array([7,8]);const b:any=a.buffer;structuredClone(b,{transfer:[b]});console.log(b.byteLength,a.length,a.byteLength,a.byteOffset,a[0]);", "0 0 0 0 undefined\n"];
        yield return ["explicit-owner-detach-control", "const b:any=new ArrayBuffer(8);const a:any=new Uint8Array(b,2,3);a[0]=7;structuredClone(b,{transfer:[b]});console.log(b.byteLength,a.length,a.byteLength,a.byteOffset,a[0]);", "0 0 0 0 undefined\n"];
        yield return ["slice-pre-post", "const a:any=new Uint8Array([7,8]);console.log(a.slice(0)[0]);structuredClone(a.buffer,{transfer:[a.buffer]});try{a.slice(0);console.log(\"returned\")}catch(e){console.log(e instanceof TypeError)}", "7\ntrue\n"];
        yield return ["subarray-pre-post", "const a:any=new Uint8Array([7,8]);console.log(a.subarray(0).length);structuredClone(a.buffer,{transfer:[a.buffer]});try{a.subarray(0);console.log(\"returned\")}catch(e){console.log(e instanceof TypeError)}", "2\ntrue\n"];
        yield return ["set-empty-pre-post", "const a:any=new Uint8Array([7,8]);a.set([9]);console.log(a[0]);structuredClone(a.buffer,{transfer:[a.buffer]});try{a.set([]);console.log(\"returned\")}catch(e){console.log(e instanceof TypeError)}", "9\ntrue\n"];
        yield return ["fill-empty-range-pre-post", "const a:any=new Uint8Array([7,8]);console.log(a.fill(3,0,0)===a);structuredClone(a.buffer,{transfer:[a.buffer]});try{a.fill(3,0,0);console.log(\"returned\")}catch(e){console.log(e instanceof TypeError)}", "true\ntrue\n"];
        yield return ["shared-owner-subviews", "const b:any=new ArrayBuffer(8);const a:any=new Uint8Array(b,2,3);const v:any=a.subarray(1);a[1]=7;console.log(v[0]);structuredClone(b,{transfer:[b]});console.log(a.length,v.length,a.byteOffset,v.byteOffset,a[1],v[0]);", "7\n0 0 0 0 undefined undefined\n"];
        yield return ["all-element-kinds-detached", "const views:any[]=[new Int8Array(1),new Uint8Array(1),new Uint8ClampedArray(1),new Int16Array(1),new Uint16Array(1),new Int32Array(1),new Uint32Array(1),new Float32Array(1),new Float64Array(1),new BigInt64Array(1),new BigUint64Array(1)];for(const a of views){structuredClone(a.buffer,{transfer:[a.buffer]});console.log(a.length,a.byteLength,a.byteOffset,a[0]);}", "0 0 0 undefined\n0 0 0 undefined\n0 0 0 undefined\n0 0 0 undefined\n0 0 0 undefined\n0 0 0 undefined\n0 0 0 undefined\n0 0 0 undefined\n0 0 0 undefined\n0 0 0 undefined\n0 0 0 undefined\n"];
        yield return ["empty-view-owner-detached", "const b:any=new ArrayBuffer(8);const a:any=new Uint16Array(b,4,0);structuredClone(b,{transfer:[b]});console.log(b.byteLength,a.length,a.byteLength,a.byteOffset,a[0]);", "0 0 0 0 undefined\n"];
        yield return ["clone-without-transfer-attached", "const a:any=new Uint8Array([7,8]);const b:any=a.buffer;const c:any=structuredClone(b);console.log(b.byteLength,a.length,a[0],c.byteLength,b===c);", "2 2 7 2 false\n"];
        yield return ["unrelated-owner-transfer", "const a:any=new Uint8Array([7]);const b:any=new ArrayBuffer(2);structuredClone(b,{transfer:[b]});console.log(b.byteLength,a.buffer.byteLength,a.length,a[0]);", "0 1 1 7\n"];
        yield return ["owner-transferred-outside-cloned-value", "const a:any=new Uint8Array([7]);console.log(structuredClone(3,{transfer:[a.buffer]}),a.length,a[0]);", "3 0 undefined\n"];
        yield return ["ordinary-properties-survive-detach", "const a:any=new Uint8Array([7]);const s=Symbol('s');Object.defineProperty(a,'x',{value:8});Object.defineProperty(a,s,{value:9});structuredClone(a.buffer,{transfer:[a.buffer]});console.log(a.x,a[s],Object.hasOwn(a,'x'),Object.hasOwn(a,s),a[0]);", "8 9 true true undefined\n"];
        yield return ["physical-presence-after-detach", "const a:any=new Uint8Array([7]);console.log(Object.hasOwn(a,'0'),Reflect.ownKeys(a).length);structuredClone(a.buffer,{transfer:[a.buffer]});console.log(Object.hasOwn(a,'0'),Reflect.ownKeys(a).length,Object.getOwnPropertyDescriptor(a,'0')===undefined);", "true 1\nfalse 0 true\n"];
        yield return ["detached-put-converts-value", "const a:any=new Uint8Array([7]);let calls=0;const v={valueOf(){calls++;return 9}};structuredClone(a.buffer,{transfer:[a.buffer]});a[0]=v;console.log(a[0],calls,a.length);", "undefined 1 0\n"];
        yield return ["value-conversion-detaches-owner", "const a:any=new Uint8Array([7]);let calls=0;const v={valueOf(){calls++;structuredClone(a.buffer,{transfer:[a.buffer]});return 9}};a[0]=v;console.log(a[0],calls,a.length);", "undefined 1 0\n"];
        yield return ["physical-reflect-read", "const a:any=new Uint8Array([7]);console.log(Reflect.get(a,'0',{}));structuredClone(a.buffer,{transfer:[a.buffer]});console.log(Reflect.get(a,'0',{}),Reflect.has(a,'0'));", "7\nundefined false\n"];
        yield return ["key-conversion-detaches-owner", "const a:any=new Uint8Array([7]);let calls=0;const k:any={toString(){calls++;structuredClone(a.buffer,{transfer:[a.buffer]});return '0'}};console.log(a[k],calls,a.length);", "undefined 1 0\n"];
        yield return ["owned-length-shadow-retained", "const a:any=new Uint8Array([7]);Object.defineProperty(a,'length',{value:9});structuredClone(a.buffer,{transfer:[a.buffer]});console.log(a.length,a.byteLength,a.byteOffset,a[0]);", "9 0 0 undefined\n"];
        yield return ["shared-buffer-remains-attached", "const b:any=new SharedArrayBuffer(4);const a:any=new Uint8Array(b,1,2);a[0]=7;const other:any=new ArrayBuffer(1);structuredClone(other,{transfer:[other]});console.log(a.length,a.byteLength,a.byteOffset,a[0],a.buffer===b);", "2 2 1 7 true\n"];
        yield return ["copywithin-empty-detached", "const a:any=new Uint8Array([7]);console.log(a.copyWithin(0,0,0)===a);structuredClone(a.buffer,{transfer:[a.buffer]});try{a.copyWithin(0,0,0);console.log('returned')}catch(e){console.log(e instanceof TypeError)}", "true\ntrue\n"];
        yield return ["reverse-singleton-detached", "const a:any=new Uint8Array([7]);console.log(a.reverse()===a,a[0]);structuredClone(a.buffer,{transfer:[a.buffer]});try{a.reverse();console.log('returned')}catch(e){console.log(e instanceof TypeError)}", "true 7\ntrue\n"];
        yield return ["set-detached-source", "const a:any=new Uint8Array([7]);const b:any=new Uint8Array(2);structuredClone(a.buffer,{transfer:[a.buffer]});try{b.set(a);console.log('returned')}catch(e){console.log(e instanceof TypeError,b.length,b[0])}", "true 2 0\n"];
        yield return ["indexof-empty-detached", "const a:any=new Uint8Array(0);console.log(a.indexOf(7));structuredClone(a.buffer,{transfer:[a.buffer]});try{a.indexOf(7);console.log('returned')}catch(e){console.log(e instanceof TypeError)}", "-1\ntrue\n"];
        yield return ["lastindexof-empty-detached", "const a:any=new Uint8Array(0);console.log(a.lastIndexOf(7));structuredClone(a.buffer,{transfer:[a.buffer]});try{a.lastIndexOf(7);console.log('returned')}catch(e){console.log(e instanceof TypeError)}", "-1\ntrue\n"];
        yield return ["join-empty-detached", "const a:any=new Uint8Array(0);console.log(a.join()=== '');structuredClone(a.buffer,{transfer:[a.buffer]});try{a.join();console.log('returned')}catch(e){console.log(e instanceof TypeError)}", "true\ntrue\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DetachedOwnerInvalidatesViews(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
