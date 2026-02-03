using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Compiler-specific generator tests that verify the iterator protocol implementation.
/// These tests focus on .next() protocol, IteratorResult structure, and features
/// that may behave differently between interpreter and compiler.
/// </summary>
public class GeneratorCompilerTests
{
    #region Iterator Protocol (.next())

    [Fact]
    public void Generator_BasicYield_ReturnsValues()
    {
        var source = """
            function* counter() {
                yield 1;
                yield 2;
                yield 3;
            }

            let gen = counter();
            console.log(gen.next().value);
            console.log(gen.next().value);
            console.log(gen.next().value);
            console.log(gen.next().done);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\ntrue\n", output);
    }

    [Fact]
    public void Generator_EmptyGenerator_ReturnsDoneImmediately()
    {
        var source = """
            function* empty() {}

            let gen = empty();
            let result = gen.next();
            console.log(result.done);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Generator_IteratorResult_HasCorrectStructure()
    {
        var source = """
            function* single() {
                yield 42;
            }

            let gen = single();
            let first = gen.next();
            let second = gen.next();

            console.log("First value:", first.value);
            console.log("First done:", first.done);
            console.log("Second done:", second.done);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("First value: 42\nFirst done: false\nSecond done: true\n", output);
    }

    [Fact]
    public void Generator_MultipleInstances_IndependentState()
    {
        var source = """
            function* counter() {
                yield 1;
                yield 2;
                yield 3;
            }

            let gen1 = counter();
            let gen2 = counter();

            console.log(gen1.next().value);
            console.log(gen2.next().value);
            console.log(gen1.next().value);
            console.log(gen2.next().value);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n1\n2\n2\n", output);
    }

    #endregion

    #region Yield* with Collections

    [Fact]
    public void Generator_YieldStarString_DelegatesCharacters()
    {
        var source = """
            function* chars() {
                yield* "hi";
            }

            for (let c of chars()) {
                console.log(c);
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("h\ni\n", output);
    }

    [Fact]
    public void Generator_YieldStarMap_DelegatesEntries()
    {
        var source = """
            function* mapIter() {
                let m = new Map<string, number>();
                m.set("a", 1);
                m.set("b", 2);
                yield* m;
            }

            for (let pair of mapIter()) {
                console.log(pair[0] + ":" + pair[1]);
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("a:1\nb:2\n", output);
    }

    [Fact]
    public void Generator_YieldStarSet_DelegatesValues()
    {
        var source = """
            function* setIter() {
                let s = new Set<number>();
                s.add(100);
                s.add(200);
                yield* s;
            }

            for (let v of setIter()) {
                console.log(v);
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n200\n", output);
    }

    #endregion

    #region Interpreter vs Compiler Parity

    [Fact]
    public void Generator_BasicYield_InterpreterParity()
    {
        var source = """
            function* counter() {
                yield 1;
                yield 2;
                yield 3;
            }

            let gen = counter();
            console.log(gen.next().value);
            console.log(gen.next().value);
            console.log(gen.next().value);
            console.log(gen.next().done);
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void Generator_IteratorResult_InterpreterParity()
    {
        var source = """
            function* single() {
                yield 42;
            }

            let gen = single();
            let first = gen.next();
            let second = gen.next();

            console.log("First value:", first.value);
            console.log("First done:", first.done);
            console.log("Second done:", second.done);
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void Generator_MultipleInstances_InterpreterParity()
    {
        var source = """
            function* counter() {
                yield 1;
                yield 2;
                yield 3;
            }

            let gen1 = counter();
            let gen2 = counter();

            console.log(gen1.next().value);
            console.log(gen2.next().value);
            console.log(gen1.next().value);
            console.log(gen2.next().value);
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void Generator_YieldStarString_InterpreterParity()
    {
        var source = """
            function* chars() {
                yield* "hi";
            }

            for (let c of chars()) {
                console.log(c);
            }
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void Generator_YieldStarMap_InterpreterParity()
    {
        var source = """
            function* mapIter() {
                let m = new Map<string, number>();
                m.set("a", 1);
                m.set("b", 2);
                yield* m;
            }

            for (let pair of mapIter()) {
                console.log(pair[0] + ":" + pair[1]);
            }
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void Generator_YieldStarSet_InterpreterParity()
    {
        var source = """
            function* setIter() {
                let s = new Set<number>();
                s.add(100);
                s.add(200);
                yield* s;
            }

            for (let v of setIter()) {
                console.log(v);
            }
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    #endregion
}
