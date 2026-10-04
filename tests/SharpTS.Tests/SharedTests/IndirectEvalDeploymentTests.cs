using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class IndirectEvalDeploymentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const holder:any={evaluate:eval};const text=String("1+2");console.log(holder.evaluate(text));
            """, "3\n" };
        yield return new object[] { "direct-dynamic", """
            const text=String("1+2");console.log(eval(text));
            """, "3\n" };
        yield return new object[] { "alias", """
            const evaluate:any=eval;const text=String("1+2");console.log(evaluate(text));
            """, "3\n" };
        yield return new object[] { "global-property", """
            const evaluate:any=globalThis.eval;const text=String("1+2");console.log(evaluate(text));
            """, "3\n" };
        yield return new object[] { "global-computed", """
            const evaluate:any=globalThis["eval"];const text=String("1+2");console.log(evaluate(text));
            """, "3\n" };
        yield return new object[] { "global-dynamic-key", """
            const key=String("eval");const evaluate:any=globalThis[key];const text=String("1+2");console.log(evaluate(text));
            """, "3\n" };
        yield return new object[] { "generator", """
            function* run(){const holder:any={evaluate:eval};yield holder.evaluate(String("1+2"));}console.log(run().next().value);
            """, "3\n" };
        yield return new object[] { "async", """
            async function run(){const holder:any={evaluate:eval};console.log(holder.evaluate(String("1+2")));}run();
            """, "3\n" };
        yield return new object[] { "async-arrow", """
            const run=async()=>{const holder:any={evaluate:eval};console.log(holder.evaluate(String("1+2")));};run();
            """, "3\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void ValueFormEvalEvaluatesDynamicSource(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    public static IEnumerable<object[]> StandaloneCases()
    {
        yield return new object[] { "non-string", """
            const holder:any={evaluate:eval};const value:any={id:7};console.log(holder.evaluate(value)===value,holder.evaluate(42));
            """, "true 42\n", true };
        yield return new object[] { "standalone-missing", """
            const holder:any={evaluate:eval};const text=String("1+2");try{console.log(holder.evaluate(text));}catch(e:any){console.log(String(e.message).includes("SharpTS runtime not present"));}
            """, "true\n", true };
        yield return new object[] { "direct-static", """
            function run(){const local=7;return eval("local+1");}console.log(run());
            """, "8\n", false };
    }
}
