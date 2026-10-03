using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UsingLoopTransferTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "loop_control", """
            for(let i=0;i<3;i++){using r={value:i,[Symbol.dispose](){console.log(this.value);}};if(i===0)continue;if(i===1)break;}console.log("after");
            """, "0\n1\nafter\n" };
        yield return new object[] { "loop_finally_control", """
            for(let i=0;i<3;i++){try{if(i===0)continue;if(i===1)break;}finally{console.log(i);}}console.log("after");
            """, "0\n1\nafter\n" };
        yield return new object[] { "multiple-resources-lifo", """
            function make(value:number):any{return {value,[Symbol.dispose](){console.log(this.value);}};}for(let i=0;i<3;i++){using a=make(i*10),b=make(i*10+1);if(i===0)continue;break;}console.log("after");
            """, "1\n0\n11\n10\nafter\n" };
        yield return new object[] { "nested-labeled-transfer", """
            function make(value:string):any{return {value,[Symbol.dispose](){console.log(this.value);}};}outer:for(let i=0;i<3;i++){using a=make("a"+i);for(let j=0;j<2;j++){using b=make("b"+i+j);if(i===0)continue outer;break outer;}}console.log("after");
            """, "b00\na0\nb10\na1\nafter\n" };
        yield return new object[] { "inner-loop-within-using-scope", """
            for(let i=0;i<2;i++){using r={value:i,[Symbol.dispose](){console.log("dispose",this.value);}};for(let j=0;j<3;j++){if(j===0)continue;if(j===2)break;console.log(i,j);}console.log("body",i);}console.log("after");
            """, "0 1\nbody 0\ndispose 0\n1 1\nbody 1\ndispose 1\nafter\n" };
        yield return new object[] { "while-and-do-transfers", """
            let i=0;while(i<3){using r={value:i,[Symbol.dispose](){console.log(this.value);}};i++;if(i===1)continue;break;}let j=0;do{using r={value:j+10,[Symbol.dispose](){console.log(this.value);}};j++;if(j===1)continue;break;}while(j<3);console.log("after");
            """, "0\n1\n10\n11\nafter\n" };
        yield return new object[] { "nested-block-function-return", """
            function work():number{for(let i=0;i<3;i++){{using r={value:i,[Symbol.dispose](){console.log(this.value);}};if(i===0)continue;return 7;}}return 8;}console.log(work());
            """, "0\n1\n7\n" };
        yield return new object[] { "forof-transfer", """
            for(const value of [0,1,2]){using r={value,[Symbol.dispose](){console.log(this.value);}};if(value===0)continue;break;}console.log("after");
            """, "0\n1\nafter\n" };
        yield return new object[] { "captured-method-on-transfer", """
            for(let i=0;i<2;i++){const r:any={value:i,[Symbol.dispose](){console.log(this.value);}};using x=r;r[Symbol.dispose]=function(){console.log("replacement");};if(i===0)continue;break;}console.log("after");
            """, "0\n1\nafter\n" };
        yield return new object[] { "unreached-later-registration", """
            function make(value:string):any{return {value,[Symbol.dispose](){console.log(this.value);}};}for(let i=0;i<3;i++){using a=make("a"+i);if(i===1)continue;using b=make("b"+i);console.log("body",i);}console.log("after");
            """, "body 0\nb0\na0\na1\nbody 2\nb2\na2\nafter\n" };
        yield return new object[] { "acquisition-failure-after-earlier-iteration", """
            function make(value:string):any{return {value,[Symbol.dispose](){console.log(this.value);}};}function second(i:number):any{if(i===1)throw new Error("get");return make("b"+i);}for(let i=0;i<3;i++){try{{using a=make("a"+i),b=second(i),c=make("c"+i);console.log("body",i);}}catch(e){console.log(e.message);}}console.log("after");
            """, "body 0\nc0\nb0\na0\na1\nget\nbody 2\nc2\nb2\na2\nafter\n" };
        yield return new object[] { "disposal-failure-cancels-transfer", """
            const original=new Error("dispose");for(let i=0;i<2;i++){try{{using r={[Symbol.dispose](){console.log("dispose",i);throw original;}};if(i===0)continue;break;}}catch(e){console.log(e===original);}}console.log("after");
            """, "dispose 0\ntrue\ndispose 1\ntrue\nafter\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledUsingTransfersRunCleanup(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
