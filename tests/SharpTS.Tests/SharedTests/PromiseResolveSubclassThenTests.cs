using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseResolveSubclassThenTests
{
    public const string OriginalSource = "let calls=0;class P extends (Promise as any){then(r:any,j:any){calls++;return super.then(r,j);}}const p:any=P.resolve(7);Promise.resolve(p).then((v:any)=>console.log(\"value\",v),(e:any)=>console.log(\"error\",e.name,e.message));setTimeout(()=>console.log(\"calls\",calls),1);";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original","let calls=0;class P extends (Promise as any){then(r:any,j:any){calls++;return super.then(r,j);}}const p:any=P.resolve(7);Promise.resolve(p).then((v:any)=>console.log(\"value\",v),(e:any)=>console.log(\"error\",e.name,e.message));setTimeout(()=>console.log(\"calls\",calls),1);","value 7\ncalls 1\n"];
        yield return ["inherited-direct","let calls=0;class P extends (Promise as any){then(r:any,j:any){calls++;return super.then(r,j);}}const p:any=P.resolve(7);console.log(p.then===P.prototype.then,p.constructor===P,Object.getPrototypeOf(p)===P.prototype);p.then((v:any)=>console.log(\"value\",v));setTimeout(()=>console.log(\"calls\",calls),1);","true true true\nvalue 7\ncalls 1\n"];
        yield return ["intrinsic-identity","let gets=0;const p:any=Promise.resolve(7);Object.defineProperty(p,\"then\",{get(){gets++;throw 99;},configurable:true});console.log(Promise.resolve(p)===p,gets);","true 0\n"];
        yield return ["retagged-identity","let calls=0;class P extends (Promise as any){then(r:any,j:any){calls++;return super.then(r,j);}}const p:any=P.resolve(7);let gets=0;Object.defineProperty(p,\"constructor\",{value:Promise,configurable:true});Object.defineProperty(p,\"then\",{get(){gets++;throw 99;},configurable:true});console.log(Promise.resolve(p)===p,gets);","true 0\n"];
        yield return ["own-constructor-live-then","const events:string[]=[];let gets=0,calls=0;const p:any=Promise.resolve(1);Object.defineProperty(p,\"constructor\",{value:{},configurable:true});Object.defineProperty(p,\"then\",{get(){gets++;events.push(\"get\");return function(this:any,resolve:any,reject:any){calls++;events.push(this===p?\"then\":\"wrong\");resolve(7);reject(99);};},configurable:true});const q=Promise.resolve(p);events.push(\"after\");queueMicrotask(()=>events.push(\"job\"));q.then(v=>console.log(v,gets,calls,events.join(\",\")));","7 1 1 get,after,then,job\n"];
        yield return ["captured-once","const events:string[]=[];let gets=0,replacements=0;class P extends (Promise as any){}const p:any=P.resolve(1);Object.defineProperty(p,\"then\",{get(){gets++;events.push(\"get\");return function(this:any,resolve:any){events.push(this===p?\"then\":\"wrong\");resolve(8);};},configurable:true});const q=Promise.resolve(p);Object.defineProperty(p,\"then\",{value(){replacements++;},configurable:true});q.then(v=>console.log(v,gets,replacements,events.join(\",\")));","8 1 0 get,then\n"];
        yield return ["inherited-getter","let gets=0,calls=0;const events:string[]=[];class P extends (Promise as any){}const p:any=P.resolve(1);Object.defineProperty(P.prototype,\"then\",{get(){gets++;events.push(this===p?\"get\":\"wrong\");return function(this:any,resolve:any){calls++;events.push(this===p?\"then\":\"wrong\");resolve(9);return 99;};},configurable:true});const q=Promise.resolve(p);events.push(\"after\");q.then(v=>console.log(v,gets,calls,events.join(\",\")));","9 1 1 get,after,then\n"];
        yield return ["getter-abrupt","const marker={};let gets=0;class P extends (Promise as any){}const p:any=P.resolve(1);Object.defineProperty(P.prototype,\"then\",{get(){gets++;throw marker;},configurable:true});const q=Promise.resolve(p);console.log(\"returned\",gets);q.then(()=>console.log(\"wrong\"),error=>console.log(error===marker,gets));","returned 1\ntrue 1\n"];
        yield return ["reject-once","const marker={};let calls=0;class P extends (Promise as any){}const p:any=P.resolve(1);Object.defineProperty(P.prototype,\"then\",{value:function(this:any,resolve:any,reject:any){calls++;reject(marker);resolve(99);throw 99;},configurable:true});Promise.resolve(p).then(()=>console.log(\"wrong\"),error=>console.log(error===marker,calls));","true 1\n"];
        yield return ["inherited-chain","let calls=0;class P extends (Promise as any){then(r:any,j:any){calls++;return super.then(r,j);}}class Q extends P{}const p:any=(Q as any).resolve(7);Promise.resolve(p).then(v=>console.log(v,calls,p.constructor===Q));","7 1 true\n"];
        yield return ["null-prototype","const p:any=Promise.resolve(7);Object.setPrototypeOf(p,null);Promise.resolve(p).then(v=>console.log(v===p,Object.getPrototypeOf(p)===null));","true true\n"];
        yield return ["ordinary-prototype","let calls=0;const p:any=Promise.resolve(7);Object.setPrototypeOf(p,{then(this:any,resolve:any){calls++;resolve(this===p?9:99);}});Promise.resolve(p).then(v=>console.log(v,calls));","9 1\n"];
        yield return ["own-noncallable-wins","let gets=0;class P extends (Promise as any){}const p:any=P.resolve(7);Object.defineProperty(P.prototype,\"then\",{get(){gets++;throw 99;},configurable:true});Object.defineProperty(p,\"then\",{value:7,configurable:true});Promise.resolve(p).then(v=>console.log(v===p,gets));","true 0\n"];
        yield return ["constructor-abrupt","let constructors=0,gets=0;const marker={};const p:any=Promise.resolve(1);Object.defineProperty(p,\"constructor\",{get(){constructors++;throw marker;},configurable:true});Object.defineProperty(p,\"then\",{get(){gets++;throw 99;},configurable:true});try{Promise.resolve(p);console.log(\"wrong\");}catch(error){console.log(error===marker,constructors,gets);}","true 1 0\n"];
        yield return ["foreign-realm","import * as vm from \"node:vm\";const info:any=vm.runInNewContext(\"let calls=0;class P extends Promise{then(r,j){calls++;return super.then(r,j);}}({promise:P.resolve(7),read:()=>calls})\");Promise.resolve(info.promise).then(value=>console.log(value,info.read()),error=>console.log(\"error\",error.name));","7 1\n"];
    }

    public static IEnumerable<object[]> NativeCases() => Cases().Where(row => (string)row[0] != "foreign-realm");

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void AdoptsThroughTheActualLiveThenAfterConstructorIdentityFails(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var actual = name == "foreign-realm"
            ? TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode)
            : TestHarness.Run(source, mode);
        Assert.Equal(expected, actual);
    }
}
