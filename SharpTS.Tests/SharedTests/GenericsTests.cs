using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for generic functions, classes, interfaces, and type constraints. Runs against both interpreter and compiler.
/// </summary>
public class GenericsTests
{
    #region Generic Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericFunction_TypeInference_Works(ExecutionMode mode)
    {
        var source = """
            function identity<T>(x: T): T {
                return x;
            }
            console.log(identity(42));
            console.log(identity("hello"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\nhello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericFunction_ExplicitTypeArgument_Works(ExecutionMode mode)
    {
        var source = """
            function identity<T>(x: T): T {
                return x;
            }
            console.log(identity<number>(42));
            console.log(identity<string>("world"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\nworld\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericFunction_MultipleTypeParameters_Works(ExecutionMode mode)
    {
        var source = """
            function pair<T, U>(first: T, second: U): T {
                console.log(second);
                return first;
            }
            console.log(pair<string, number>("hello", 42));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\nhello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericFunction_WithArrayType_Works(ExecutionMode mode)
    {
        var source = """
            function first<T>(arr: T[]): T {
                return arr[0];
            }
            let nums: number[] = [1, 2, 3];
            console.log(first(nums));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    #endregion

    #region Generic Classes

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericClass_BasicInstantiation_Works(ExecutionMode mode)
    {
        var source = """
            class Box<T> {
                value: T;
                constructor(v: T) {
                    this.value = v;
                }
            }
            let numBox: Box<number> = new Box<number>(42);
            console.log(numBox.value);
            let strBox: Box<string> = new Box<string>("hello");
            console.log(strBox.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\nhello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericClass_WithMethod_Works(ExecutionMode mode)
    {
        var source = """
            class Container<T> {
                value: T;
                constructor(v: T) {
                    this.value = v;
                }
                getValue(): T {
                    return this.value;
                }
            }
            let c: Container<number> = new Container<number>(99);
            console.log(c.getValue());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericClass_MultipleTypeParameters_Works(ExecutionMode mode)
    {
        var source = """
            class Pair<K, V> {
                key: K;
                value: V;
                constructor(k: K, v: V) {
                    this.key = k;
                    this.value = v;
                }
            }
            let p: Pair<string, number> = new Pair<string, number>("age", 25);
            console.log(p.key);
            console.log(p.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("age\n25\n", output);
    }

    #endregion

    #region Generic Interfaces

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericInterface_ObjectLiteral_Works(ExecutionMode mode)
    {
        var source = """
            interface Container<T> {
                value: T;
            }
            let c: Container<number> = { value: 42 };
            console.log(c.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericInterface_MultipleTypeParameters_Works(ExecutionMode mode)
    {
        var source = """
            interface KeyValue<K, V> {
                key: K;
                value: V;
            }
            let kv: KeyValue<string, number> = { key: "count", value: 10 };
            console.log(kv.key);
            console.log(kv.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("count\n10\n", output);
    }

    #endregion

    #region Type Constraints

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericFunction_WithConstraint_Works(ExecutionMode mode)
    {
        var source = """
            class Animal {
                name: string;
                constructor(n: string) {
                    this.name = n;
                }
            }
            class Dog extends Animal {
                constructor(n: string) {
                    super(n);
                }
            }
            function getName<T extends Animal>(animal: T): string {
                return animal.name;
            }
            let dog: Dog = new Dog("Rex");
            console.log(getName(dog));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Rex\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericClass_WithConstraint_Works(ExecutionMode mode)
    {
        var source = """
            class Animal {
                name: string;
                constructor(n: string) {
                    this.name = n;
                }
                speak(): string {
                    return this.name + " speaks";
                }
            }
            class AnimalHolder<T extends Animal> {
                animal: T;
                constructor(a: T) {
                    this.animal = a;
                }
                makeSpeak(): string {
                    return this.animal.speak();
                }
            }
            let a: Animal = new Animal("Buddy");
            let holder: AnimalHolder<Animal> = new AnimalHolder<Animal>(a);
            console.log(holder.makeSpeak());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Buddy speaks\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericFunction_MixedConstraints_Works(ExecutionMode mode)
    {
        var source = """
            class Base {
                id: number;
                constructor(i: number) {
                    this.id = i;
                }
            }
            function process<T extends Base, U>(item: T, data: U): number {
                console.log(data);
                return item.id;
            }
            let b: Base = new Base(42);
            console.log(process(b, "extra"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("extra\n42\n", output);
    }

    #endregion
}
