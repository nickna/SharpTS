using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for call signatures in interfaces.
/// </summary>
public class CallSignatureTests
{
    [Fact]
    public void CallSignature_BasicInterface()
    {
        var code = """
            interface StringTransformer {
                (input: string): string;
            }

            let upper: StringTransformer = (s: string) => s.toUpperCase();
            console.log(upper("hello"));
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("HELLO\n", result);
    }

    [Fact]
    public void CallSignature_WithMultipleParameters()
    {
        var code = """
            interface Adder {
                (a: number, b: number): number;
            }

            let add: Adder = (x: number, y: number) => x + y;
            console.log(add(3, 5));
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("8\n", result);
    }

    [Fact]
    public void CallSignature_WithTwoParameters()
    {
        var code = """
            interface Greeter {
                (name: string, greeting: string): string;
            }

            let greet: Greeter = (name: string, greeting: string) => {
                return greeting + " " + name;
            };
            console.log(greet("World", "Hello"));
            console.log(greet("World", "Hi"));
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("Hello World\nHi World\n", result);
    }

    [Fact]
    public void CallSignature_ReturnsVoid()
    {
        var code = """
            interface Logger {
                (message: string): void;
            }

            let log: Logger = (msg: string) => {
                console.log("LOG: " + msg);
            };
            log("test");
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("LOG: test\n", result);
    }

    [Fact]
    public void CallSignature_MultipleSignatures()
    {
        var code = """
            interface Formatter {
                (value: string): string;
                (value: number): string;
            }

            let format: Formatter = (value: any): string => {
                if (typeof value === "number") {
                    return "Number: " + value;
                }
                return "String: " + value;
            };

            console.log(format("hello"));
            console.log(format(42));
            """;

        var result = TestHarness.RunInterpreted(code);
        Assert.Equal("String: hello\nNumber: 42\n", result);
    }
}
