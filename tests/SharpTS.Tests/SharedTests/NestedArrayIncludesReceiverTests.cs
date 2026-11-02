using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NestedArrayIncludesReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-string-stored-key-order", "const p:any=String.prototype;const s1=Symbol('one'),s2=Symbol('two');Object.defineProperty(p,'b',{value:1,configurable:true});p[s1]=1;Object.defineProperty(p,'10',{value:1,configurable:true});Object.defineProperty(p,'2',{value:1,configurable:true});Object.defineProperty(p,'a',{value:1,enumerable:true,configurable:true});p[s2]=2;const names=['b','10','2','a'];console.log(Reflect.ownKeys(p).filter(k=>k===s1||k===s2||(typeof k==='string'&&names.includes(k))).map(k=>k===s1?'s1':k===s2?'s2':k).join(','));\n", "2,10,b,a,s1,s2\n"];
        yield return ["original-string-accessor-delete-recreate", "const p:any=String.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 7},configurable:true});Object.defineProperty(p,'erase',{value:1,configurable:true});Object.defineProperty(p,'tail',{value:2,configurable:true});delete p.erase;Object.defineProperty(p,'erase',{value:3,configurable:true});const names=['probe','erase','tail'];console.log(Reflect.ownKeys(p).filter(k=>typeof k==='string'&&names.includes(k)).join(','),gets);\n", "probe,tail,erase 0\n"];
        yield return ["original-number-stored-key-order", "const p:any=Number.prototype;const s1=Symbol('one'),s2=Symbol('two');Object.defineProperty(p,'b',{value:1,configurable:true});p[s1]=1;Object.defineProperty(p,'10',{value:1,configurable:true});Object.defineProperty(p,'2',{value:1,configurable:true});Object.defineProperty(p,'a',{value:1,enumerable:true,configurable:true});p[s2]=2;const names=['b','10','2','a'];console.log(Reflect.ownKeys(p).filter(k=>k===s1||k===s2||(typeof k==='string'&&names.includes(k))).map(k=>k===s1?'s1':k===s2?'s2':k).join(','));\n", "2,10,b,a,s1,s2\n"];
        yield return ["original-number-accessor-delete-recreate", "const p:any=Number.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 7},configurable:true});Object.defineProperty(p,'erase',{value:1,configurable:true});Object.defineProperty(p,'tail',{value:2,configurable:true});delete p.erase;Object.defineProperty(p,'erase',{value:3,configurable:true});const names=['probe','erase','tail'];console.log(Reflect.ownKeys(p).filter(k=>typeof k==='string'&&names.includes(k)).join(','),gets);\n", "probe,tail,erase 0\n"];
        yield return ["original-boolean-stored-key-order", "const p:any=Boolean.prototype;const s1=Symbol('one'),s2=Symbol('two');Object.defineProperty(p,'b',{value:1,configurable:true});p[s1]=1;Object.defineProperty(p,'10',{value:1,configurable:true});Object.defineProperty(p,'2',{value:1,configurable:true});Object.defineProperty(p,'a',{value:1,enumerable:true,configurable:true});p[s2]=2;const names=['b','10','2','a'];console.log(Reflect.ownKeys(p).filter(k=>k===s1||k===s2||(typeof k==='string'&&names.includes(k))).map(k=>k===s1?'s1':k===s2?'s2':k).join(','));\n", "2,10,b,a,s1,s2\n"];
        yield return ["original-boolean-accessor-delete-recreate", "const p:any=Boolean.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 7},configurable:true});Object.defineProperty(p,'erase',{value:1,configurable:true});Object.defineProperty(p,'tail',{value:2,configurable:true});delete p.erase;Object.defineProperty(p,'erase',{value:3,configurable:true});const names=['probe','erase','tail'];console.log(Reflect.ownKeys(p).filter(k=>typeof k==='string'&&names.includes(k)).join(','),gets);\n", "probe,tail,erase 0\n"];
        yield return ["original-array-stored-key-order", "const p:any=Array.prototype;const s1=Symbol('one'),s2=Symbol('two');Object.defineProperty(p,'b',{value:1,configurable:true});p[s1]=1;Object.defineProperty(p,'10',{value:1,configurable:true});Object.defineProperty(p,'2',{value:1,configurable:true});Object.defineProperty(p,'a',{value:1,enumerable:true,configurable:true});p[s2]=2;const names=['b','10','2','a'];console.log(Reflect.ownKeys(p).filter(k=>k===s1||k===s2||(typeof k==='string'&&names.includes(k))).map(k=>k===s1?'s1':k===s2?'s2':k).join(','));\n", "2,10,b,a,s1,s2\n"];
        yield return ["original-array-accessor-delete-recreate", "const p:any=Array.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 7},configurable:true});Object.defineProperty(p,'erase',{value:1,configurable:true});Object.defineProperty(p,'tail',{value:2,configurable:true});delete p.erase;Object.defineProperty(p,'erase',{value:3,configurable:true});const names=['probe','erase','tail'];console.log(Reflect.ownKeys(p).filter(k=>typeof k==='string'&&names.includes(k)).join(','),gets);\n", "probe,tail,erase 0\n"];
        yield return ["minimal-descriptor-filter", "Object.defineProperty({},'unused',{value:1});const names=['b','a'];const out=['x','b','y','a'].filter(k=>names.includes(k));console.log(out[0],out[1],out.join(','));", "b a b,a\n"];
        yield return ["map-membership", "Object.defineProperty({},'unused',{value:1});const names=['b','a'];console.log(['x','b','y','a'].map(k=>names.includes(k)).join(','));", "false,true,false,true\n"];
        yield return ["foreach-membership", "Object.defineProperty({},'unused',{value:1});const names=['b','a'];let log='';['x','b','y','a'].forEach(k=>{log+=k+':'+names.includes(k)+','});console.log(log);", "x:false,b:true,y:false,a:true,\n"];
        yield return ["some-short-circuit", "Object.defineProperty({},'unused',{value:1});const names=['b','a'];let seen='';console.log(['x','b','y','a'].some(k=>{seen+=k;return names.includes(k)}),seen);", "true xb\n"];
        yield return ["every-short-circuit", "Object.defineProperty({},'unused',{value:1});const names=['b','a'];let seen='';console.log(['b','x','a'].every(k=>{seen+=k;return names.includes(k)}),seen);", "false bx\n"];
        yield return ["omitted-search-null-control", "Object.defineProperty({},'unused',{value:1});const names:any=[undefined];console.log([1,2].map(k=>String(names.includes())+':'+names.includes(null)).join(','));", "true:false,true:false\n"];
        yield return ["holes-as-undefined", "Object.defineProperty({},'unused',{value:1});const names:any[]=[,];console.log(['a','b'].map(k=>names.includes(undefined)).join(','));", "true,true\n"];
        yield return ["nested-from-index", "Object.defineProperty({},'unused',{value:1});const names=['b','a'];console.log(['x','b','a'].map(k=>String(names.includes(k,1))+':'+names.includes(k,-2)).join(','));", "false:false,false:true,true:true\n"];
        yield return ["abrupt-index-restores-outer", "Object.defineProperty({},'unused',{value:1});const names=['needle'];const marker={};let caught=0;const out=['x','b','y','a'].filter(k=>{try{names.includes(k,{valueOf(){throw marker}} as any)}catch(e){if(e===marker)caught++}return true});console.log(out.join(','),caught,names.includes('needle'));", "x,b,y,a 4 true\n"];
        yield return ["nested-index-coercion", "Object.defineProperty({},'unused',{value:1});const names=['b','a'],other=['x'];let calls=0;const out=['x','b','y','a'].filter(k=>names.includes(k,{valueOf(){calls++;return other.includes(k)?1:0}} as any));console.log(out.join(','),calls);", "b,a 4\n"];
        yield return ["prototype-replacement-live", "Object.defineProperty({},'unused',{value:1});let calls=0;const p:any=Array.prototype;const original=p.includes;p.includes=function(k:any){calls++;return k==='a'};console.log(['x','b','a'].filter(k=>['b'].includes(k)).join(','),calls);p.includes=original;", "a 3\n"];
        yield return ["direct-path-control", "const names=['b','a'];console.log(['x','b','y','a'].filter(k=>names.includes(k)).join(','));", "b,a\n"];
        yield return ["dynamic-inner-strings", "Object.defineProperty({},'unused',{value:1});const names:any=['b','a'];console.log(['x','b','y','a'].map(k=>names.includes(k)).join(','));", "false,true,false,true\n"];
        yield return ["dynamic-inner-undefined", "Object.defineProperty({},'unused',{value:1});const names:any=[undefined];console.log([1,2].map(k=>String(names[0]===undefined)+':'+names.includes(undefined)+':'+names.includes(null)).join(','));", "true:true:false,true:true:false\n"];
        yield return ["nested-borrowed-getter-receiver", "Object.defineProperty({},'unused',{value:1});let gets=0;const receiver:any={length:1,get 0(){gets++;return this===receiver?'needle':'wrong'}};const includes:any=Array.prototype.includes;console.log(['outer'].map(()=>Array.prototype.includes.call(receiver,'needle')).join(','),gets);console.log(['outer'].map(()=>includes.call(receiver,'needle')).join(','),gets);", "true 1\ntrue 2\n"];
        yield return ["generator-any-inner", "Object.defineProperty({},'unused',{value:1});const names:any=['b','a'];function* check(k:string){yield names.includes(k)}console.log(['x','b','y','a'].map(k=>check(k).next().value).join(','));", "false,true,false,true\n"];
        yield return ["async-prefix-any-inner", "Object.defineProperty({},'unused',{value:1});const names:any=['b','a'];async function check(k:string){return names.includes(k)}async function run(){console.log((await Promise.all(['x','b','y','a'].map(k=>check(k)))).join(','))}void run();", "false,true,false,true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void NestedIncludesUsesItsOwnReceiver(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
