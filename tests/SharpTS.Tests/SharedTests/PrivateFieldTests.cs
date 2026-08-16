using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for ES2022 private class elements (#field, #method).
/// These provide hard runtime privacy unlike TypeScript's 'private' keyword.
/// </summary>
public class PrivateFieldTests
{
    #region Instance Private Fields

    [Theory, ModeData]
    public void PrivateField_GetAndSet(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n", output);
    }

    [Theory, ModeData]
    public void PrivateField_WithInitializer(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void PrivateField_MultipleInstances(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n1\n", output);
    }

    [Theory, ModeData]
    public void PrivateField_CoexistsWithPublicField(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n100\n200\n50\n", output);
    }

    #endregion

    #region Static Private Fields

    [Theory, ModeData]
    public void StaticPrivateField_GetAndSet(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void StaticPrivateField_WithInitializer(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1.0.0\n", output);
    }

    #endregion

    #region Private Methods

    [Theory, ModeData]
    public void PrivateMethod_CalledFromPublicMethod(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void PrivateMethod_AccessesPrivateField(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void PrivateMethod_WithParameters(ExecutionMode mode)
    {
        var source = """
            class Formatter {
                #format(prefix: string, value: number, suffix: string): string {
                    return prefix + value + suffix;
                }

                formatCurrency(amount: number): string {
                    return this.#format("$", amount, ".00");
                }
            }

            const fmt = new Formatter();
            console.log(fmt.formatCurrency(42));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("$42.00\n", output);
    }

    [Theory, ModeData]
    public void PrivateMethod_MultipleOptionalParameters_PartialApplication(ExecutionMode mode)
    {
        // #696: several optional parameters, only some supplied. Omitted trailing arguments read
        // as `undefined`. Compiled mode must pad the call so the fixed-arity method gets every slot.
        var source = """
            class C {
                #combine(a?: number, b?: number, c?: number): number {
                    return (a ?? 1) + (b ?? 2) + (c ?? 3);
                }
                go(): number { return this.#combine(10); }
            }
            console.log(new C().go());
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrivateMethod_DefaultParameter_FiresWhenOmitted(ExecutionMode mode)
    {
        // #696: a defaulted private-method parameter fires its default when the argument is omitted,
        // in compiled mode too. Before the fix the call StackUnderflowed (no padding) and the body
        // had no default prologue, so this never produced the right value under compilation.
        var source = """
            class C {
                #greet(name: string = "World"): string { return "Hi " + name; }
                go(): string { return this.#greet(); }
            }
            console.log(new C().go());
            """;
        Assert.Equal("Hi World\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void PrivateMethod_OptionalAndDefaultParameters_ProduceValidIL()
    {
        // #696 regression guard: calling a private method with fewer arguments than it declares
        // must emit a balanced stack. Covers instance + static, optional + default, omitted +
        // explicit-undefined — the exact shapes that previously emitted StackUnderflow IL.
        var source = """
            class C {
                #opt(b?: number): number { return b ?? 1; }
                #def(b: number = 7): number { return b; }
                static #sopt(b?: string): string { return b ?? "S"; }
                static #sdef(b: string = "D"): string { return b; }
                run(): number { return this.#opt() + this.#def(undefined); }
                static srun(): string { return C.#sopt() + C.#sdef(); }
            }
            console.log(new C().run());
            console.log(C.srun());
            """;
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.Empty(errors);
    }

    #endregion

    #region Static Private Methods

    [Theory, ModeData]
    public void StaticPrivateMethod_CalledFromStaticPublicMethod(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory, ModeData]
    public void StaticPrivateMethod_OptionalParameter_Omitted(ExecutionMode mode)
    {
        // #696: optional parameter on a *static* private method, invoked with no argument.
        var source = """
            class Utils {
                static #label(text?: string): string { return text ?? "default"; }
                static go(): string { return Utils.#label(); }
            }
            console.log(Utils.go());
            """;
        Assert.Equal("default\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticPrivateMethod_AccessesStaticPrivateField(ExecutionMode mode)
    {
        var source = """
            class IdGenerator {
                static #nextId: number = 0;

                static #generateId(): number {
                    IdGenerator.#nextId = IdGenerator.#nextId + 1;
                    return IdGenerator.#nextId;
                }

                static createId(): number {
                    return IdGenerator.#generateId();
                }
            }

            console.log(IdGenerator.createId());
            console.log(IdGenerator.createId());
            console.log(IdGenerator.createId());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Combined Features

    [Theory, ModeData]
    public void PrivateElements_FullExample(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #count: number = 0;
                static #instances: number = 0;

                constructor() {
                    Counter.#instances = Counter.#instances + 1;
                }

                #increment(): void {
                    this.#count = this.#count + 1;
                }

                tick(): number {
                    this.#increment();
                    return this.#count;
                }

                getCount(): number {
                    return this.#count;
                }

                static getInstances(): number {
                    return Counter.#instances;
                }
            }

            const c = new Counter();
            console.log(c.tick());
            console.log(c.tick());
            console.log(c.getCount());
            console.log(Counter.getInstances());

            const c2 = new Counter();
            console.log(Counter.getInstances());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n2\n1\n2\n", output);
    }

    [Theory, ModeData]
    public void PrivateField_MultiplePrivateFields(ExecutionMode mode)
    {
        var source = """
            class Point {
                #x: number;
                #y: number;

                constructor(x: number, y: number) {
                    this.#x = x;
                    this.#y = y;
                }

                getX(): number { return this.#x; }
                getY(): number { return this.#y; }

                distanceFromOrigin(): number {
                    return this.#x + this.#y;
                }
            }

            const p = new Point(3, 4);
            console.log(p.getX());
            console.log(p.getY());
            console.log(p.distanceFromOrigin());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n4\n7\n", output);
    }

    #endregion
}
