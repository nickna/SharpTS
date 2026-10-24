using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NeverNumericOperatorTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string op in new[] { "-", "*", "/", "%", "**", "<<", ">>", ">>>", "&", "|", "^", "<", "<=", ">", ">=" })
        foreach (var (type, value) in new[] { ("number", "8"), ("bigint", "8n") })
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
        {
            if (op == ">>>" && type == "bigint") continue;
            yield return [op, type, value, mode];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void NeverBinaryOperandsThrowInSourceEvaluationOrder(string op, string type, string value, ExecutionMode mode)
    {
        string source = $$"""
            let trace = '';
            const sentinel: any = {};
            const fail = () => { trace += 'f'; throw sentinel; };
            function operand(): {{type}} { trace += 'v'; return {{value}}; }
            try { const result = operand() {{op}} fail(); }
            catch (error) { console.log(error === sentinel, trace); }
            trace = '';
            try { const result = fail() {{op}} operand(); }
            catch (error) { console.log(error === sentinel, trace); }
            trace = '';
            try { const result = fail() {{op}} fail(); }
            catch (error) { console.log(error === sentinel, trace); }
            """;
        Assert.Equal("true vf\ntrue f\ntrue f\n", TestHarness.Run(source, mode));
    }
}
