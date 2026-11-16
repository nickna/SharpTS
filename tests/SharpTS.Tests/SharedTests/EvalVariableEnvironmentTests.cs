using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EvalVariableEnvironmentTests
{
    [Theory, ModeData]
    public void LoopReadsAndAllWritesFollowTheInstantiatedBinding(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            function run() {
                for (let i = 0; i < 2; i++) {
                    console.log(x);
                    eval('var x = 2;');
                    x += 3;
                    console.log(x++, ++x);
                    x &&= 8;
                    console.log(x);
                }
                console.log(typeof created);
                eval('var created = 7;');
                console.log(typeof created);
            }
            run(); console.log(x);
            """;
        Assert.Equal("10\n5 7\n8\n8\n5 7\n8\nundefined\nnumber\n10\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void DeclarationPreservesPreviouslySelectedOuterAssignment(ExecutionMode mode)
    {
        // Test262 S11.13.1_A6_T1/T2 are the oracle: resolving the LHS precedes eval.
        const string source = """
            function uninitialized() {
                var x = 0;
                var inner = (function(){x = (eval('var x;'), 1); return x;})();
                console.log(inner, x);
            }
            function initialized() {
                var x = 0;
                var inner = (function(){x = (eval('var x = 2;'), 1); return x;})();
                console.log(inner, x);
            }
            uninitialized(); initialized();
            """;
        Assert.Equal("undefined 1\n2 1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ConditionalInstantiationAndRepeatedDeclarationsRetainBindingIdentity(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            function run(enabled) {
                if (enabled) eval('var x = 2;');
                console.log(x);
                x = (eval('var x;'), 3);
                console.log(x);
            }
            run(false); console.log(x);
            run(true); console.log(x);
            """;
        Assert.Equal("10\nundefined\n3\n2\n3\n3\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void EvalVarsBelongToFunctionAndReuseItsExistingParametersAndVars(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            function fresh() {
                { eval('var x = 2;'); }
                console.log(x);
            }
            function existing(x) {
                var local = 8;
                eval('var x; var local;');
                console.log(x, local);
                eval('var x = 4; var local = 9;');
                console.log(x, local);
            }
            fresh(); existing(5); console.log(x);
            """;
        Assert.Equal("2\n5 8\n4 9\n10\n", TestHarness.RunUnchecked(source, mode));
    }
}
