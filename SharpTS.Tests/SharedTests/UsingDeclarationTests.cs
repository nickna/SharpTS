using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the 'using' declaration (TypeScript 5.2+ explicit resource management).
/// Runs against both interpreter and compiler where supported.
/// </summary>
public class UsingDeclarationTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_CallsDisposeAtBlockEnd(ExecutionMode mode)
    {
        var source = """
            let disposed = false;
            {
                using resource = {
                    [Symbol.dispose]() {
                        disposed = true;
                    }
                };
                console.log("inside block");
            }
            console.log("disposed:", disposed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inside block\ndisposed: true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_SkipsNullValues(ExecutionMode mode)
    {
        var source = """
            let log: string[] = [];
            {
                using resource: any = null;
                log.push("inside block");
            }
            log.push("after block");
            console.log(log.join(", "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inside block, after block\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_DisposesInReverseOrder(ExecutionMode mode)
    {
        var source = """
            let order: string[] = [];
            {
                using a = { [Symbol.dispose]() { order.push("a"); } };
                using b = { [Symbol.dispose]() { order.push("b"); } };
                using c = { [Symbol.dispose]() { order.push("c"); } };
            }
            console.log(order.join(", "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("c, b, a\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_DisposesOnException(ExecutionMode mode)
    {
        var source = """
            let disposed = false;
            try {
                {
                    using resource = {
                        [Symbol.dispose]() {
                            disposed = true;
                        }
                    };
                    throw new Error("test error");
                }
            } catch (e) {
                // error caught
            }
            console.log("disposed:", disposed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("disposed: true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_DisposesOnReturn(ExecutionMode mode)
    {
        var source = """
            let disposed = false;

            function test(): number {
                using resource = {
                    [Symbol.dispose]() {
                        disposed = true;
                    }
                };
                return 42;
            }

            let result = test();
            console.log("result:", result);
            console.log("disposed:", disposed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("result: 42\ndisposed: true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_CommaSeparated_DisposesAll(ExecutionMode mode)
    {
        var source = """
            let disposed: string[] = [];
            {
                using a = { [Symbol.dispose]() { disposed.push("a"); } },
                      b = { [Symbol.dispose]() { disposed.push("b"); } };
            }
            console.log(disposed.join(", "));
            """;

        var output = TestHarness.Run(source, mode);
        // Should dispose in reverse order: b, then a
        Assert.Equal("b, a\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_VariableAccessible_InsideBlock(ExecutionMode mode)
    {
        var source = """
            {
                using resource = {
                    value: 42,
                    [Symbol.dispose]() {}
                };
                console.log("value:", resource.value);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("value: 42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_WithTypeAnnotation(ExecutionMode mode)
    {
        var source = """
            type Disposable = {
                value: number;
            };

            let disposed = false;
            {
                using resource: Disposable = {
                    value: 100,
                    [Symbol.dispose]() {
                        disposed = true;
                    }
                };
                console.log("value:", resource.value);
            }
            console.log("disposed:", disposed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("value: 100\ndisposed: true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_NestedBlocks_DisposeInCorrectOrder(ExecutionMode mode)
    {
        var source = """
            let order: string[] = [];
            {
                using outer = { [Symbol.dispose]() { order.push("outer"); } };
                {
                    using inner = { [Symbol.dispose]() { order.push("inner"); } };
                }
                order.push("between");
            }
            console.log(order.join(", "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner, between, outer\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_DisposeReceivesCorrectThis(ExecutionMode mode)
    {
        var source = """
            let receivedThis: any = null;
            {
                using resource = {
                    id: "test-resource",
                    [Symbol.dispose]() {
                        receivedThis = this.id;
                    }
                };
            }
            console.log(receivedThis);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test-resource\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Using_DisposeThrows_CatchesError(ExecutionMode mode)
    {
        var source = """
            let disposed = false;
            let errorCaught = false;
            try {
                {
                    using resource = {
                        [Symbol.dispose]() {
                            disposed = true;
                            throw new Error("dispose error");
                        }
                    };
                }
            } catch (e) {
                errorCaught = true;
            }
            console.log("disposed:", disposed);
            console.log("error caught:", errorCaught);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("disposed: true", output);
        Assert.Contains("error caught: true", output);
    }
}
