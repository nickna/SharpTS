using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncGeneratorInjectedThrowTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            async function* values(){try{yield 1;}catch(e:any){yield e.tag;}finally{console.log("closed");}}async function run(){const g=values();console.log((await g.next()).value);console.log((await g.throw({tag:7})).value);console.log((await g.next()).done);}run();
            """, "1\n7\nclosed\ntrue\n" };
        yield return new object[] { "caller-control", """
            async function* values(){try{yield 1;}catch(e:any){yield e.tag;}finally{console.log("closed");}}async function run(){const g=values();console.log((await g.next()).value);try{const r=await g.throw({tag:7});console.log("resolved",r.value,r.done);}catch(e:any){console.log("rejected",e.tag);}console.log("after");}run();
            """, "1\nresolved 7 false\nafter\n" };
        yield return new object[] { "awaiting-catch", """
            const error:any={tag:8};
            async function* values(){try{yield 1;}catch(e){console.log(e===error);await Promise.resolve(0);yield 2;}finally{await Promise.resolve(0);console.log("closed");}return 9;}
            async function run(){const g=values();console.log((await g.next()).value);const caught=await g.throw(error);console.log(caught.value,caught.done);const last=await g.next();console.log(last.value,last.done);}run();
            """, "1\ntrue\n2 false\nclosed\n9 true\n" };
        yield return new object[] { "uncaught-finally", """
            const error:any={tag:8};
            async function* values(){try{yield 1;}finally{console.log("closed");}}
            async function run(){const g=values();console.log((await g.next()).value);try{await g.throw(error);}catch(e){console.log(e===error);}const last=await g.next();console.log(last.value,last.done);}run();
            """, "1\nclosed\ntrue\nundefined true\n" };
        yield return new object[] { "uncaught-awaiting-finally", """
            const error:any={tag:8};
            async function* values(){try{yield 1;}finally{await Promise.resolve(0);console.log("closed");}}
            async function run(){const g=values();console.log((await g.next()).value);try{await g.throw(error);}catch(e){console.log(e===error);}const last=await g.next();console.log(last.value,last.done);}run();
            """, "1\nclosed\ntrue\nundefined true\n" };
        yield return new object[] { "nullish-falsy", """
            async function* values(){try{yield 1;}catch(e){yield e;}}
            async function run(){for(const error of [null,undefined,false,0,""]){const g=values();await g.next();const caught=await g.throw(error);console.log(caught.value===error,caught.done);console.log((await g.next()).done);}}run();
            """, "true false\ntrue\ntrue false\ntrue\ntrue false\ntrue\ntrue false\ntrue\ntrue false\ntrue\n" };
        yield return new object[] { "nested-catch", """
            const error:any={tag:8};
            async function* values(){try{try{yield 1;}finally{console.log("inner");}}catch(e){console.log(e===error);yield 2;}finally{console.log("outer");}}
            async function run(){const g=values();console.log((await g.next()).value);const caught=await g.throw(error);console.log(caught.value,caught.done);console.log((await g.next()).done);}run();
            """, "1\ninner\ntrue\n2 false\nouter\ntrue\n" };
        yield return new object[] { "throw-from-suspended-catch", """
            const first:any={tag:8};const second:any={tag:9};
            async function* values(){try{yield 1;}catch(e){console.log(e===first);yield 2;}finally{console.log("closed");}}
            async function run(){const g=values();console.log((await g.next()).value);const caught=await g.throw(first);console.log(caught.value,caught.done);try{await g.throw(second);}catch(e){console.log(e===second);}const last=await g.next();console.log(last.value,last.done);}run();
            """, "1\ntrue\n2 false\nclosed\ntrue\nundefined true\n" };
        yield return new object[] { "not-started-completed", """
            const error:any={tag:8};async function* values(){console.log("body");yield 1;}
            async function run(){const g=values();try{await g.throw(error);}catch(e){console.log(e===error);}const last=await g.next();console.log(last.value,last.done);try{await g.throw(error);}catch(e){console.log(e===error);}}run();
            """, "true\nundefined true\ntrue\n" };
    }

    public static IEnumerable<object[]> CompiledCases()
    {
        foreach (var row in Cases()) yield return row;
        yield return new object[] { "next-return-controls", """
            async function* values(){try{const sent:any=yield 1;yield sent;}finally{console.log("closed");}return 0;}
            async function run(){const g=values();console.log((await g.next()).value);const sent=await g.next(7);console.log(sent.value,sent.done);const last=await g.return(9);console.log(last.value,last.done);console.log((await g.next()).done);}run();
            """, "1\n7 false\nclosed\n9 true\ntrue\n" };
    }

    public static IEnumerable<object[]> IndependentCases()
    {
        yield return new object[] { "completed-null-rejection-independent", """
            const error:any={tag:8};async function* values(){console.log("body");yield 1;}
            async function run(){const g=values();try{await g.throw(error);}catch(e){console.log(e===error);}const last=await g.next();console.log(last.value,last.done);try{await g.throw(null);}catch(e){console.log(e===null);}}run();
            """, "true\nundefined true\ntrue\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    public static IEnumerable<object[]> CompiledParityCases() => CompiledCases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void ThrowResumesCatchFinallyAndSettlesWithStepOrOriginalError(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Theory, MemberData(nameof(CompiledParityCases))]
    public void CompiledThrowResumesCatchFinallyAndPreservesNextReturnControls(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
