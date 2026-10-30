using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CompiledPromiseConstructorInventoryTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-promise-metadata-inventories", """
            const C:any=Promise;const names=Object.getOwnPropertyNames(C),keys=Reflect.ownKeys(C);console.log(names.includes('length'),names.includes('name'),names.includes('prototype'),keys.includes('length'),keys.includes('name'),keys.includes('prototype'));
            """, "true true true true true true\n"];
        yield return ["initial-standard-promise-order", """
            console.log(Object.getOwnPropertyNames(Promise).filter(key=>key!=='allKeyed'&&key!=='allSettledKeyed').join(','));
            """, "length,name,prototype,all,allSettled,any,race,resolve,reject,withResolvers\n"];
        yield return ["expando-and-species-without-get", """
            const C:any=Promise;let reads=0;C.extra=1;Object.defineProperty(C,'hidden',{get(){reads++;return 2;},configurable:true});Object.defineProperty(C,Symbol.species,{get(){reads++;return C;},configurable:true});console.log(Object.getOwnPropertyNames(C).filter(key=>key!=='allKeyed'&&key!=='allSettledKeyed').join(','),reads,Object.keys(C).join(','),Reflect.ownKeys(C).includes(Symbol.species));
            """, "length,name,prototype,all,allSettled,any,race,resolve,reject,withResolvers,extra,hidden 0 extra true\n"];
        yield return ["deleted-and-recreated-static-order", """
            const C:any=Promise;delete C.resolve;console.log(Object.getOwnPropertyNames(C).filter(key=>key!=='allKeyed'&&key!=='allSettledKeyed').join(','));C.resolve=()=>7;console.log(Object.getOwnPropertyNames(C).filter(key=>key!=='allKeyed'&&key!=='allSettledKeyed').join(','),C.resolve());
            """, "length,name,prototype,all,allSettled,any,race,reject,withResolvers\nlength,name,prototype,all,allSettled,any,race,reject,withResolvers,resolve 7\n"];
        yield return ["replaced-static-keeps-initial-position", """
            const C:any=Promise;C.resolve=()=>7;console.log(Object.getOwnPropertyNames(C).filter(key=>key!=='allKeyed'&&key!=='allSettledKeyed').join(','),C.resolve());
            """, "length,name,prototype,all,allSettled,any,race,resolve,reject,withResolvers 7\n"];
        yield return ["metadata-descriptor-coherence", """
            const C:any=Promise;for(const key of ['length','name','prototype']){const d=Object.getOwnPropertyDescriptor(C,key)!;console.log(key,Object.hasOwn(C,key),d.value===C[key],d.enumerable,d.configurable);}
            """, "length true true false true\nname true true false true\nprototype true true false false\n"];
        yield return ["same-name-source-class", """
            const C=class Promise{static resolve(){return 7;}};console.log(Object.getOwnPropertyNames(C).join(','));
            """, "length,name,prototype,resolve\n"];
        yield return ["static-accessor-without-get", """
            const C:any=Promise;let reads=0;Object.defineProperty(C,'resolve',{get(){reads++;return ()=>7;},configurable:true});console.log(Object.getOwnPropertyNames(C).includes('resolve'),Reflect.ownKeys(C).includes('resolve'),reads);
            """, "true true 0\n"];
        yield return ["no-promise-control", """
            console.log(Object.getOwnPropertyNames({x:1}).join(','));
            """, "x\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OwnNamesUseTheLiveActualPromiseConstructor(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
