using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for ES2022 private class elements (#field, #method) in the IL compiler.
/// Currently, the IL compiler throws NotImplementedException for private elements.
/// These tests document the expected behavior when IL compilation is implemented.
/// </summary>
public class PrivateFieldTests
{
    #region Instance Private Fields (Skipped - IL not implemented)

    [Fact]
    public void PrivateField_GetAndSet()
    {
        var source = """
            class Counter {
                #count: number = 0;

                increment(): void {
                    this.#count = this.#count + 1;
                }

                getCount(): number {
                    return this.#count;
                }
            }

            const c = new Counter();
            console.log(c.getCount());
            c.increment();
            console.log(c.getCount());
            c.increment();
            console.log(c.getCount());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n1\n2\n", output);
    }

    [Fact]
    public void PrivateField_WithInitializer()
    {
        var source = """
            class Box {
                #value: number = 42;

                getValue(): number {
                    return this.#value;
                }
            }

            const b = new Box();
            console.log(b.getValue());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void PrivateField_MultipleInstances()
    {
        var source = """
            class Counter {
                #count: number = 0;

                increment(): void {
                    this.#count = this.#count + 1;
                }

                getCount(): number {
                    return this.#count;
                }
            }

            const a = new Counter();
            const b = new Counter();

            a.increment();
            a.increment();
            b.increment();

            console.log(a.getCount());
            console.log(b.getCount());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n1\n", output);
    }

    [Fact]
    public void PrivateField_CoexistsWithPublicField()
    {
        var source = """
            class Box {
                value: number = 0;
                #value: number = 100;

                getPrivateValue(): number {
                    return this.#value;
                }

                setPrivateValue(v: number): void {
                    this.#value = v;
                }
            }

            const box = new Box();
            console.log(box.value);
            console.log(box.getPrivateValue());
            box.setPrivateValue(200);
            console.log(box.getPrivateValue());
            box.value = 50;
            console.log(box.value);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n100\n200\n50\n", output);
    }

    #endregion

    #region Static Private Fields (Skipped - IL not implemented)

    [Fact]
    public void StaticPrivateField_GetAndSet()
    {
        var source = """
            class Counter {
                static #instanceCount: number = 0;

                constructor() {
                    Counter.#instanceCount = Counter.#instanceCount + 1;
                }

                static getCount(): number {
                    return Counter.#instanceCount;
                }
            }

            console.log(Counter.getCount());
            const a = new Counter();
            console.log(Counter.getCount());
            const b = new Counter();
            console.log(Counter.getCount());
            const c = new Counter();
            console.log(Counter.getCount());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n1\n2\n3\n", output);
    }

    [Fact]
    public void StaticPrivateField_WithInitializer()
    {
        var source = """
            class Config {
                static #version: string = "1.0.0";

                static getVersion(): string {
                    return Config.#version;
                }
            }

            console.log(Config.getVersion());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1.0.0\n", output);
    }

    #endregion

    #region Private Methods (Skipped - IL not implemented)

    [Fact]
    public void PrivateMethod_CalledFromPublicMethod()
    {
        var source = """
            class Calculator {
                #add(a: number, b: number): number {
                    return a + b;
                }

                sum(x: number, y: number): number {
                    return this.#add(x, y);
                }
            }

            const calc = new Calculator();
            console.log(calc.sum(3, 4));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void PrivateMethod_AccessesPrivateField()
    {
        var source = """
            class Counter {
                #count: number = 0;

                #increment(): void {
                    this.#count = this.#count + 1;
                }

                tick(): number {
                    this.#increment();
                    return this.#count;
                }
            }

            const c = new Counter();
            console.log(c.tick());
            console.log(c.tick());
            console.log(c.tick());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Static Private Methods (Skipped - IL not implemented)

    [Fact]
    public void StaticPrivateMethod_CalledFromStaticPublicMethod()
    {
        var source = """
            class Utils {
                static #double(n: number): number {
                    return n * 2;
                }

                static quadruple(n: number): number {
                    return Utils.#double(Utils.#double(n));
                }
            }

            console.log(Utils.quadruple(5));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20\n", output);
    }

    #endregion

    #region Compiler Behavior Verification

    [Fact]
    public void PrivateField_CompilerSupportsBasicGetAndSet()
    {
        // This test verifies that private fields are now supported in compiled mode
        var source = """
            class Foo {
                #value: number = 0;

                getValue(): number {
                    return this.#value;
                }

                setValue(v: number): void {
                    this.#value = v;
                }
            }

            const f = new Foo();
            console.log(f.getValue());
            f.setValue(42);
            console.log(f.getValue());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n42\n", output);
    }

    #endregion
}
