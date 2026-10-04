using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DynamicGeneratorIteratorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "lookup", """
            function* values(){yield 1;}const g:any=values();const method:any=g[Symbol.iterator];console.log(typeof method);
            """, "function\n" };
        yield return new object[] { "original", """
            function* values(){yield 1;const sent:any=yield 2;return sent;}const g:any=values();console.log(g[Symbol.iterator]()===g);const a=g.next();const b=g.next();const c=g.next(9);console.log(a.value,a.done,b.value,b.done,c.value,c.done);
            """, "true\n1 false 2 false 9 true\n" };
        yield return new object[] { "captured", """
            function* values(){yield 1;}const g:any=values();const method:any=g[Symbol.iterator];console.log("before");console.log(method.call(g)===g);console.log("after");
            """, "before\ntrue\nafter\n" };
        yield return new object[] { "next-control", """
            function* values(){yield 1;const sent:any=yield 2;return sent;}const g:any=values();const a=g.next();const b=g.next();const c=g.next(9);console.log(a.value,a.done,b.value,b.done,c.value,c.done);
            """, "1 false 2 false 9 true\n" };
        yield return new object[] { "return-finally", """
            function* values(){try{yield 1;yield 2;}finally{console.log("finally");}}
            const g:any=values(); const method:any=g[Symbol.iterator];
            console.log(method.apply(g,[])===g,method.bind(g)()===g);
            const first=g.next(); console.log(first.value,first.done);
            const result=g.return(7); console.log(result.value,result.done);
            const last=g.next(); console.log(last.value,last.done,g[Symbol.iterator]()===g);
            """, "true true\n1 false\nfinally\n7 true\nundefined true true\n" };
        yield return new object[] { "throw-catch", """
            const error:any={label:"boom"};
            function* values(){try{yield 1;}catch(e){console.log(e===error);yield 2;}finally{console.log("finally");}return 9;}
            const g:any=values(); console.log(g[Symbol.iterator]()===g);
            console.log(g.next().value);
            const caught=g.throw(error); console.log(caught.value,caught.done);
            const last=g.next(); console.log(last.value,last.done);
            """, "true\n1\ntrue\n2 false\nfinally\n9 true\n" };
        yield return new object[] { "throw-identity", """
            function* values(){try{yield 1;}finally{console.log("finally");}}
            const error:any={label:"boom"}; const g:any=values();
            const method:any=g[Symbol.iterator]; console.log(method.call(g)===g,g.next().value);
            try{g.throw(error);}catch(e){console.log(e===error);}
            const last=g.next(); console.log(last.value,last.done,method.call(g)===g);
            """, "true 1\nfinally\ntrue\nundefined true true\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void ComputedIteratorLookupKeepsGeneratorAndLifecycle(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
