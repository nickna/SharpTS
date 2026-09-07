using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ArrayLiteralConstructionTests
{
    [Theory, ModeData]
    public void LiteralConstruction_PreservesEvaluationOrderAndFreshIdentity(ExecutionMode mode)
    {
        const string source = """
            let log: string = "";
            function value(n: number): number { log = log + n; return n; }
            function make(): any[] { return [value(1), "two", value(3), true]; }
            const a = make(); const b = make();
            console.log(log, a.length, a[0], a[1], a[2], a[3], a === b);
            a[0] = 9;
            console.log(b[0]);
            try { const c = [value(4), (() => { throw new Error("stop"); })(), value(5)]; }
            catch (error) { console.log(log); }
            const holes = [, 1, ,];
            console.log(holes.length, 0 in holes, 1 in holes, 2 in holes);
            """;
        Assert.Equal("1313 4 1 two 3 true false\n1\n13134\n3 false true false\n", TestHarness.Run(source, mode));
        if (mode == ExecutionMode.Compiled) Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Theory, ModeData]
    public void LiteralConstruction_DoesNotInvokeReplacedPushAndSupportsSuspension(ExecutionMode mode)
    {
        const string source = """
            Array.prototype.push = function(...items: any[]): number { throw new Error("push called"); };
            const a = [1, "two", true];
            console.log(a.length, a[1]);
            async function run(): Promise<void> {
                const b = [await Promise.resolve(3), await Promise.resolve(4)];
                console.log(b.length, b[0], b[1]);
            }
            run();
            """;
        Assert.Equal("3 two\n2 3 4\n", TestHarness.Run(source, mode));
    }
}
