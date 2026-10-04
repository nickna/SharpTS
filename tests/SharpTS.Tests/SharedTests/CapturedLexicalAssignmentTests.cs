using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CapturedLexicalAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "lexical_assignment", """
            function outer(){const set=(v:number)=>{x=v;};try{set(1);}catch(e){console.log(e.name);}let x=2;console.log(x);set(3);console.log(x);}outer();
            """, "ReferenceError\n2\n3\n" };
        yield return new object[] { "lexical_read", """
            function outer(){const get=()=>x;try{get();}catch(e){console.log(e.name);}let x:any=undefined;console.log(get()===undefined);x=4;console.log(get());}outer();
            """, "ReferenceError\ntrue\n4\n" };
        yield return new object[] { "lexical_typeof", """
            function outer(){const get=()=>typeof x;try{get();}catch(e){console.log(e.name);}let x:any=undefined;console.log(get());}outer();
            """, "ReferenceError\nundefined\n" };
        yield return new object[] { "lexical_nested", """
            let x="outer";function outer(){const get=()=>()=>x;try{get()();}catch(e){console.log(e.name);}let x="inner";console.log(get()());}outer();console.log(x);
            """, "ReferenceError\ninner\nouter\n" };
        yield return new object[] { "nested-setter", """
            function outer(){const make=()=>()=>{x=3;};const set=make();try{set();}catch(e){console.log(e.name);}let x=2;set();console.log(x);}outer();
            """, "ReferenceError\n3\n" };
        yield return new object[] { "rhs-before-reference-error", """
            function outer(){let log="";function rhs(){log+="rhs";return 1;}const set=()=>{x=rhs();};try{set();}catch(e){console.log(e.name,log);}let x=2;console.log(x);}outer();
            """, "ReferenceError rhs\n2\n" };
        yield return new object[] { "rhs-throw-wins", """
            function outer(){function rhs():number{throw new Error("rhs");}const set=()=>{x=rhs();};try{set();}catch(e){console.log(e.name,e.message);}let x=2;console.log(x);}outer();
            """, "Error rhs\n2\n" };
        yield return new object[] { "declaration-without-initializer", """
            function outer(){const set=(v:number)=>{x=v;};try{set(1);}catch(e){console.log(e.name);}let x:number|undefined;console.log(x===undefined);set(3);console.log(x);}outer();
            """, "ReferenceError\ntrue\n3\n" };
        yield return new object[] { "assignment-expression-result", """
            function outer(){const set=(v:number)=>x=v;try{set(1);}catch(e){console.log(e.name);}let x=2;console.log(set(3),x);}outer();
            """, "ReferenceError\n3 3\n" };
        yield return new object[] { "var-control", """
            function outer(){var x:any;const set=(v:number)=>{x=v;};set(1);console.log(x);x=2;console.log(x);set(3);console.log(x);}outer();
            """, "1\n2\n3\n" };
        yield return new object[] { "shadowed-binding", """
            let x=9;function outer(){const set=()=>{x=1;};try{set();}catch(e){console.log(e.name);}let x=2;console.log(x);}outer();console.log(x);
            """, "ReferenceError\n2\n9\n" };
        yield return new object[] { "initializer-reentry", """
            function outer(){const set=(v:number)=>x=v;let x=set(1);console.log(x);}try{outer();}catch(e){console.log(e.name);}try{outer();}catch(e){console.log(e.name);}
            """, "ReferenceError\nReferenceError\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CapturedLetAssignmentsCheckInitializationAfterTheRhs(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
