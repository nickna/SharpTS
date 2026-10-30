using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CompiledDateConstructorInventoryTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-date-metadata-inventories", """
            const C:any=Date;const names=Object.getOwnPropertyNames(C),keys=Reflect.ownKeys(C);console.log(names.includes('length'),names.includes('name'),names.includes('prototype'),keys.includes('length'),keys.includes('name'),keys.includes('prototype'));
            """, "true true true true true true\n"];
        yield return ["initial-date-intrinsic-order", """
            console.log(Object.getOwnPropertyNames(Date).join(','));console.log(Reflect.ownKeys(Date).join(','));
            """, "length,name,prototype,now,parse,UTC\nlength,name,prototype,now,parse,UTC\n"];
        yield return ["expando-and-accessor-without-get", """
            const C:any=Date;let reads=0;C.extra=1;Object.defineProperty(C,'hidden',{get(){reads++;return 2;},configurable:true});console.log(Object.getOwnPropertyNames(C).join(','),reads,Object.keys(C).join(','));
            """, "length,name,prototype,now,parse,UTC,extra,hidden 0 extra\n"];
        yield return ["deleted-and-recreated-static-order", """
            const C:any=Date;delete C.now;console.log(Object.getOwnPropertyNames(C).join(','));C.now=()=>7;console.log(Object.getOwnPropertyNames(C).join(','),C.now());
            """, "length,name,prototype,parse,UTC\nlength,name,prototype,parse,UTC,now 7\n"];
        yield return ["replaced-static-keeps-initial-position", """
            const C:any=Date;C.now=()=>7;console.log(Object.getOwnPropertyNames(C).join(','),C.now());
            """, "length,name,prototype,now,parse,UTC 7\n"];
        yield return ["metadata-descriptor-coherence", """
            const C:any=Date;for(const key of ['length','name','prototype']){const d=Object.getOwnPropertyDescriptor(C,key)!;console.log(key,Object.hasOwn(C,key),d.value===C[key],d.enumerable,d.configurable);}
            """, "length true true false true\nname true true false true\nprototype true true false false\n"];
        yield return ["same-name-source-class", """
            const C=class Date{static now(){return 7;}};const names=Object.getOwnPropertyNames(C);console.log(names.includes('now'),names.includes('parse'),names.includes('UTC'));
            """, "true false false\n"];
        yield return ["no-date-control", """
            console.log(Object.getOwnPropertyNames({x:1}).join(','));
            """, "x\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OwnNamesUseTheLiveActualDateConstructor(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
