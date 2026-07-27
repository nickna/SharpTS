using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for TypeScript class compatibility. Public class surfaces are structural;
/// private/protected members carry declaration-origin brands.
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
    public void UnrelatedSameShapeClasses_StructurallyCompatible()
    {
        // Two unrelated, identically-shaped, all-public classes are mutually assignable in
        // TypeScript (classes are compared structurally unless branded by private/protected
        // members). Issue #129.
        var source = """
            class Cat {
                constructor(public name: string) {}
            }
            class Dog {
                constructor(public name: string) {}
            }

            let cat: Cat = new Cat("Whiskers");
            let dog: Dog = cat;
            console.log(dog.name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Whiskers\n", result);
    }

    [Fact]
    public void UnrelatedEmptyClasses_AreStructurallyCompatible()
    {
        var source = """
            class A {}
            class B {}
            let a: A = new B();
            let b: B = new A();
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void UnrelatedClassesWithPrivateMembers_NominallyIncompatible()
    {
        // A private (or protected) member brands the target nominally: an identically-shaped but
        // unrelated class is NOT assignable, matching TypeScript. Issue #129.
        var source = """
            class A {
                private id: number = 1;
            }
            class B {
                private id: number = 1;
            }

            let a: A = new A();
            let b: B = a;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void SourceWithPrivateMember_NotAssignableToPublicTarget()
    {
        // A class whose `foo` is private cannot be assigned to a target whose `foo` is public —
        // TypeScript relates the property nominally even when the TARGET carries no brand. The
        // conflict is on the source side. (assignmentCompatWithObjectMembersAccessibility)
        var source = """
            class Pub {
                public foo: string = "";
            }
            class Priv {
                private foo: string = "";
            }

            let target: Pub = new Pub();
            let src: Priv = new Priv();
            target = src;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ObjectLiteralTarget_RejectsSourceWithPrivateMember()
    {
        // An anonymous object type is all-public; a class with a private `foo` is not assignable to it.
        var source = """
            class Priv {
                private foo: string = "";
            }

            let target: { foo: string };
            let src: Priv = new Priv();
            target = src;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void InterfaceExtendingClass_SameOriginPrivate_Assignable()
    {
        // `interface I extends Base` inherits Base's private member with Base's identity, so a Base
        // instance IS assignable to the interface (and vice versa) — same declaration. The branded
        // target must not reject the structurally-identical, same-origin source.
        var source = """
            class Base {
                private foo: string = "x";
            }
            interface I extends Base {}

            let b: Base = new Base();
            let i: I = b;
            let back: Base = i;
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void InterfaceExtendingClass_PublicSource_NotAssignable()
    {
        // The interface inherited a PRIVATE `foo` from Base, so an unrelated public-`foo` class is
        // not assignable to it (public cannot satisfy a private member).
        var source = """
            class Base {
                private foo: string = "x";
            }
            interface I extends Base {}
            class Pub {
                public foo: string = "y";
            }

            let i: I;
            let p: Pub = new Pub();
            i = p;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void SameShapeClass_WithExtraMembers_AssignableToSmallerTarget()
    {
        // Width subtyping: a source with extra public members is assignable to an unbranded
        // target that needs only a subset. Issue #129.
        var source = """
            class Detailed {
                constructor(public name: string, public age: number) {}
            }
            class Named {
                constructor(public name: string) {}
            }

            let d: Detailed = new Detailed("Rex", 3);
            let n: Named = d;
            console.log(n.name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex\n", result);
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

    [Fact]
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

    [Fact]
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

    // #756: a class may declare it implements the built-in iterable-protocol interfaces
    // (Iterable<T>, AsyncIterable<T>, …) — validated structurally via [Symbol.iterator]() etc.

    [Fact]
    public void ClassImplementsIterable_Compatible()
    {
        var source = """
            class Range implements Iterable<number> {
                *[Symbol.iterator](): Iterator<number> { yield 1; yield 2; }
            }
            for (const x of new Range()) console.log(x);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", result);
    }

    [Fact]
    public void ClassImplementsAsyncIterable_Compatible()
    {
        var source = """
            class AR implements AsyncIterable<number> {
                async *[Symbol.asyncIterator](): AsyncIterator<number> { yield 1; }
            }
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ClassExpressionImplementsIterable_Compatible()
    {
        var source = """
            const Range = class implements Iterable<number> {
                *[Symbol.iterator](): Iterator<number> { yield 7; }
            };
            for (const x of new Range()) console.log(x);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", result);
    }

    [Fact]
    public void ClassDoesNotImplementIterable_Fails()
    {
        var source = """
            class Bad implements Iterable<number> { x = 1; }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("incorrectly implements", ex.Message);
    }

    [Fact]
    public void ClassImplementsUnknownName_StillFails()
    {
        var source = """
            class C implements NotAThing {}
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("is not an interface", ex.Message);
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

    [Fact]
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
    public void This_InStaticMethod_ResolvesToClass()
    {
        // Per JS spec, `this` inside a static method is the class constructor.
        // Verifies that this.staticField reads/writes the class's static slot and
        // that this.staticMethod() dispatches to sibling static methods.
        var source = """
            class Counter {
                static count: number = 0;
                static increment(): void {
                    this.count++;
                }
                static addVia(n: number): void {
                    for (let i = 0; i < n; i++) this.increment();
                }
            }
            Counter.addVia(3);
            console.log(Counter.count);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", result);
    }

    [Fact]
    public void This_InStaticAccessor_ResolvesToClassConstructor()
    {
        // Per JS spec, `this` inside a static getter/setter is the class constructor,
        // so `new this(...)` is valid — the canonical semver `static get ANY()` pattern.
        var source = """
            class Range {
                raw: string;
                constructor(r: string) { this.raw = r; }
                static get ANY(): Range { return new this("any"); }
            }
            const a = Range.ANY;
            console.log(a.raw);
            console.log(a instanceof Range);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("any\ntrue\n", result);
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
