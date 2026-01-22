using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for variance annotations (in, out, in out) on generic type parameters.
/// TypeScript 4.7+ feature for explicit variance control.
/// </summary>
public class VarianceAnnotationTests
{
    #region Parsing Tests

    [Fact]
    public void ParseVariance_OutModifier()
    {
        var source = """
            interface Producer<out T> {
                produce(): T;
            }

            class StringProducer implements Producer<string> {
                produce(): string { return "hello"; }
            }

            let p: Producer<string> = new StringProducer();
            console.log(p.produce());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void ParseVariance_InModifier()
    {
        var source = """
            interface Consumer<in T> {
                accept(value: T): void;
            }

            class StringConsumer implements Consumer<string> {
                accept(value: string): void {
                    console.log(value);
                }
            }

            let c: Consumer<string> = new StringConsumer();
            c.accept("hello");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void ParseVariance_InOutModifier()
    {
        var source = """
            interface Processor<in out T> {
                process(value: T): T;
            }

            class StringProcessor implements Processor<string> {
                process(value: string): string {
                    return value.toUpperCase();
                }
            }

            let p: Processor<string> = new StringProcessor();
            console.log(p.process("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\n", result);
    }

    [Fact]
    public void ParseVariance_WithConstraint()
    {
        var source = """
            interface HasLength { length: number; }

            interface Producer<out T extends HasLength> {
                produce(): T;
            }

            class ArrayProducer implements Producer<number[]> {
                produce(): number[] { return [1, 2, 3]; }
            }

            let p: Producer<number[]> = new ArrayProducer();
            console.log(p.produce().length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", result);
    }

    [Fact]
    public void ParseVariance_WithDefault()
    {
        // Test that variance annotation works with default type parameter
        var source = """
            interface Producer<out T = string> {
                produce(): T;
            }

            class StringProducer implements Producer<string> {
                produce(): string { return "default"; }
            }

            let p: Producer<string> = new StringProducer();
            console.log(p.produce());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("default\n", result);
    }

    #endregion

    #region Covariance Tests (out T)

    [Fact]
    public void Covariant_AssignSubtypeProducer_Works()
    {
        // Producer<Dog> should be assignable to Producer<Animal> with out T
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            interface Producer<out T> {
                produce(): T;
            }

            class DogProducer implements Producer<Dog> {
                produce(): Dog {
                    return { name: "Buddy", bark: () => console.log("Woof!") };
                }
            }

            // Covariance allows this assignment
            let dogProducer: Producer<Dog> = new DogProducer();
            let animalProducer: Producer<Animal> = dogProducer;

            console.log(animalProducer.produce().name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Buddy\n", result);
    }

    [Fact]
    public void Covariant_AssignSupertypeProducer_Fails()
    {
        // Producer<Animal> should NOT be assignable to Producer<Dog> with out T
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            interface Producer<out T> {
                produce(): T;
            }

            class AnimalProducer implements Producer<Animal> {
                produce(): Animal {
                    return { name: "Generic Animal" };
                }
            }

            // This should fail - covariance does not allow supertype assignment
            let animalProducer: Producer<Animal> = new AnimalProducer();
            let dogProducer: Producer<Dog> = animalProducer;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("not assignable", ex.Message);
    }

    #endregion

    #region Contravariance Tests (in T)

    [Fact]
    public void Contravariant_AssignSupertypeConsumer_Works()
    {
        // Consumer<Animal> should be assignable to Consumer<Dog> with in T
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            interface Consumer<in T> {
                accept(value: T): void;
            }

            class AnimalConsumer implements Consumer<Animal> {
                accept(value: Animal): void {
                    console.log("Received: " + value.name);
                }
            }

            // Contravariance allows this assignment
            let animalConsumer: Consumer<Animal> = new AnimalConsumer();
            let dogConsumer: Consumer<Dog> = animalConsumer;

            dogConsumer.accept({ name: "Buddy", bark: () => {} });
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Received: Buddy\n", result);
    }

    [Fact]
    public void Contravariant_AssignSubtypeConsumer_Fails()
    {
        // Consumer<Dog> should NOT be assignable to Consumer<Animal> with in T
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            interface Consumer<in T> {
                accept(value: T): void;
            }

            class DogConsumer implements Consumer<Dog> {
                accept(value: Dog): void {
                    console.log("Dog: " + value.name);
                    value.bark();
                }
            }

            // This should fail - contravariance does not allow subtype assignment
            let dogConsumer: Consumer<Dog> = new DogConsumer();
            let animalConsumer: Consumer<Animal> = dogConsumer;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("not assignable", ex.Message);
    }

    #endregion

    #region Bivariance Tests (in out T)

    [Fact]
    public void Bivariant_AssignEitherDirection_Works()
    {
        // With in out T, both directions should work
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            interface Processor<in out T> {
                process(value: T): T;
            }

            class AnimalProcessor implements Processor<Animal> {
                process(value: Animal): Animal {
                    console.log("Processing: " + value.name);
                    return value;
                }
            }

            // Bivariance allows both directions
            let animalProcessor: Processor<Animal> = new AnimalProcessor();
            let dogProcessor: Processor<Dog> = animalProcessor;

            dogProcessor.process({ name: "Buddy", bark: () => {} });
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Processing: Buddy\n", result);
    }

    #endregion

    #region Invariance Tests (no modifier)

    [Fact]
    public void Invariant_RequiresExactMatch_SubtypeFails()
    {
        // Without variance modifier, Box<Dog> should NOT be assignable to Box<Animal>
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            interface Box<T> {
                value: T;
                assign(v: T): void;
            }

            class DogBox implements Box<Dog> {
                value: Dog = { name: "Buddy", bark: () => {} };
                assign(v: Dog): void { this.value = v; }
            }

            // Invariance prevents this assignment
            let dogBox: Box<Dog> = new DogBox();
            let animalBox: Box<Animal> = dogBox;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("not assignable", ex.Message);
    }

    [Fact]
    public void Invariant_RequiresExactMatch_SupertypeFails()
    {
        // Without variance modifier, Box<Animal> should NOT be assignable to Box<Dog>
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            interface Box<T> {
                value: T;
                assign(v: T): void;
            }

            class AnimalBox implements Box<Animal> {
                value: Animal = { name: "Generic" };
                assign(v: Animal): void { this.value = v; }
            }

            // Invariance prevents this assignment
            let animalBox: Box<Animal> = new AnimalBox();
            let dogBox: Box<Dog> = animalBox;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("not assignable", ex.Message);
    }

    [Fact]
    public void Invariant_ExactMatchWorks()
    {
        // Box<Dog> should be assignable to Box<Dog>
        var source = """
            interface Dog { name: string; bark(): void; }

            interface Box<T> {
                value: T;
            }

            class DogBox implements Box<Dog> {
                value: Dog = { name: "Buddy", bark: () => console.log("Woof!") };
            }

            let box1: Box<Dog> = new DogBox();
            let box2: Box<Dog> = box1;
            console.log(box2.value.name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Buddy\n", result);
    }

    #endregion

    #region Multiple Type Parameters Tests

    [Fact]
    public void Variance_MultipleTypeParams_IndependentVariance()
    {
        // interface Func<in T, out R> { apply(t: T): R; }
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            interface Func<in T, out R> {
                apply(t: T): R;
            }

            class DogToString implements Func<Dog, string> {
                apply(t: Dog): string {
                    return "Dog: " + t.name;
                }
            }

            // With in T, out R: Func<Dog, string> -> Func<Animal, string> for input
            // but Func<Dog, string> -> Func<Dog, object> for output
            let dogToString: Func<Dog, string> = new DogToString();

            console.log(dogToString.apply({ name: "Buddy", bark: () => {} }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Dog: Buddy\n", result);
    }

    #endregion

    #region Class Variance Tests

    [Fact]
    public void ClassVariance_Covariant_Works()
    {
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            class Producer<out T> {
                constructor(private factory: () => T) {}
                produce(): T { return this.factory(); }
            }

            let dogFactory = () => ({ name: "Buddy", bark: () => {} });
            let dogProducer = new Producer<Dog>(dogFactory);

            // Covariant: Producer<Dog> assignable to Producer<Animal>
            let animalProducer: Producer<Animal> = dogProducer;
            console.log(animalProducer.produce().name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Buddy\n", result);
    }

    [Fact]
    public void ClassVariance_Contravariant_Works()
    {
        var source = """
            interface Animal { name: string; }
            interface Dog extends Animal { bark(): void; }

            class Consumer<in T> {
                constructor(private handler: (v: T) => void) {}
                accept(value: T): void { this.handler(value); }
            }

            let animalHandler = (a: Animal) => console.log("Animal: " + a.name);
            let animalConsumer = new Consumer<Animal>(animalHandler);

            // Contravariant: Consumer<Animal> assignable to Consumer<Dog>
            let dogConsumer: Consumer<Dog> = animalConsumer;
            dogConsumer.accept({ name: "Buddy", bark: () => {} });
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Animal: Buddy\n", result);
    }

    #endregion
}
