using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseDetachedSpeciesTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-declaration", """
            class P extends Promise<any> {}
            const owner: any = P, initial = Object.getPrototypeOf(owner);
            const source: any = new P(resolve => resolve(7));
            for (const parent of [{ marker: 1 }, null, initial]) {
                Object.setPrototypeOf(owner, parent);
                const result: any = source.then();
                console.log(result instanceof P, result instanceof Promise);
            }
            """, "false true\nfalse true\ntrue true\n"];
        yield return ["evaluated-expression", """
            const P=class extends Promise<any> {};
            const owner: any = P, initial = Object.getPrototypeOf(owner);
            const source: any = new P(resolve => resolve(7));
            for (const parent of [{ marker: 1 }, null, initial]) {
                Object.setPrototypeOf(owner, parent);
                const result: any = source.then();
                console.log(result instanceof P, result instanceof Promise);
            }
            """, "false true\nfalse true\ntrue true\n"];
        yield return ["inherited-live-chain", """
            class P extends Promise<any>{}class Q extends P{}const owner:any=P,initial=Object.getPrototypeOf(owner),source:any=new Q(resolve=>resolve(7));for(const parent of [{},null,initial]){Object.setPrototypeOf(owner,parent);const result:any=source.then();console.log(result instanceof Q,result instanceof P,result instanceof Promise);}
            """, "false false true\nfalse false true\ntrue true true\n"];
        yield return ["own-species-precedes-detached-parent", """
            const events:string[]=[];class P extends Promise<any>{static get [Symbol.species]():any{events.push('own');return Promise;}}const owner:any=P,source:any=new P(resolve=>resolve(7));Object.setPrototypeOf(owner,null);const result:any=source.then();console.log(result instanceof P,result instanceof Promise,events.join(','));
            """, "false true own\n"];
        yield return ["custom-parent-getter-receiver", """
            let reads=0,same=false;class P extends Promise<any>{}const owner:any=P,source:any=new P(resolve=>resolve(7));const parent:any={get [Symbol.species](){reads++;same=this===owner;return Promise;}};Object.setPrototypeOf(owner,parent);const result:any=source.then();console.log(result instanceof P,result instanceof Promise,reads,same);
            """, "false true 1 true\n"];
        yield return ["abrupt-species-before-reaction", """
            const marker={},events:string[]=[];class P extends Promise<any>{}const owner:any=P,source:any=new P(resolve=>resolve(7));Object.setPrototypeOf(owner,{get [Symbol.species](){events.push('get');throw marker;}});try{source.then(()=>events.push('reaction'));}catch(error){console.log(error===marker,events.join(','));}
            """, "true get\n"];
        yield return ["invalid-custom-species", """
            class P extends Promise<any>{}const owner:any=P,source:any=new P(resolve=>resolve(7));Object.setPrototypeOf(owner,{[Symbol.species]:1});try{source.then();console.log('accepted');}catch(error){console.log(error instanceof TypeError);}Object.setPrototypeOf(owner,null);const result:any=source.then();console.log(result instanceof P,result instanceof Promise);
            """, "true\nfalse true\n"];
        yield return ["generic-constructor-live-parent", """
            class P<T> extends Promise<T> {}
            const owner: any = P, initial = Object.getPrototypeOf(owner);
            const source: any = new P<number>(resolve => resolve(7));
            for (const parent of [{ marker: 1 }, null, initial]) {
                Object.setPrototypeOf(owner, parent);
                const result: any = source.then();
                console.log(result instanceof P, result instanceof Promise);
            }
            """, "false true\nfalse true\ntrue true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReactionsObserveTheSelectedConstructorPrototype(string name, string source, string expected, ExecutionMode mode)
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
