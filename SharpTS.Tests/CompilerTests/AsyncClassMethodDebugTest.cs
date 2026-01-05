using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Debug tests for async class methods.
/// </summary>
public class AsyncClassMethodDebugTest
{
    [Fact]
    public void SyncClassMethod_Works()
    {
        var source = """
            class Foo {
                getValue(): number {
                    return 42;
                }
            }
            let f = new Foo();
            console.log(f.getValue());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void SyncClassMethod_FromSyncFunction()
    {
        // Test sync method call on class from sync function
        var source = """
            class Foo {
                getValue(): number {
                    return 42;
                }
            }
            function main(): void {
                let f = new Foo();
                let val = f.getValue();
                console.log(val);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void SyncClassMethod_FromAsyncFunction()
    {
        // Test sync method call on class instance from async function context
        var source = """
            class Foo {
                getValue(): number {
                    return 42;
                }
            }
            async function main(): Promise<void> {
                let f = new Foo();
                let val = f.getValue();
                console.log(val);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void SyncClassMethod_AfterAwait()
    {
        // Test sync method call on class instance AFTER an await
        var source = """
            class Foo {
                getValue(): number {
                    return 42;
                }
            }
            async function main(): Promise<void> {
                await Promise.resolve(null);
                let f = new Foo();
                let val = f.getValue();
                console.log(val);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AsyncClassMethod_NewInAsync()
    {
        var source = """
            class Foo {
                value: number = 42;
            }
            async function main(): Promise<void> {
                await Promise.resolve(null);
                let f = new Foo();
                console.log(f);
                console.log(f.value);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("42", output);
    }
}
