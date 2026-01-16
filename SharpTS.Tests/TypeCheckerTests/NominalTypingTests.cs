using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for nominal type checking (class inheritance) in TypeChecker.
/// Classes use nominal typing (name-based), unlike interfaces which use structural typing.
/// </summary>
public class NominalTypingTests
{
    #region Class-to-Class Compatibility

    [Fact]
    public void SameClass_Compatible()
    {
        var source = """
            class Point {
                constructor(public x: number, public y: number) {}
            }

            let p1: Point = new Point(1, 2);
            let p2: Point = p1;
            console.log(p2.x + p2.y);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", result);
    }

    [Fact]
    public void SubclassToSuperclass_Compatible()
    {
        var source = """
            class Animal {
                constructor(public name: string) {}
            }
            class Dog extends Animal {
                constructor(name: string, public breed: string) {
                    super(name);
                }
            }

            let dog: Dog = new Dog("Rex", "German Shepherd");
            let animal: Animal = dog;
            console.log(animal.name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex\n", result);
    }

    [Fact]
    public void SuperclassToSubclass_Incompatible()
    {
        var source = """
            class Animal {
                constructor(public name: string) {}
            }
            class Dog extends Animal {
                constructor(name: string, public breed: string) {
                    super(name);
                }
            }

            let animal: Animal = new Animal("Generic");
            let dog: Dog = animal;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void UnrelatedClasses_Incompatible()
    {
        var source = """
            class Cat {
                constructor(public name: string) {}
            }
            class Dog {
                constructor(public name: string) {}
            }

            let cat: Cat = new Cat("Whiskers");
            let dog: Dog = cat;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void DeepInheritance_Compatible()
    {
        var source = """
            class Animal {
                constructor(public name: string) {}
            }
            class Mammal extends Animal {
                constructor(name: string) {
                    super(name);
                }
            }
            class Dog extends Mammal {
                constructor(name: string, public breed: string) {
                    super(name);
                }
            }

            let dog: Dog = new Dog("Rex", "Shepherd");
            let mammal: Mammal = dog;
            let animal: Animal = dog;
            console.log(animal.name);
            console.log(mammal.name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex\nRex\n", result);
    }

    #endregion

    #region Generic Classes

    [Fact]
    public void GenericClass_SameTypeArg_Compatible()
    {
        var source = """
            class Box<T> {
                constructor(public value: T) {}
            }

            let b1: Box<number> = new Box<number>(42);
            let b2: Box<number> = b1;
            console.log(b2.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void GenericClass_DifferentTypeArg_Incompatible()
    {
        var source = """
            class Box<T> {
                constructor(public value: T) {}
            }

            let numBox: Box<number> = new Box<number>(42);
            let strBox: Box<string> = numBox;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact(Skip = "Generic class constructor type argument inference not yet implemented")]
    public void GenericClass_InferredTypeArg_Works()
    {
        var source = """
            class Box<T> {
                constructor(public value: T) {}
            }

            let b = new Box(42);  // T inferred as number
            console.log(b.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void GenericClassInheritance_Compatible()
    {
        var source = """
            class Box<T> {
                constructor(public value: T) {}
            }
            class NumberBox extends Box<number> {
                constructor(value: number) {
                    super(value);
                }
            }

            let nb: NumberBox = new NumberBox(42);
            let b: Box<number> = nb;
            console.log(b.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void GenericClassInheritance_PropertyTypeSubstitution()
    {
        // Tests that inherited generic properties have their types properly substituted
        var source = """
            class Container<T> {
                constructor(public item: T) {}
                getItem(): T { return this.item; }
            }

            class NumberContainer extends Container<number> {
                constructor(n: number) {
                    super(n);
                }
                double(): number {
                    return this.item * 2;
                }
            }

            let nc: NumberContainer = new NumberContainer(21);
            console.log(nc.double());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void GenericClassInheritance_MultipleTypeParams()
    {
        // Tests generic inheritance with multiple type parameters
        var source = """
            class Pair<K, V> {
                constructor(public key: K, public value: V) {}
            }

            class StringNumberPair extends Pair<string, number> {
                constructor(key: string, value: number) {
                    super(key, value);
                }
                describe(): string {
                    return this.key;
                }
            }

            let p: StringNumberPair = new StringNumberPair("age", 25);
            console.log(p.describe());
            console.log(p.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("age\n25\n", result);
    }

    [Fact]
    public void GenericClassInheritance_MethodReturnType()
    {
        // Tests that inherited generic methods have return types properly substituted
        var source = """
            class Box<T> {
                constructor(private content: T) {}
                getContent(): T { return this.content; }
            }

            class StringBox extends Box<string> {
                constructor(s: string) {
                    super(s);
                }
                getUpperContent(): string {
                    return this.getContent();
                }
            }

            let sb: StringBox = new StringBox("hello");
            console.log(sb.getUpperContent());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void GenericClassInheritance_TypeMismatch_Fails()
    {
        // Tests that type mismatches are caught when assigning incompatible generic types
        var source = """
            class Box<T> {
                constructor(public value: T) {}
            }

            class NumberBox extends Box<number> {
                constructor(value: number) {
                    super(value);
                }
            }

            let nb: NumberBox = new NumberBox(42);
            let sb: Box<string> = nb;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("not assignable", ex.Message);
    }

    #endregion

    #region Interface Implementation

    [Fact]
    public void ClassImplementsInterface_Compatible()
    {
        var source = """
            interface Printable {
                print(): void;
            }

            class Document implements Printable {
                print(): void {
                    console.log("Document printed");
                }
            }

            let doc: Document = new Document();
            let printable: Printable = doc;
            printable.print();
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Document printed\n", result);
    }

    [Fact]
    public void ClassMissingInterfaceMethod_Fails()
    {
        var source = """
            interface Printable {
                print(): void;
            }

            class Document implements Printable {
                display(): void {
                    console.log("Document displayed");
                }
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ClassImplementsMultipleInterfaces()
    {
        var source = """
            interface Printable {
                print(): void;
            }
            interface Saveable {
                save(): void;
            }

            class Document implements Printable, Saveable {
                print(): void {
                    console.log("printed");
                }
                save(): void {
                    console.log("saved");
                }
            }

            let doc = new Document();
            doc.print();
            doc.save();
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("printed\nsaved\n", result);
    }

    [Fact(Skip = "TypeChecker does not validate interface method signature compatibility (parameter count/types)")]
    public void InterfaceMethodWrongSignature_Fails()
    {
        var source = """
            interface Printable {
                print(message: string): void;
            }

            class Document implements Printable {
                print(): void {
                    console.log("No message");
                }
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Abstract Classes

    [Fact]
    public void AbstractClass_ConcreteSubclass_Works()
    {
        var source = """
            abstract class Shape {
                abstract area(): number;
            }

            class Circle extends Shape {
                constructor(public radius: number) {
                    super();
                }
                area(): number {
                    return 3.14159 * this.radius * this.radius;
                }
            }

            let c: Shape = new Circle(10);
            console.log(c.area());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.StartsWith("314", result);
    }

    [Fact]
    public void AbstractClass_MissingAbstractMethod_Fails()
    {
        var source = """
            abstract class Shape {
                abstract area(): number;
            }

            class Circle extends Shape {
                constructor(public radius: number) {
                    super();
                }
                // Missing area() implementation
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void AbstractClass_CannotInstantiate()
    {
        var source = """
            abstract class Shape {
                abstract area(): number;
            }

            let s = new Shape();
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("abstract", ex.Message.ToLower());
    }

    [Fact]
    public void AbstractClass_WithConcreteMethod()
    {
        // Note: Subclass must define constructor explicitly since constructor inheritance isn't automatic
        var source = """
            abstract class Animal {
                constructor(public name: string) {}

                greet(): void {
                    console.log("Hello, " + this.name);
                }

                abstract speak(): void;
            }

            class Dog extends Animal {
                constructor(name: string) {
                    super(name);
                }
                speak(): void {
                    console.log("Woof!");
                }
            }

            let d = new Dog("Rex");
            d.greet();
            d.speak();
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, Rex\nWoof!\n", result);
    }

    #endregion

    #region Override Validation

    [Fact]
    public void Override_ValidOverride_Works()
    {
        var source = """
            class Animal {
                speak(): void {
                    console.log("...");
                }
            }

            class Dog extends Animal {
                override speak(): void {
                    console.log("Woof!");
                }
            }

            let d = new Dog();
            d.speak();
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Woof!\n", result);
    }

    [Fact]
    public void Override_NoParentMethod_Fails()
    {
        var source = """
            class Animal {
                eat(): void {
                    console.log("eating");
                }
            }

            class Dog extends Animal {
                override speak(): void {
                    console.log("Woof!");
                }
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void Override_CompatibleReturnType_Works()
    {
        var source = """
            class Animal {
                clone(): Animal {
                    return new Animal();
                }
            }

            class Dog extends Animal {
                override clone(): Dog {
                    return new Dog();
                }
            }

            let d = new Dog();
            let cloned = d.clone();
            console.log("clone works");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("clone works\n", result);
    }

    #endregion

    #region Static Members

    [Fact]
    public void StaticMethod_AccessedViaClassName()
    {
        var source = """
            class MathHelper {
                static add(a: number, b: number): number {
                    return a + b;
                }
            }

            console.log(MathHelper.add(3, 4));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", result);
    }

    [Fact(Skip = "Static fields with initializers in class definition not yet supported")]
    public void StaticField_AccessedViaClassName()
    {
        var source = """
            class Counter {
                static count: number = 0;

                static increment(): void {
                    Counter.count++;
                }
            }

            Counter.increment();
            Counter.increment();
            console.log(Counter.count);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", result);
    }

    [Fact]
    public void StaticMethod_InheritedInSubclass()
    {
        var source = """
            class Parent {
                static greet(): void {
                    console.log("Hello from parent");
                }
            }

            class Child extends Parent {}

            Child.greet();
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello from parent\n", result);
    }

    #endregion

    #region This Type in Classes

    [Fact]
    public void This_InMethod_ReturnsInstance()
    {
        var source = """
            class Builder {
                value: number = 0;

                add(n: number): Builder {
                    this.value += n;
                    return this;
                }
            }

            let b = new Builder();
            b.add(1).add(2).add(3);
            console.log(b.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("6\n", result);
    }

    [Fact]
    public void This_InStaticMethod_Fails()
    {
        var source = """
            class Counter {
                static value: number = 0;

                static increment(): void {
                    this.value++;
                }
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Super Calls

    [Fact]
    public void Super_CallsParentConstructor()
    {
        var source = """
            class Animal {
                constructor(public name: string) {}
            }

            class Dog extends Animal {
                constructor(name: string, public breed: string) {
                    super(name);
                }
            }

            let d = new Dog("Rex", "Shepherd");
            console.log(d.name);
            console.log(d.breed);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex\nShepherd\n", result);
    }

    [Fact]
    public void Super_CallsParentMethod()
    {
        var source = """
            class Animal {
                speak(): void {
                    console.log("...");
                }
            }

            class Dog extends Animal {
                speak(): void {
                    super.speak();
                    console.log("Woof!");
                }
            }

            let d = new Dog();
            d.speak();
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("...\nWoof!\n", result);
    }

    #endregion
}
