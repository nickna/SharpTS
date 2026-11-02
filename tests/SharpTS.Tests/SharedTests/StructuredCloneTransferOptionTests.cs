using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StructuredCloneTransferOptionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["plain-buffer-transfer-original", "const b:any=new ArrayBuffer(2);const c:any=structuredClone(b,{transfer:[b]});console.log(b.byteLength,c.byteLength,b===c);", "0 2 false\n"];
        yield return ["transfer-preserves-copied-bytes", "const b:any=new ArrayBuffer(2);new Uint8Array(b)[0]=7;const c:any=structuredClone(b,{transfer:[b]});console.log(b.byteLength,c.byteLength,new Uint8Array(c)[0]);", "0 2 7\n"];
        yield return ["copy-without-options", "const b:any=new ArrayBuffer(2);const c:any=structuredClone(b);console.log(b.byteLength,c.byteLength,b===c);", "2 2 false\n"];
        yield return ["empty-transfer", "const b:any=new ArrayBuffer(2);const c:any=structuredClone(b,{transfer:[]});console.log(b.byteLength,c.byteLength,b===c);", "2 2 false\n"];
        yield return ["undefined-and-null-options", "const b:any=new ArrayBuffer(2);const c:any=structuredClone(b,undefined);const d:any=structuredClone(b,null as any);console.log(b.byteLength,c.byteLength,d.byteLength,b===c,b===d);", "2 2 2 false false\n"];
        yield return ["aliased-clone-transfer-outside-value", "const f:any=structuredClone;const b:any=new ArrayBuffer(2);console.log(f(7,{transfer:[b]}),b.byteLength);", "7 0\n"];
        yield return ["global-escape-clone", "const f:any=(globalThis as any)['structuredClone'];const b:any=new ArrayBuffer(2);const c:any=f(b,{transfer:[b]});console.log(b.byteLength,c.byteLength);", "0 2\n"];
        yield return ["transfer-getter-once-receiver", "const b:any=new ArrayBuffer(2);let reads=0;const options:any={get transfer(){reads++;console.log(this===options);return [b]}};const c:any=structuredClone(b,options);console.log(reads,b.byteLength,c.byteLength);", "true\n1 0 2\n"];
        yield return ["transfer-getter-abrupt", "const b:any=new ArrayBuffer(2),reason={};const options:any={get transfer(){throw reason}};try{structuredClone(b,options)}catch(e){console.log(e===reason,b.byteLength)}", "true 2\n"];
        yield return ["null-transfer-typeerror", "const b:any=new ArrayBuffer(2);try{structuredClone(b,{transfer:null} as any)}catch(e){console.log(e instanceof TypeError,b.byteLength)}", "true 2\n"];
        yield return ["primitive-options-typeerror", "const b:any=new ArrayBuffer(2);try{structuredClone(b,3 as any)}catch(e){console.log(e instanceof TypeError,b.byteLength)}", "true 2\n"];
        yield return ["primitive-transfer-item-typeerror", "const b:any=new ArrayBuffer(2);try{structuredClone(b,{transfer:[null]} as any)}catch(e){console.log(e instanceof TypeError,b.byteLength)}", "true 2\n"];
        yield return ["invalid-object-item-without-binary-feature", "try{structuredClone(1,{transfer:[{}]} as any)}catch(e){console.log(String(e).includes('DataCloneError'))}", "true\n"];
        yield return ["duplicate-buffer-refusal", "const b:any=new ArrayBuffer(2);try{structuredClone(1,{transfer:[b,b]})}catch(e){console.log(String(e).includes('DataCloneError'),b.byteLength)}", "true 2\n"];
        yield return ["sequence-abrupt-before-duplicate-validation", "const b:any=new ArrayBuffer(2),reason={};function* items(){yield b;yield b;throw reason}try{structuredClone(1,{transfer:items()} as any)}catch(e){console.log(e===reason,b.byteLength)}", "true 2\n"];
        yield return ["clone-failure-does-not-detach", "const b:any=new ArrayBuffer(2);try{structuredClone(()=>1,{transfer:[b]})}catch(e){console.log(String(e).includes('DataCloneError'),b.byteLength)}", "true 2\n"];
        yield return ["multiple-owners-outside-payload", "const a:any=new ArrayBuffer(2),b:any=new ArrayBuffer(3);console.log(structuredClone(9,{transfer:[a,b]}),a.byteLength,b.byteLength);", "9 0 0\n"];
        yield return ["set-iterable-transfer", "const b:any=new ArrayBuffer(2);const c:any=structuredClone(b,{transfer:new Set([b])} as any);console.log(b.byteLength,c.byteLength);", "0 2\n"];
        yield return ["already-detached-transfer-refusal", "const b:any=new ArrayBuffer(2);structuredClone(b,{transfer:[b]});try{structuredClone(1,{transfer:[b]})}catch(e){console.log(String(e).includes('DataCloneError'),b.byteLength)}", "true 0\n"];
        yield return ["empty-transfer-without-binary-feature", "const c:any=structuredClone({x:7},{transfer:[]});console.log(c.x);", "7\n"];
        yield return ["shared-buffer-not-transferable", "const b:any=new SharedArrayBuffer(2);try{structuredClone(1,{transfer:[b]} as any)}catch(e){console.log(String(e).includes('DataCloneError'),b.byteLength)}", "true 2\n"];
        yield return ["iterator-close-on-webidl-item-error", "let iteratorClosedCalls=0;function* items(){try{yield null}finally{iteratorClosedCalls++}}try{structuredClone(1,{transfer:items()} as any)}catch(e){console.log(e instanceof TypeError,iteratorClosedCalls)}", "true 0\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void CompilerHonorsTransferOptions(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
