using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GeneratorUsingCleanupTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "generator_close", """
            function* values(){using r={ [Symbol.dispose](){console.log("dispose");} };yield 1;return 2;}const g=values();console.log(g.next().value);console.log(g.return(9).value);
            """, "1\ndispose\n9\n" };
        yield return new object[] { "generator_complete_control", """
            function* values(){using r={ [Symbol.dispose](){console.log("dispose");} };yield 1;return 2;}const g=values();console.log(g.next().value);console.log(g.next().value);
            """, "1\ndispose\n2\n" };
        yield return new object[] { "generator_finally_control", """
            function* values(){try{yield 1;return 2;}finally{console.log("dispose");}}const g=values();console.log(g.next().value);console.log(g.return(9).value);
            """, "1\ndispose\n9\n" };
        yield return new object[] { "exactly-once-and-unstarted", """
            let calls=0;function* values(){using r={[Symbol.dispose](){calls++;}};yield 1;}const a:any=values();console.log(a.return(9).value,calls);const b:any=values();console.log(b.next().value,calls);console.log(b.return(8).value,calls);console.log(b.next().done,b.return(7).value,calls);
            """, "9 0\n1 0\n8 1\ntrue 7 1\n" };
        yield return new object[] { "receiver-and-captured-method", """
            const r:any={value:3,[Symbol.dispose](){console.log(this===r,this.value);}};function* values(){using x=r;yield x.value;}const g:any=values();console.log(g.next().value);r[Symbol.dispose]=function(){console.log("replacement");};r.value=4;console.log(g.return(9).value);
            """, "3\ntrue 4\n9\n" };
        yield return new object[] { "multiple-declarations-lifo", """
            function make(value:number):any{return {value,[Symbol.dispose](){console.log(this.value);}};}function* values(){using a=make(1),b=make(2);yield 3;using c=make(4);yield 5;}const g:any=values();console.log(g.next().value);console.log(g.next().value);console.log(g.return(9).value);
            """, "3\n5\n4\n2\n1\n9\n" };
        yield return new object[] { "nested-scope-natural-completion", """
            function* values(){using a={[Symbol.dispose](){console.log("outer");}};{using b={[Symbol.dispose](){console.log("inner");}};yield 1;}yield 2;}const g:any=values();console.log(g.next().value);console.log(g.next().value);console.log(g.next().done);
            """, "1\ninner\n2\nouter\ntrue\n" };
        yield return new object[] { "injected-throw-identity", """
            const original=new Error("original");function* values(){using a={[Symbol.dispose](){console.log("a");}};{using b={[Symbol.dispose](){console.log("b");}};yield 1;}}const g:any=values();console.log(g.next().value);try{g.throw(original);}catch(e){console.log(e===original);}console.log(g.next().done);
            """, "1\nb\na\ntrue\ntrue\n" };
        yield return new object[] { "disposer-throws-on-return", """
            const original=new Error("dispose");function* values(){using r={[Symbol.dispose](){console.log("dispose");throw original;}};yield 1;}const g:any=values();console.log(g.next().value);try{g.return(9);}catch(e){console.log(e===original);}console.log(g.next().done);
            """, "1\ndispose\ntrue\ntrue\n" };
        yield return new object[] { "nullish-resources", """
            function* values(){using a:any=null;using b:any=undefined;yield 1;return 2;}const g:any=values();console.log(g.next().value);console.log(g.next().value,g.next().done);
            """, "1\n2 true\n" };
        yield return new object[] { "getter-before-suspension", """
            const r:any={value:5};let calls=0;Object.defineProperty(r,Symbol.dispose,{get(){calls++;console.log("get");return function(){console.log(this.value,calls);};}});function* values(){using x=r;console.log("body");yield 1;}const g:any=values();console.log(g.next().value);console.log(g.return(9).value);
            """, "get\nbody\n1\n5 1\n9\n" };
        yield return new object[] { "partial-registration-throws", """
            const original=new Error("get");const r:any={};Object.defineProperty(r,Symbol.dispose,{get(){console.log("get");throw original;}});function* values(){using a={[Symbol.dispose](){console.log("a");}},b=r;yield 1;}const g:any=values();try{g.next();}catch(e){console.log(e===original);}console.log(g.next().done);
            """, "get\na\ntrue\ntrue\n" };
        yield return new object[] { "shadowed-resource-fields", """
            function make(value:number):any{return {value,[Symbol.dispose](){console.log("dispose",this.value);}};}function* values(){using r=make(1);{using r=make(2);yield r.value;}yield r.value;}const g:any=values();console.log(g.next().value);console.log(g.next().value);console.log(g.next().done);
            """, "2\ndispose 2\n1\ndispose 1\ntrue\n" };
        yield return new object[] { "class-generator-and-forof-close", """
            class C{value=7;*values(){using r={value:this.value,[Symbol.dispose](){console.log(this.value);}};yield r.value;yield 8;}}for(const value of new C().values()){console.log(value);break;}console.log("after");
            """, "7\n7\nafter\n" };
        yield return new object[] { "disposer-throws-but-outer-cleans-up", """
            const original=new Error("dispose");function* values(){using a={[Symbol.dispose](){console.log("outer");}},b={[Symbol.dispose](){console.log("inner");throw original;}};yield 1;}const g:any=values();console.log(g.next().value);try{g.return(9);}catch(e){console.log(e===original);}console.log(g.next().done);
            """, "1\ninner\nouter\ntrue\ntrue\n" };
        yield return new object[] { "catch-scope-and-explicit-finally", """
            const original=new Error("injected");function* values(){try{yield 1;}catch(e){using r={[Symbol.dispose](){console.log("catch dispose");}};console.log(e===original);yield 2;}finally{console.log("finally");}}const g:any=values();console.log(g.next().value);console.log(g.throw(original).value);console.log(g.return(9).value);
            """, "1\ntrue\n2\ncatch dispose\nfinally\n9\n" };
        yield return new object[] { "no-yield-generator-cleanup", """
            function* values(){using r={[Symbol.dispose](){console.log("dispose");}};return 7;}const g=values();console.log(g.next().value,g.next().done);
            """, "dispose\n7 true\n" };
        yield return new object[] { "nested-explicit-finally-throw-control", """
            const original=new Error("dispose");function* values(){try{try{yield 1;}finally{console.log("inner");throw original;}}finally{console.log("outer");}}const g:any=values();console.log(g.next().value);try{g.return(9);}catch(e){console.log(e===original);}console.log(g.next().done);
            """, "1\ninner\nouter\ntrue\ntrue\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledGeneratorUsingCleansUpAcrossSuspension(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
