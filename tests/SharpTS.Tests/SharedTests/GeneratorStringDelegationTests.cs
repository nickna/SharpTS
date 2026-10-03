using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GeneratorStringDelegationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            function* values(){yield* [1,2];yield* "a😀";}console.log([...values()].join(","));
            """, "1,2,a,😀\n" };
        yield return new object[] { "numeric", """
            function* values(){yield* "a😀";}const result:any=[...values()];console.log(result.length,result[1].length,result[1].charCodeAt(0));
            """, "2 2 55357\n" };
        yield return new object[] { "array-control", """
            function* values(){yield* [1,2];}console.log([...values()].join(","));
            """, "1,2\n" };
        yield return new object[] { "spread-control", """
            const result:any=[..."a😀"];console.log(result.length,result[1].length,result[1].charCodeAt(0));
            """, "2 2 55357\n" };
        yield return new object[] { "mixed-unpaired", """
            function* values(){yield* "a😀b\uD800c\uDC00𝄞";}
            const result:any=[...values()];
            console.log(result.length);
            console.log(result.map((value:any)=>value.length).join(","));
            console.log(result.map((value:any)=>value.charCodeAt(0)).join(","));
            """, "7\n1,2,1,1,1,1,2\n97,55357,98,55296,99,56320,55348\n" };
        yield return new object[] { "completion", """
            function* inner(){const empty:any=yield* "";console.log(empty===undefined);const result:any=yield* "😀";console.log(result===undefined);return 9;}
            function* outer(){const result:any=yield* inner();return result;}
            const g:any=outer(); const first=g.next();console.log(first.value.length,first.done);
            const last=g.next(7);console.log(last.value,last.done);
            """, "true\n2 false\ntrue\n9 true\n" };
        yield return new object[] { "return-finally", """
            function* values(){try{yield* "😀ab";}finally{console.log("finally");}}
            const g:any=values(); const first=g.next();console.log(first.value.length,first.done);
            const last=g.return(5);console.log(last.value,last.done);
            const after=g.next();console.log(after.value,after.done);
            """, "2 false\nfinally\n5 true\nundefined true\n" };
        yield return new object[] { "operand-exception", """
            const error:any={label:"boom"}; function text():string{throw error;}
            function* values(){try{yield* text();}catch(e){console.log(e===error);}finally{console.log("finally");}return 8;}
            const g:any=values(); const last=g.next();console.log(last.value,last.done);
            """, "true\nfinally\n8 true\n" };
    }

    public static IEnumerable<object[]> IndependentCases()
    {
        yield return new object[] { "throw-finally-independent", """
            function* values(){try{yield* "😀ab";}finally{console.log("finally");}}
            const g:any=values(); const first=g.next();console.log(first.value.length,first.done);
            try{g.throw("boom");}catch(e){console.log(e instanceof TypeError);}
            const after=g.next();console.log(after.value,after.done);
            """, "2 false\nfinally\ntrue\nundefined true\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void StringDelegationKeepsCodePointsCompletionAndAbruptBehavior(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
