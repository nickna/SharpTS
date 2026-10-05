using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InlineConstructorPrototypeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["""
            function make() { return class {}; }
            const First = make();
            const Second = make();
            console.log(First === Second);
            console.log(Object.getPrototypeOf(new First()) === Object.getPrototypeOf(new Second()));
            """, "false\nfalse\n"];
        yield return ["""
            const events: string[] = [];
            function make() { return class {
                constructor(a: number = 3, b: number = 4) { events.push(a + ":" + b); }
            }; }
            const First = make();
            function target() { events.push("target"); return First; }
            function arg(value: number) { events.push("arg" + value); return value; }
            console.log(Object.getPrototypeOf(new (target())(arg(1), arg(2))) === First.prototype);
            console.log(Object.getPrototypeOf(new (target())()) === First.prototype);
            console.log(events.join(","));
            """, "true\ntrue\ntarget,arg1,arg2,1:2,target,3:4\n"];
        yield return ["""
            class Named {
                value: number;
                constructor(value: number = 7) { this.value = value; }
            }
            const Alias: any = Named;
            let calls = 0;
            function value() { calls++; return 9; }
            console.log(Object.getPrototypeOf(new Alias(value())) === Named.prototype);
            console.log(Object.getPrototypeOf(new Alias()) === Named.prototype);
            console.log(new Alias().value, calls);
            """, "true\ntrue\n7 1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InlineQueriesPreservePrototypeAndEvaluationOrderInVerifiedAssemblies(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
        Assert.Equal(expected, TestHarness.RunCompiledStandalone(source));
    }
}
