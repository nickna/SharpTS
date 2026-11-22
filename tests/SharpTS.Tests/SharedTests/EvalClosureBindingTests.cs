using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EvalClosureBindingTests
{
    [Theory, ModeData]
    public void NestedEvalInheritsStrictnessFromItsEvalCaller(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            eval('"use strict"; eval("var x = 3;");');
            console.log(x);
            """;
        Assert.Equal("10\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ClosuresCreatedInsideEvalCaptureTheSameCallerBinding(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            function make() {
                const read = eval('() => x');
                eval('var x = 2;');
                const write = eval('(value) => { x = value; }');
                return [read, write];
            }
            const methods = make();
            console.log(methods[0](), x);
            methods[1](7);
            console.log(methods[0](), x);
            """;
        Assert.Equal("2 10\n7 10\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ClosuresCreatedBeforeAndAfterEvalShareTheLiveBinding(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            function make(run) {
                const before = () => x;
                console.log(before());
                if (run) eval('var x = 2;');
                const after = function() { return x; };
                const write = value => { x = value; };
                return [before, after, write];
            }
            const a = make(true), b = make(true), c = make(false);
            console.log(a[0](), a[1](), b[0](), c[0](), x);
            a[2](7);
            console.log(a[0](), a[1](), b[0](), c[0](), x);
            """;
        Assert.Equal("10\n10\n10\n2 2 2 10 10\n7 7 2 10 10\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void EvalClosureWritesAndUpdatesRetainOneCell(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            function make() {
                function read() { return x; }
                eval('var x = 2;');
                return [read, () => ++x, () => x += 3, () => x &&= 9, () => typeof x];
            }
            const methods = make();
            console.log(methods[0](), methods[1](), methods[2](), methods[3](), methods[4](), methods[0](), x);
            """;
        Assert.Equal("2 3 6 9 number 9 10\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void OrdinaryShadowsAndStrictClosuresResolveTheirOwnEnvironments(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            function make() {
                eval('var x = 2;');
                return [x => () => x, () => { let x = 4; return () => x; }, () => { 'use strict'; return x; }];
            }
            const methods = make();
            console.log(methods[0](5)(), methods[1]()(), methods[2](), x);
            """;
        Assert.Equal("5 4 2 10\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void NestedEvalScopesFallThroughUntilEachBindingIsInstantiated(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            function outer() {
                eval('var x = 2;');
                return function inner(run) {
                    const read = () => x;
                    console.log(read());
                    if (run) eval('var x = 5;');
                    x += 1;
                    return read;
                };
            }
            const inner = outer();
            const first = inner(false), second = inner(true);
            console.log(first(), second(), x);
            """;
        Assert.Equal("2\n3\n3 6 10\n", TestHarness.RunUnchecked(source, mode));
    }
}
