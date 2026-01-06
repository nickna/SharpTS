using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class GeneratorTests
{
    // Basic Generator Tests
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Generator_WithParameters_UsesParameters()
    {
        var source = """
            function* range(start: number, end: number) {
                for (let i = start; i <= end; i++) {
                    yield i;
                }
            }

            for (let x of range(5, 8)) {
                console.log(x);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n6\n7\n8\n", output);
    }

    // For...Of Integration Tests
    [Fact]
    public void Generator_ForOfLoop_IteratesAllValues()
    {
        var source = """
            function* nums() {
                yield 10;
                yield 20;
                yield 30;
            }

            for (let n of nums()) {
                console.log(n);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n30\n", output);
    }

    // Yield* Delegation Tests
    [Fact]
    public void Generator_YieldStarArray_DelegatesCorrectly()
    {
        var source = """
            function* withArray() {
                yield* [1, 2, 3];
            }

            for (let x of withArray()) {
                console.log(x);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void Generator_YieldStarGenerator_DelegatesCorrectly()
    {
        var source = """
            function* inner() {
                yield 2;
                yield 3;
            }

            function* outer() {
                yield 1;
                yield* inner();
                yield 4;
            }

            for (let x of outer()) {
                console.log(x);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n4\n", output);
    }

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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n200\n", output);
    }

    // Generator Control Flow Tests
    [Fact]
    public void Generator_WhileLoop_YieldsMultipleTimes()
    {
        var source = """
            function* countdown(n: number) {
                while (n > 0) {
                    yield n;
                    n--;
                }
            }

            for (let x of countdown(3)) {
                console.log(x);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n2\n1\n", output);
    }

    [Fact]
    public void Generator_IfStatement_ConditionalYield()
    {
        var source = """
            function* conditionalYield(includeSecond: boolean) {
                yield 1;
                if (includeSecond) {
                    yield 2;
                }
                yield 3;
            }

            console.log("With second:");
            for (let x of conditionalYield(true)) {
                console.log(x);
            }

            console.log("Without second:");
            for (let x of conditionalYield(false)) {
                console.log(x);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("With second:\n1\n2\n3\nWithout second:\n1\n3\n", output);
    }

    // Multiple Independent Generators
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n1\n2\n2\n", output);
    }

    // Generator with Closures
    [Fact]
    public void Generator_Closure_CapturesVariables()
    {
        var source = """
            function* makeSequence(multiplier: number) {
                for (let i = 1; i <= 3; i++) {
                    yield i * multiplier;
                }
            }

            for (let x of makeSequence(10)) {
                console.log(x);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n30\n", output);
    }

    // Generator IteratorResult Structure
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("First value: 42\nFirst done: false\nSecond done: true\n", output);
    }
}
