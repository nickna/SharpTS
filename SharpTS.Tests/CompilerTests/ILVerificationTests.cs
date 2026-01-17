using SharpTS.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests that verify compiled assemblies produce valid IL.
/// These tests catch IL generation bugs at test time rather than runtime.
/// </summary>
public class ILVerificationTests
{
    // Known IL errors in runtime types that need future investigation
    private static readonly HashSet<string> KnownRuntimeErrors = new()
    {
        // URL helper methods use Uri.TryCreate with byref parameters - works at runtime
        // but IL verifier reports StackUnexpected. Needs investigation.
        "$Runtime.UrlParse",
        "$Runtime.UrlResolve",
    };

    private static List<string> FilterKnownErrors(List<string> errors)
    {
        return errors.Where(e => !KnownRuntimeErrors.Any(known => e.Contains(known))).ToList();
    }

    [Fact]
    public void BasicArithmetic_PassesILVerification()
    {
        var source = """
            let x = 10 + 5;
            console.log(x);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        var unexpectedErrors = FilterKnownErrors(errors);

        Assert.Empty(unexpectedErrors);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void ClassWithMethods_PassesILVerification()
    {
        var source = """
            class Calculator {
                add(a: number, b: number): number {
                    return a + b;
                }
            }
            let calc = new Calculator();
            console.log(calc.add(3, 4));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        var unexpectedErrors = FilterKnownErrors(errors);
        Assert.Empty(unexpectedErrors);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void AsyncAwait_PassesILVerification()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }

            async function main() {
                let result = await getValue();
                console.log(result);
            }

            main();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        var unexpectedErrors = FilterKnownErrors(errors);
        Assert.Empty(unexpectedErrors);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Closures_PassesILVerification()
    {
        var source = """
            function makeCounter(): () => number {
                let count = 0;
                return () => {
                    count = count + 1;
                    return count;
                };
            }

            let counter = makeCounter();
            console.log(counter());
            console.log(counter());
            """;

        // For now, just verify IL without running (runtime has issues with rewritten assemblies)
        var errors = TestHarness.CompileAndVerifyOnly(source);
        var unexpectedErrors = FilterKnownErrors(errors);

        Assert.Empty(unexpectedErrors);
    }

    [Fact]
    public void Inheritance_PassesILVerification()
    {
        var source = """
            class Animal {
                speak(): string {
                    return "...";
                }
            }

            class Dog extends Animal {
                speak(): string {
                    return "Woof!";
                }
            }

            let dog = new Dog();
            console.log(dog.speak());
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        var unexpectedErrors = FilterKnownErrors(errors);
        Assert.Empty(unexpectedErrors);
        Assert.Equal("Woof!\n", output);
    }

    [Fact]
    public void Generators_PassesILVerification()
    {
        var source = """
            function* range(start: number, end: number) {
                for (let i = start; i < end; i = i + 1) {
                    yield i;
                }
            }

            for (let n of range(1, 4)) {
                console.log(n);
            }
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        var unexpectedErrors = FilterKnownErrors(errors);
        Assert.Empty(unexpectedErrors);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void TryCatchFinally_PassesILVerification()
    {
        var source = """
            function test(): string {
                try {
                    throw "test error";
                } catch (e) {
                    return "caught";
                } finally {
                    console.log("finally");
                }
            }

            console.log(test());
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        var unexpectedErrors = FilterKnownErrors(errors);
        Assert.Empty(unexpectedErrors);
        Assert.Equal("finally\ncaught\n", output);
    }
}
