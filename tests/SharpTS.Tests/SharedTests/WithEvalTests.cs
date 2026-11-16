using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class WithEvalTests
{
    [Theory, ModeData]
    public void DirectEvalSeesNestedObjectEnvironmentsAndLexicalMasks(ExecutionMode mode)
    {
        const string source = """
            const x = 1;
            with ({x: 2}) {
                console.log(eval('x'));
                with ({x: 3}) console.log(eval('x'));
                { const x = 4; console.log(eval('x')); }
                console.log(eval('x'));
            }
            console.log(x);
            """;
        Assert.Equal("2\n3\n4\n2\n1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void EvalHonorsUnscopablesWritesAndMethodReceivers(ExecutionMode mode)
    {
        const string source = """
            let x = 1;
            const object = {x: 2, method(){return this === object;}};
            object[Symbol.unscopables] = {x: true};
            with (object) {
                console.log(eval('x'));
                eval('x = 3;');
                object[Symbol.unscopables].x = false;
                eval('x = 4;');
                console.log(eval('method()'));
                console.log(eval('x'));
            }
            console.log(x, object.x);
            """;
        Assert.Equal("1\ntrue\n4\n3 4\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void EvaluatedClosureRetainsLiveWithObject(ExecutionMode mode)
    {
        const string source = """
            const x = 1;
            const object = {x: 2};
            let reader;
            with (object) reader = eval('() => x');
            console.log(reader());
            object.x = 5;
            console.log(reader());
            """;
        Assert.Equal("2\n5\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ObjectEvalOverrideAndArgumentEffectsRetainNormalCallBehavior(ExecutionMode mode)
    {
        const string source = """
            const object = {x: 2, eval(text){console.log(this === object); return text + '!';}};
            with (object) console.log(eval('x'));
            let calls = 0;
            with ({x: 3}) console.log(eval('x', ++calls), calls);
            """;
        Assert.Equal("true\nx!\n3 1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void DynamicallyParsedEvalRetainsActiveWithObject(ExecutionMode mode)
    {
        const string source = """
            const x = 1;
            with ({x: 2}) console.log(eval(String('x')));
            """;
        Assert.Equal("2\n", TestHarness.RunUnchecked(source, mode));
    }
}
