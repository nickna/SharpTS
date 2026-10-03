using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncMutableCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const values:any={[Symbol.asyncIterator](){let n=0;return {async next(){return {value:++n,done:n>3};}};}};async function run(){let total=0;for await(const n of values)total+=n;console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "bounded", """
            const values:any={[Symbol.asyncIterator](){let n=0;return {async next(){return {value:++n,done:n>3};}};}};async function run(){const it=values[Symbol.asyncIterator]();const a=await it.next();const b=await it.next();const c=await it.next();const d=await it.next();console.log(a.value,a.done,b.value,b.done,c.value,c.done,d.value,d.done);}run();
            """, "1 false 2 false 3 false 4 true\n" };
        yield return new object[] { "outer-counter-control", """
            let n=0;const values:any={[Symbol.asyncIterator](){return {async next(){return {value:++n,done:n>3};}};}};async function run(){let total=0;for await(const v of values)total+=v;console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "factories-siblings", """
            function factory(start:number){let n=start;return {async next(){await Promise.resolve(0);return ++n;},current(){return n;},reset(value:number){n=value;}};}
            async function run(){const a=factory(0);const b=factory(10);console.log(await a.next(),await a.next(),await b.next());console.log(a.current(),b.current());a.reset(20);console.log(await a.next(),b.current());}run();
            """, "1 2 11\n2 11\n21 11\n" };
        yield return new object[] { "async-expression", """
            function factory(){let n=0;const next=async function(){await Promise.resolve(0);return ++n;};return {next,read:()=>n};}
            async function run(){const value=factory();console.log(await value.next(),value.read(),await value.next(),value.read());}run();
            """, "1 1 2 2\n" };
        yield return new object[] { "awaiting-pending", """
            function factory(){let n=0;return {async next(){await new Promise<number>(resolve=>setTimeout(()=>resolve(0),1));n+=2;return n;}};}
            async function run(){const value=factory();console.log(await value.next(),await value.next(),await value.next());}run();
            """, "2 4 6\n" };
        yield return new object[] { "async-siblings", """
            function factory(){let n=0;return {async inc(){return ++n;},async read(){return n;},async add(value:number){await Promise.resolve(0);n+=value;return n;}};}
            async function run(){const value=factory();console.log(await value.inc(),await value.read(),await value.add(5),await value.inc(),await value.read());}run();
            """, "1 1 6 7 7\n" };
        yield return new object[] { "captured-parameter", """
            function factory(n:number){return async()=>++n;}
            async function run(){const a=factory(0);const b=factory(10);console.log(await a(),await a(),await b(),await a(),await b());}run();
            """, "1 2 11 3 12\n" };
        yield return new object[] { "arrow-factory", """
            const factory=(start:number)=>{let n=start;return async()=>++n;};
            async function run(){const a=factory(0);const b=factory(10);console.log(await a(),await a(),await b(),await a());}run();
            """, "1 2 11 3\n" };
        yield return new object[] { "multiple-owners", """
            function factory(){let outer=0;const make=()=>{let inner=10;return async()=>++outer + ++inner;};return make();}
            async function run(){const a=factory();const b=factory();console.log(await a(),await a(),await b(),await a());}run();
            """, "12 14 12 16\n" };
        yield return new object[] { "global-name-collision", """
            let n=100;function factory(){let n=0;return async()=>++n;}
            async function run(){const a=factory();console.log(await a(),await a(),n);}run();
            """, "1 2 100\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void AsyncClosuresShareLexicalStorageAcrossCallsAndAwait(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
