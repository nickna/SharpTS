using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CompiledPromiseIntrinsicSpeciesTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-native-species", """
            class P extends Promise<any> {}
            const owner: any = P, species = Symbol.species;
            console.log(owner[species] === owner, Reflect.get(owner, species) === owner,
                species in owner, Reflect.has(owner, species));
            console.log(typeof owner.resolve, "resolve" in owner, Reflect.has(owner, "resolve"));
            """, "true true true true\nfunction true true\n"];
        yield return ["intrinsic-getter-descriptor", """
            const owner:any=Promise,s=Symbol.species,receiver={};const d:any=Object.getOwnPropertyDescriptor(owner,s);console.log(typeof d.get,d.set===undefined,d.enumerable,d.configurable,Object.hasOwn(d,'value'),d.get.call(receiver)===receiver,d.get.call(owner)===owner);
            """, "function true false true false true true\n"];
        yield return ["live-parent-read-and-presence", """
            class P extends Promise<any>{}const owner:any=P,s=Symbol.species,initial=Object.getPrototypeOf(owner);for(const parent of [initial,{},null,Promise,initial]){Object.setPrototypeOf(owner,parent);console.log(owner[s]===owner,Reflect.get(owner,s)===owner,s in owner,Reflect.has(owner,s));}
            """, "true true true true\nfalse false false false\nfalse false false false\ntrue true true true\ntrue true true true\n"];
        yield return ["own-undefined-masks-inherited-getter", """
            class P extends Promise<any>{}const owner:any=P,s=Symbol.species;Object.defineProperty(owner,s,{get:undefined,configurable:true});console.log(owner[s]===undefined,s in owner,Reflect.has(owner,s));delete owner[s];console.log(owner[s]===owner,s in owner);
            """, "true true true\ntrue true\n"];
        yield return ["intrinsic-deletion-and-restoration", """
            const owner:any=Promise,s=Symbol.species,d=Object.getOwnPropertyDescriptor(owner,s)!;class P extends Promise<any>{}const child:any=P;console.log(delete owner[s],s in owner,s in child,child[s]===undefined);Object.defineProperty(owner,s,d);console.log(owner[s]===owner,child[s]===child,s in child);
            """, "true false false true\ntrue true true\n"];
        yield return ["expression-and-generic", """
            const P:any=class extends Promise<any>{};class G<T> extends Promise<T>{}const Q:any=G;for(const owner of [P,Q]){const s=Symbol.species;console.log(owner[s]===owner,Reflect.get(owner,s)===owner,s in owner,Reflect.has(owner,s));}
            """, "true true true true\ntrue true true true\n"];
        yield return ["intrinsic-accessor-receiver", """
            class P extends Promise<any>{}const owner:any=P,s=Symbol.species,receiver={};console.log(Reflect.get(owner,s,receiver)===receiver,Reflect.get(Promise,s,receiver)===receiver);
            """, "true true\n"];
        yield return ["source-own-getter-before-intrinsic", """
            class P extends Promise<any>{static get [Symbol.species]():any{return Promise;}}const owner:any=P;console.log(owner[Symbol.species]===Promise,Reflect.get(owner,Symbol.species)===Promise);
            """, "true true\n"];
        yield return ["source-own-symbol-data-before-intrinsic", """
            class P extends Promise<any>{static [Symbol.species]:any=Promise;}const owner:any=P;console.log(owner[Symbol.species]===Promise,Reflect.get(owner,Symbol.species)===Promise);
            """, "true true\n"];
        yield return ["getter-only-assignment", """
            class P extends Promise<any>{}const owner:any=P,s=Symbol.species;owner[s]=Promise;console.log(owner[s]===owner,Object.hasOwn(owner,s),Reflect.set(owner,s,Promise));function strictWrite(){'use strict';try{owner[s]=Promise;console.log('accepted');}catch(error){console.log(error instanceof TypeError);}}strictWrite();
            """, "true false false\ntrue\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void IntrinsicSpeciesIsAnOrdinaryInheritedAccessor(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
