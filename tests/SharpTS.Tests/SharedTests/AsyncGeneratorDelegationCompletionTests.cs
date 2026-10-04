using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncGeneratorDelegationCompletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original-async", """
            async function* inner(){yield 2;yield await Promise.resolve(3);return 4;}async function* outer(){const result=yield* inner();yield result+1;}async function run(){let total=0;for await(const n of outer())total+=n;console.log(total);}run();
            """, "10\n" };
        yield return new object[] { "original-sync", """
            function* inner(){yield 2;yield 3;return 4;}async function* outer(){const result=yield* inner();yield result+1;}async function run(){let total=0;for await(const n of outer())total+=n;console.log(total);}run();
            """, "10\n" };
        yield return new object[] { "direct-async", """
            async function* inner(){yield 2;yield 3;return 4;}async function* outer(){return yield* inner();}async function run(){const g=outer();const a=await g.next();const b=await g.next();const c=await g.next();console.log(a.value,a.done,b.value,b.done,c.value,c.done);}run();
            """, "2 false 3 false 4 true\n" };
        yield return new object[] { "direct-sync", """
            function* inner(){yield 2;yield 3;return 4;}async function* outer(){return yield* inner();}async function run(){const g=outer();const a=await g.next();const b=await g.next();const c=await g.next();console.log(a.value,a.done,b.value,b.done,c.value,c.done);}run();
            """, "2 false 3 false 4 true\n" };
        yield return new object[] { "values-control", """
            async function* inner(){yield 2;yield 3;}async function* outer(){yield* inner();}async function run(){let total=0;for await(const n of outer())total+=n;console.log(total);}run();
            """, "5\n" };
        yield return new object[] { "sent-async-cleanup", """
            async function* inner(){try{const sent:any=yield 1;yield sent;return 9;}finally{await Promise.resolve(0);console.log("inner");}}
            async function* outer(){try{const result:any=yield* inner();yield result+1;}finally{console.log("outer");}}
            async function run(){const g=outer();const a=await g.next();console.log(a.value,a.done);const b=await g.next(7);console.log(b.value,b.done);const c=await g.next();console.log(c.value,c.done);console.log((await g.next()).done);}run();
            """, "1 false\n7 false\ninner\n10 false\nouter\ntrue\n" };
        yield return new object[] { "sent-sync-cleanup", """
            function* inner(){try{const sent:any=yield 1;yield sent;return 9;}finally{console.log("inner");}}
            async function* outer(){try{const result:any=yield* inner();yield result+1;}finally{console.log("outer");}}
            async function run(){const g=outer();const a=await g.next();console.log(a.value,a.done);const b=await g.next(7);console.log(b.value,b.done);const c=await g.next();console.log(c.value,c.done);console.log((await g.next()).done);}run();
            """, "1 false\n7 false\ninner\n10 false\nouter\ntrue\n" };
        yield return new object[] { "empty-completion", """
            function* sync(){if(false)yield 0;return 8;}async function* asynchronous(){if(false)yield 0;return await Promise.resolve(9);}
            async function* outer(){const a:any=yield* sync();const b:any=yield* asynchronous();const c:any=yield* [];console.log(a,b,c===undefined);return 10;}
            async function run(){const last=await outer().next();console.log(last.value,last.done);}run();
            """, "8 9 true\n10 true\n" };
        yield return new object[] { "identity-async", """
            async function* inner(value:any){yield 1;return value;}async function* outer(value:any){return yield* inner(value);}
            async function run(){const object:any={tag:8};for(const value of [null,undefined,false,0,"",object]){const g=outer(value);await g.next();const last=await g.next();console.log(last.value===value,last.done);}}run();
            """, "true true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\n" };
        yield return new object[] { "identity-sync", """
            function* inner(value:any){yield 1;return value;}async function* outer(value:any){return yield* inner(value);}
            async function run(){const object:any={tag:8};for(const value of [null,undefined,false,0,"",object]){const g=outer(value);await g.next();const last=await g.next();console.log(last.value===value,last.done);}}run();
            """, "true true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\n" };
        yield return new object[] { "nested-pending", """
            async function* inner(){yield await new Promise<number>(resolve=>setTimeout(()=>resolve(3),1));return 4;}
            async function* middle(){return yield* inner();}async function* outer(){const value:any=yield* middle();return value+1;}
            async function run(){const g=outer();const a=await g.next();const b=await g.next();console.log(a.value,a.done,b.value,b.done);}run();
            """, "3 false 5 true\n" };
        yield return new object[] { "error-async-cleanup", """
            const error:any={tag:8};async function* inner(){try{yield 1;throw error;}finally{console.log("inner");}}
            async function* outer(){try{yield* inner();}catch(e){console.log(e===error);}finally{console.log("outer");}return 9;}
            async function run(){const g=outer();console.log((await g.next()).value);const last=await g.next();console.log(last.value,last.done);}run();
            """, "1\ninner\ntrue\nouter\n9 true\n" };
        yield return new object[] { "error-sync-cleanup", """
            const error:any={tag:8};function* inner(){try{yield 1;throw error;}finally{console.log("inner");}}
            async function* outer(){try{yield* inner();}catch(e){console.log(e===error);}finally{console.log("outer");}return 9;}
            async function run(){const g=outer();console.log((await g.next()).value);const last=await g.next();console.log(last.value,last.done);}run();
            """, "1\ninner\ntrue\nouter\n9 true\n" };
        yield return new object[] { "error-then-ordinary-throw", """
            const first:any={tag:8};const second:any={tag:9};function* inner(){yield 1;throw first;}
            async function* outer(){try{yield* inner();}catch(e){console.log(e===first);yield 2;}finally{console.log("outer");}}
            async function run(){const g=outer();console.log((await g.next()).value);console.log((await g.next()).value);try{await g.throw(second);}catch(e){console.log(e===second);}console.log((await g.next()).done);}run();
            """, "1\ntrue\n2\nouter\ntrue\ntrue\n" };
    }

    public static IEnumerable<object[]> IndependentCases()
    {
        yield return new object[] { "unawaited-return-promise-independent", """
            function* sync(){if(false)yield 0;return 8;}async function* asynchronous(){if(false)yield 0;return Promise.resolve(9);}
            async function* outer(){const a:any=yield* sync();const b:any=yield* asynchronous();const c:any=yield* [];console.log(a,b,c===undefined);return 10;}
            async function run(){const last=await outer().next();console.log(last.value,last.done);}run();
            """, "8 9 true\n10 true\n" };
        yield return new object[] { "direct-return-promise-independent", """
            async function* values(){if(false)yield 0;return Promise.resolve(9);}
            async function run(){const last=await values().next();console.log(last.value,last.done);}run();
            """, "9 true\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void DelegationRetainsCompletionSentValuesAndCleanup(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
