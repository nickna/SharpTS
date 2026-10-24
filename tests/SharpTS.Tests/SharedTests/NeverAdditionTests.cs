using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NeverAdditionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (type, value) in new[] { ("number", "1"), ("string", "'value'"), ("bigint", "1n") })
        foreach (bool left in new[] { false, true })
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [type, value, left, mode];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ThrowOnlyArrowPreservesAdditionEvaluationOrder(string type, string value, bool left, ExecutionMode mode)
    {
        string expression = left ? "fail() + operand()" : "operand() + fail()";
        string source = $$"""
            let trace = '';
            const sentinel: any = {};
            const fail = () => { trace += 'f'; throw sentinel; };
            function operand(): {{type}} { trace += 'v'; return {{value}}; }
            try { const result = {{expression}}; }
            catch (error) { console.log(error === sentinel, trace); }
            """;
        Assert.Equal(left ? "true f\n" : "true vf\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedRejectionAbortsBothSidesOfEachPrimitiveAddition(ExecutionMode mode)
    {
        const string source = """
            const sentinel: any = {};
            async function run(): Promise<void> {
                try { const value = 1 + await Promise.reject(sentinel); } catch (error) { console.log(error === sentinel); }
                try { const value = await Promise.reject(sentinel) + 1; } catch (error) { console.log(error === sentinel); }
                try { const value = 'a' + await Promise.reject(sentinel); } catch (error) { console.log(error === sentinel); }
                try { const value = await Promise.reject(sentinel) + 'a'; } catch (error) { console.log(error === sentinel); }
                try { const value = 1n + await Promise.reject(sentinel); } catch (error) { console.log(error === sentinel); }
                try { const value = await Promise.reject(sentinel) + 1n; } catch (error) { console.log(error === sentinel); }
            }
            run();
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }
}
