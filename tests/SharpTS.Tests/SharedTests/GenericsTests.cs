using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for generic functions, classes, interfaces, and type constraints. Runs against both interpreter and compiler.
/// </summary>
public class GenericsTests
{
    #region Generic Functions

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void GenericArrowFunction_Works(ExecutionMode mode)
    {
        var source = """
            const identity = <T>(x: T): T => x;
            console.log(identity<number>(42));
            console.log(identity("hello"));
            const second = <T, U>(a: T, b: U) => b;
            console.log(second<string, number>("a", 99));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\nhello\n99\n", output);
    }

    #endregion

    #region Generic Classes

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void GenericClass_InferredTypeArguments_Works(ExecutionMode mode)
    {
        // `new Box(5)` relies on type-argument inference from the constructor argument.
        // In compiled mode this previously emitted Newobj against the open generic TypeDef
        // and threw TypeLoadException at load time. (#274)
        var source = """
            class Box<T> {
                constructor(public v: T) {}
                get(): T { return this.v; }
            }
            console.log(new Box(5).get());
            console.log(new Box("hello").get());
            console.log(new Box(true).get());

            class Pair<A, B> {
                constructor(public a: A, public b: B) {}
            }
            let p = new Pair("x", 42);
            console.log(p.a, p.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\nhello\ntrue\nx 42\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    // Regression tests for #178: compiled mode emitted castclass/callvirt tokens against
    // the open generic TypeDef inside the class's own method bodies, which the CLR refuses
    // to load at JIT time (TypeLoadException: Could not load type 'Stack').

    [Theory, ModeData]
    public void GenericClass_ArrayFieldAndGetter_Works(ExecutionMode mode)
    {
        var source = """
            class Stack<T> {
                private items: T[] = [];
                push(item: T): void {
                    this.items.push(item);
                }
                pop(): T | undefined {
                    return this.items.pop();
                }
                peek(): T | undefined {
                    return this.items[this.items.length - 1];
                }
                get size(): number {
                    return this.items.length;
                }
            }
            const stack = new Stack<number>();
            stack.push(10);
            stack.push(20);
            stack.push(30);
            console.log(stack.size);
            console.log(stack.peek());
            console.log(stack.pop());
            console.log(stack.size);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n30\n30\n2\n", output);
    }

    [Theory, ModeData]
    public void GenericClass_NonGenericFields_Work(ExecutionMode mode)
    {
        var source = """
            class Mixed<T> {
                private maybe: T | undefined;
                private label: string = "lbl";
                private count: number = 0;
                set(v: T): void {
                    this.maybe = v;
                    this.count = this.count + 1;
                }
                describe(): string {
                    return this.label + ":" + this.count + ":" + this.maybe;
                }
            }
            const m = new Mixed<number>();
            m.set(42);
            console.log(m.describe());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("lbl:1:42\n", output);
    }

    [Theory, ModeData]
    public void GenericClass_MethodCallingOwnMethod_Works(ExecutionMode mode)
    {
        var source = """
            class Box<T> {
                describe(): string {
                    return "box:" + this.tag();
                }
                tag(): string {
                    return "generic";
                }
            }
            console.log(new Box<number>().describe());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("box:generic\n", output);
    }

    [Theory, ModeData]
    public void GenericClass_ExtendedByNonGeneric_InheritedFieldAndSuperCall_Work(ExecutionMode mode)
    {
        var source = """
            class Base<T> {
                protected stored: T[] = [];
                put(v: T): void {
                    this.stored.push(v);
                }
                count(): number {
                    return this.stored.length;
                }
            }
            class IntBag extends Base<number> {
                sum(): number {
                    let t = 0;
                    for (const v of this.stored) {
                        t += v;
                    }
                    return t + super.count();
                }
            }
            const bag = new IntBag();
            bag.put(5);
            bag.put(7);
            console.log(bag.sum() + " " + bag.count());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("14 2\n", output);
    }

    [Theory, ModeData]
    public void GenericClass_ExplicitAccessors_Work(ExecutionMode mode)
    {
        var source = """
            class Temp<T> {
                private _v: number = 0;
                get value(): number {
                    return this._v;
                }
                set value(n: number) {
                    this._v = n;
                }
                bump(): number {
                    this.value = this.value + 5;
                    return this.value;
                }
            }
            const t = new Temp<boolean>();
            t.value = 10;
            console.log(t.bump());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void GenericClass_AsyncMethodTouchingFields_Works(ExecutionMode mode)
    {
        var source = """
            class Repo<T> {
                private items: T[] = [];
                add(item: T): void {
                    this.items.push(item);
                }
                async fetchAll(): Promise<number> {
                    await Promise.resolve();
                    return this.items.length;
                }
            }
            async function main(): Promise<void> {
                const r = new Repo<number>();
                r.add(1);
                r.add(2);
                console.log(await r.fetchAll());
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void GenericClass_GeneratorMethodTouchingFields_Works(ExecutionMode mode)
    {
        var source = """
            class Seq<T> {
                private items: T[] = [];
                add(item: T): void {
                    this.items.push(item);
                }
                *iterate(): Generator<T> {
                    for (const x of this.items) {
                        yield x;
                    }
                }
            }
            const s = new Seq<string>();
            s.add("a");
            s.add("b");
            let out = "";
            for (const v of s.iterate()) {
                out += v;
            }
            console.log(out);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ab\n", output);
    }

    #endregion

    #region Generic Interfaces

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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
