using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[Collection("CompilationService")]
public sealed class DeletedRootConstructorReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const p:any=Object.prototype;delete p.constructor;Object.preventExtensions(p);const accepted=Reflect.defineProperty(p,'constructor',{value:9});console.log(accepted,Object.hasOwn(p,'constructor'),p.constructor===undefined,p.constructor===Object,typeof p.constructor);", "false false true false undefined\n"];
        yield return ["direct-dot-delete", "const p:any=Object.prototype;console.log(p.constructor===Object,delete p.constructor,p.constructor===undefined,typeof p.constructor,Object.hasOwn(p,'constructor'));", "true true true undefined false\n"];
        yield return ["direct-bracket-delete", "const p:any=Object.prototype;delete p['constructor'];console.log(p['constructor']===undefined,typeof p['constructor']);", "true undefined\n"];
        yield return ["held-root", "const root:any=Object.prototype;function read(p:any){return p.constructor}delete root.constructor;console.log(read(root)===undefined,typeof read(root));", "true undefined\n"];
        yield return ["object-key-once", "const p:any=Object.prototype;delete p.constructor;let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);calls++;return 'constructor'}};console.log(p[key]===undefined,calls);", "string\ntrue 1\n"];
        yield return ["ordinary-object-live-root", "const p:any=Object.prototype,child:any={};delete p.constructor;console.log(child.constructor===undefined,typeof child.constructor,Object.hasOwn(child,'constructor'));", "true undefined false\n"];
        yield return ["ordinary-object-baseline", "const child:any={};console.log(child.constructor===Object,Object.prototype.constructor===Object);", "true true\n"];
        yield return ["recreated-data", "const p:any=Object.prototype;delete p.constructor;p.constructor=9;const d:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(p.constructor,d.value,d.writable,d.enumerable,d.configurable);", "9 9 true true true\n"];
        yield return ["recreated-accessor", "const p:any=Object.prototype;delete p.constructor;let calls=0;Object.defineProperty(p,'constructor',{get(){calls++;return this===p?7:99},configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(calls,typeof d.get,p.constructor,calls);", "0 function 7 1\n"];
        yield return ["getter-abrupt", "const p:any=Object.prototype,marker={};delete p.constructor;Object.defineProperty(p,'constructor',{get(){throw marker},configurable:true});try{console.log(p.constructor)}catch(e){console.log(e===marker)}", "true\n"];
        yield return ["null-prototype-control", "const p:any=Object.create(null);console.log(p.constructor===undefined,Object.getPrototypeOf(p)===null);", "true true\n"];
        yield return ["own-constructor-control", "const p:any=Object.prototype,child:any={constructor:7};delete p.constructor;console.log(child.constructor,Object.hasOwn(child,'constructor'));", "7 true\n"];
        yield return ["source-class-control", "class C{}const p:any=Object.prototype;delete p.constructor;console.log(C.prototype.constructor===C,new C().constructor===C);", "true true\n"];
        yield return ["refused-delete-control", "const p:any=Object.prototype;Object.defineProperty(p,'constructor',{value:9,configurable:false});console.log(delete p.constructor,p.constructor,Object.hasOwn(p,'constructor'));", "false 9 true\n"];
        yield return ["descendant-getter-receiver", "const o:any={};Object.defineProperty(Object.prototype,'constructor',{get(){return this===o?7:99},configurable:true});console.log(o.constructor);", "7\n"];
        yield return ["descendant-setter-only", "const o:any={};let calls=0;Object.defineProperty(Object.prototype,'constructor',{set(value:any){calls++},configurable:true});console.log(o.constructor===undefined,calls);", "true 0\n"];
        yield return ["descendant-null", "const o:any={};Object.defineProperty(Object.prototype,'constructor',{value:null,configurable:true});console.log(o.constructor===null);", "true\n"];
        yield return ["descendant-undefined", "const o:any={};Object.defineProperty(Object.prototype,'constructor',{value:undefined,configurable:true});console.log(o.constructor===undefined);", "true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ReadsUseTheLiveIntrinsicProperty(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
