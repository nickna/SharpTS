using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BoundConstructorAwaitTests
{
    // #1724: constructor values must remain live across awaited continuations.
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[]
        {
            "fulfilled-observed",
            CanonicalSource("""
            function Inner(this:any){this.y=2;}
            async function run(){
                const receiver:any={y:90};
                const bound:any=Inner.bind(receiver);
                await Promise.resolve();
                const instance:any=new bound();
                console.log(instance.y,receiver.y,instance===receiver);
            }
            run().then(()=>console.log("fulfilled"),(error:any)=>console.log("rejected",error.name));
            """),
            "2 90 false\nfulfilled\n"
        };
        yield return new object[]
        {
            "pending-observed",
            CanonicalSource("""
            let release:any;
            const gate:any=new Promise<any>((resolve:any)=>{release=resolve;});
            function Inner(this:any){this.y=2;}
            async function run(){
                const receiver:any={y:90};
                const bound:any=Inner.bind(receiver);
                await gate;
                const instance:any=new bound();
                console.log(instance.y,receiver.y,instance===receiver);
            }
            run().then(()=>console.log("fulfilled"),(error:any)=>console.log("rejected",error.name));
            console.log("waiting");
            release(0);
            """),
            "waiting\n2 90 false\nfulfilled\n"
        };
        yield return new object[]
        {
            "caught-rejection-live-callee",
            CanonicalSource("""
            function Inner(this:any){this.y=2;}
            async function run(){
                const receiver:any={y:90};
                const bound:any=Inner.bind(receiver);
                try{await Promise.reject("stop");}catch(error){console.log(error==="stop");}
                const instance:any=new bound();
                console.log(instance.y,receiver.y,instance===receiver);
            }
            run().then(()=>console.log("fulfilled"),(error:any)=>console.log("rejected",error.name));
            """),
            "true\n2 90 false\nfulfilled\n"
        };
        yield return new object[]
        {
            "awaited-argument-sideeffects",
            CanonicalSource("""
            const events:any[]=[];
            function Value(this:any,a:any,b:any){this.value=a*10+b;}
            function argument(label:any,value:any){events.push(label);return value;}
            async function run(){
                const receiver:any={value:90};
                const bound:any=Value.bind(receiver);
                const instance:any=new bound(argument("first",3),await Promise.resolve(argument("second",4)));
                console.log(instance.value,receiver.value,instance===receiver,events.join(","));
            }
            run().then(()=>console.log("fulfilled"),(error:any)=>console.log("rejected",error.name));
            """),
            "34 90 false first,second\nfulfilled\n"
        };
        yield return new object[]
        {
            "construction-before-await",
            CanonicalSource("""
            function Inner(this:any){this.y=2;}
            async function run(){
                const receiver:any={y:90};
                const bound:any=Inner.bind(receiver);
                const instance:any=new bound();
                console.log("before",instance.y,receiver.y,instance===receiver);
                await Promise.resolve();
                console.log("after",instance.y,receiver.y,instance===receiver);
            }
            run().then(()=>console.log("fulfilled"),(error:any)=>console.log("rejected",error.name));
            """),
            "before 2 90 false\nafter 2 90 false\nfulfilled\n"
        };
    }

    private static string CanonicalSource(string source) =>
        source.Replace("\r\n", "\n") + "\n";

    [Theory, MemberData(nameof(Cases))]
    public void ConstructionPreservesLiveCalleeAndEvaluationOrder(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
