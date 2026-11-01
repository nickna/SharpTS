using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class MutablePrototypeReflectOwnKeysTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-reflect-new-string", "const p:any=String.prototype;Reflect.defineProperty(p,'probe',{value:1,configurable:true});console.log(Reflect.ownKeys(p).includes('probe'));\n", "true\n"];
        yield return ["number-reflect-new-string", "const p:any=Number.prototype;Reflect.defineProperty(p,'probe',{value:1,configurable:true});console.log(Reflect.ownKeys(p).includes('probe'));\n", "true\n"];
        yield return ["boolean-reflect-new-string", "const p:any=Boolean.prototype;Reflect.defineProperty(p,'probe',{value:1,configurable:true});console.log(Reflect.ownKeys(p).includes('probe'));\n", "true\n"];
        yield return ["array-reflect-new-string", "const p:any=Array.prototype;Reflect.defineProperty(p,'probe',{value:1,configurable:true});console.log(Reflect.ownKeys(p).includes('probe'));\n", "true\n"];
        yield return ["string-stored-key-order-direct-loop", "const p:any=String.prototype;const s1=Symbol('one'),s2=Symbol('two');Object.defineProperty(p,'b',{value:1,configurable:true});p[s1]=1;Object.defineProperty(p,'10',{value:1,configurable:true});Object.defineProperty(p,'2',{value:1,configurable:true});Object.defineProperty(p,'a',{value:1,enumerable:true,configurable:true});p[s2]=2;const names=['b','10','2','a'];const keys=Reflect.ownKeys(p);let selected='',count=0;for(let i=0;i<keys.length;i++){const k=keys[i];if(k===s1||k===s2||(typeof k==='string'&&names.includes(k))){if(count++)selected+=',';selected+=typeof k==='symbol'?(k===s1?'s1':'s2'):String(k)}}console.log(selected);\n", "2,10,b,a,s1,s2\n"];
        yield return ["string-accessor-delete-recreate-direct-loop", "const p:any=String.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 7},configurable:true});Object.defineProperty(p,'erase',{value:1,configurable:true});Object.defineProperty(p,'tail',{value:2,configurable:true});delete p.erase;Object.defineProperty(p,'erase',{value:3,configurable:true});const names=['probe','erase','tail'];const keys=Reflect.ownKeys(p);let selected='',count=0;for(let i=0;i<keys.length;i++){const k=keys[i];if(typeof k==='string'&&names.includes(k)){if(count++)selected+=',';selected+=k}}console.log(selected,gets);\n", "probe,tail,erase 0\n"];
        yield return ["string-deleted-builtin-not-resurrected", "const p:any=String.prototype;Object.defineProperty(p,'toString',{value:function(){return 'changed'},configurable:true});console.log(Reflect.ownKeys(p).includes('toString'));delete p.toString;Object.preventExtensions(p);console.log(Reflect.ownKeys(p).includes('toString'));\n", "true\nfalse\n"];
        yield return ["number-stored-key-order-direct-loop", "const p:any=Number.prototype;const s1=Symbol('one'),s2=Symbol('two');Object.defineProperty(p,'b',{value:1,configurable:true});p[s1]=1;Object.defineProperty(p,'10',{value:1,configurable:true});Object.defineProperty(p,'2',{value:1,configurable:true});Object.defineProperty(p,'a',{value:1,enumerable:true,configurable:true});p[s2]=2;const names=['b','10','2','a'];const keys=Reflect.ownKeys(p);let selected='',count=0;for(let i=0;i<keys.length;i++){const k=keys[i];if(k===s1||k===s2||(typeof k==='string'&&names.includes(k))){if(count++)selected+=',';selected+=typeof k==='symbol'?(k===s1?'s1':'s2'):String(k)}}console.log(selected);\n", "2,10,b,a,s1,s2\n"];
        yield return ["number-accessor-delete-recreate-direct-loop", "const p:any=Number.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 7},configurable:true});Object.defineProperty(p,'erase',{value:1,configurable:true});Object.defineProperty(p,'tail',{value:2,configurable:true});delete p.erase;Object.defineProperty(p,'erase',{value:3,configurable:true});const names=['probe','erase','tail'];const keys=Reflect.ownKeys(p);let selected='',count=0;for(let i=0;i<keys.length;i++){const k=keys[i];if(typeof k==='string'&&names.includes(k)){if(count++)selected+=',';selected+=k}}console.log(selected,gets);\n", "probe,tail,erase 0\n"];
        yield return ["number-deleted-builtin-not-resurrected", "const p:any=Number.prototype;Object.defineProperty(p,'toString',{value:function(){return 'changed'},configurable:true});console.log(Reflect.ownKeys(p).includes('toString'));delete p.toString;Object.preventExtensions(p);console.log(Reflect.ownKeys(p).includes('toString'));\n", "true\nfalse\n"];
        yield return ["boolean-stored-key-order-direct-loop", "const p:any=Boolean.prototype;const s1=Symbol('one'),s2=Symbol('two');Object.defineProperty(p,'b',{value:1,configurable:true});p[s1]=1;Object.defineProperty(p,'10',{value:1,configurable:true});Object.defineProperty(p,'2',{value:1,configurable:true});Object.defineProperty(p,'a',{value:1,enumerable:true,configurable:true});p[s2]=2;const names=['b','10','2','a'];const keys=Reflect.ownKeys(p);let selected='',count=0;for(let i=0;i<keys.length;i++){const k=keys[i];if(k===s1||k===s2||(typeof k==='string'&&names.includes(k))){if(count++)selected+=',';selected+=typeof k==='symbol'?(k===s1?'s1':'s2'):String(k)}}console.log(selected);\n", "2,10,b,a,s1,s2\n"];
        yield return ["boolean-accessor-delete-recreate-direct-loop", "const p:any=Boolean.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 7},configurable:true});Object.defineProperty(p,'erase',{value:1,configurable:true});Object.defineProperty(p,'tail',{value:2,configurable:true});delete p.erase;Object.defineProperty(p,'erase',{value:3,configurable:true});const names=['probe','erase','tail'];const keys=Reflect.ownKeys(p);let selected='',count=0;for(let i=0;i<keys.length;i++){const k=keys[i];if(typeof k==='string'&&names.includes(k)){if(count++)selected+=',';selected+=k}}console.log(selected,gets);\n", "probe,tail,erase 0\n"];
        yield return ["boolean-deleted-builtin-not-resurrected", "const p:any=Boolean.prototype;Object.defineProperty(p,'toString',{value:function(){return 'changed'},configurable:true});console.log(Reflect.ownKeys(p).includes('toString'));delete p.toString;Object.preventExtensions(p);console.log(Reflect.ownKeys(p).includes('toString'));\n", "true\nfalse\n"];
        yield return ["array-stored-key-order-direct-loop", "const p:any=Array.prototype;const s1=Symbol('one'),s2=Symbol('two');Object.defineProperty(p,'b',{value:1,configurable:true});p[s1]=1;Object.defineProperty(p,'10',{value:1,configurable:true});Object.defineProperty(p,'2',{value:1,configurable:true});Object.defineProperty(p,'a',{value:1,enumerable:true,configurable:true});p[s2]=2;const names=['b','10','2','a'];const keys=Reflect.ownKeys(p);let selected='',count=0;for(let i=0;i<keys.length;i++){const k=keys[i];if(k===s1||k===s2||(typeof k==='string'&&names.includes(k))){if(count++)selected+=',';selected+=typeof k==='symbol'?(k===s1?'s1':'s2'):String(k)}}console.log(selected);\n", "2,10,b,a,s1,s2\n"];
        yield return ["array-accessor-delete-recreate-direct-loop", "const p:any=Array.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 7},configurable:true});Object.defineProperty(p,'erase',{value:1,configurable:true});Object.defineProperty(p,'tail',{value:2,configurable:true});delete p.erase;Object.defineProperty(p,'erase',{value:3,configurable:true});const names=['probe','erase','tail'];const keys=Reflect.ownKeys(p);let selected='',count=0;for(let i=0;i<keys.length;i++){const k=keys[i];if(typeof k==='string'&&names.includes(k)){if(count++)selected+=',';selected+=k}}console.log(selected,gets);\n", "probe,tail,erase 0\n"];
        yield return ["array-deleted-builtin-not-resurrected", "const p:any=Array.prototype;Object.defineProperty(p,'toString',{value:function(){return 'changed'},configurable:true});console.log(Reflect.ownKeys(p).includes('toString'));delete p.toString;Object.preventExtensions(p);console.log(Reflect.ownKeys(p).includes('toString'));\n", "true\nfalse\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReflectKeysObserveOwnedStorageWithoutGetters(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
