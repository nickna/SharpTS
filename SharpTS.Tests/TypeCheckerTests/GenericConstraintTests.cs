using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for generic type parameter constraints in TypeChecker.Generics.cs.
/// Note: Some generic constraint features are not fully implemented.
/// </summary>
public class GenericConstraintTests
{
    #region Unconstrained Type Parameters

    [Fact]
    public void UnconstrainedTypeParam_AcceptsAnyType()
    {
        var source = """
            function identity<T>(value: T): T {
                return value;
            }

            console.log(identity(42));
            console.log(identity("hello"));
            console.log(identity(true));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\nhello\ntrue\n", result);
    }

    [Fact]
    public void UnconstrainedClass_AcceptsAnyType()
    {
        var source = """
            class Box<T> {
                constructor(public value: T) {}
            }

            let numBox = new Box(42);
            let strBox = new Box("hello");
            console.log(numBox.value);
            console.log(strBox.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\nhello\n", result);
    }

    [Fact]
    public void UnconstrainedClass_WithExplicitTypeArg()
    {
        var source = """
            class Box<T> {
                constructor(public value: T) {}
            }

            let numBox = new Box<number>(42);
            let strBox = new Box<string>("hello");
            console.log(numBox.value);
            console.log(strBox.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\nhello\n", result);
    }

    #endregion

    #region Primitive Constraints (extends)

    [Fact(Skip = "TypeParameter with primitive constraint not recognized for arithmetic operations")]
    public void ExtendsNumber_AcceptsNumber()
    {
        var source = """
            function double<T extends number>(value: T): number {
                return value * 2;
            }

            console.log(double(21));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact(Skip = "TypeParameter with primitive constraint not recognized for method calls")]
    public void ExtendsString_AcceptsString()
    {
        var source = """
            function shout<T extends string>(value: T): string {
                return value.toUpperCase();
            }

            console.log(shout("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\n", result);
    }

    [Fact]
    public void ExtendsNumber_RejectsString()
    {
        var source = """
            function double<T extends number>(value: T): number {
                return value as number * 2;
            }

            double("hello");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Union Constraints

    [Fact(Skip = "Union constraints not fully working with type parameter operations")]
    public void ExtendsUnion_AcceptsUnionMembers()
    {
        var source = """
            function process<T extends string | number>(value: T): string {
                return "" + value;
            }

            console.log(process(42));
            console.log(process("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\nhello\n", result);
    }

    [Fact]
    public void ExtendsUnion_RejectsNonMember()
    {
        var source = """
            function process<T extends string | number>(value: T): string {
                return "" + value;
            }

            process(true);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Interface Constraints

    [Fact(Skip = "Interface constraint on type parameter not fully implemented")]
    public void ExtendsInterface_AcceptsImplementor()
    {
        var source = """
            interface HasLength {
                length: number;
            }

            function getLength<T extends HasLength>(value: T): number {
                return value.length;
            }

            console.log(getLength("hello"));
            console.log(getLength([1, 2, 3]));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n3\n", result);
    }

    [Fact]
    public void ExtendsInterface_RejectsNonImplementor()
    {
        var source = """
            interface HasLength {
                length: number;
            }

            function getLength<T extends HasLength>(value: T): number {
                return value.length;
            }

            getLength(42);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Record/Object Constraints

    [Fact]
    public void ExtendsRecord_AcceptsMatchingObject()
    {
        var source = """
            function getName<T extends { name: string }>(obj: T): string {
                return obj.name;
            }

            console.log(getName({ name: "Alice", age: 30 }));
            console.log(getName({ name: "Bob" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\nBob\n", result);
    }

    [Fact]
    public void ExtendsRecord_RejectsMissingField()
    {
        var source = """
            function getName<T extends { name: string }>(obj: T): string {
                return obj.name;
            }

            getName({ age: 30 });
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Class Constraints

    [Fact]
    public void ExtendsClass_AcceptsClassInstance()
    {
        var source = """
            class Animal {
                constructor(public name: string) {}
            }

            function getName<T extends Animal>(animal: T): string {
                return animal.name;
            }

            console.log(getName(new Animal("Rex")));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex\n", result);
    }

    [Fact]
    public void ExtendsClass_AcceptsSubclass()
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

            function getName<T extends Animal>(animal: T): string {
                return animal.name;
            }

            console.log(getName(new Dog("Rex", "German Shepherd")));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex\n", result);
    }

    #endregion

    #region Multiple Type Parameters

    [Fact]
    public void MultipleTypeParams_Works()
    {
        var source = """
            function pair<T, U>(first: T, second: U): string {
                return "" + first + "," + second;
            }

            console.log(pair("hello", 42));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello,42\n", result);
    }

    #endregion

    #region Generic Class Constraints

    [Fact]
    public void GenericClassConstraintViolation_Fails()
    {
        var source = """
            interface HasId {
                id: number;
            }

            class Repository<T extends HasId> {
                items: T[] = [];
            }

            let repo = new Repository<number>();
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Keyof Constraint

    [Fact(Skip = "Keyof constraint not fully implemented")]
    public void KeyofConstraint_Works()
    {
        var source = """
            function getProperty<T, K extends keyof T>(obj: T, key: K): T[K] {
                return obj[key];
            }

            let person = { name: "Alice", age: 30 };
            console.log(getProperty(person, "name"));
            console.log(getProperty(person, "age"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n30\n", result);
    }

    [Fact]
    public void KeyofConstraint_InvalidKey_Fails()
    {
        var source = """
            function getProperty<T, K extends keyof T>(obj: T, key: K): any {
                return obj[key];
            }

            let person = { name: "Alice", age: 30 };
            getProperty(person, "invalid");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Type Inference with Constraints

    [Fact(Skip = "Type inference with constraints not fully working")]
    public void TypeInference_RespectsConstraint()
    {
        var source = """
            function longest<T extends { length: number }>(a: T, b: T): T {
                return a.length >= b.length ? a : b;
            }

            console.log(longest("hello", "hi"));
            console.log(longest([1, 2, 3], [1]));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n1,2,3\n", result);
    }

    [Fact]
    public void TypeInference_ConstraintMismatch_Fails()
    {
        var source = """
            function longest<T extends { length: number }>(a: T, b: T): T {
                return a.length >= b.length ? a : b;
            }

            longest(42, 100);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Default Type Parameters

    [Fact(Skip = "Default type parameters not yet implemented")]
    public void DefaultTypeParam_UsedWhenNotSpecified()
    {
        var source = """
            class Container<T = string> {
                constructor(public value: T) {}
            }

            let c1 = new Container<number>(42);
            let c2 = new Container("hello");
            console.log(c1.value);
            console.log(c2.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\nhello\n", result);
    }

    #endregion

    #region Recursive Constraints

    [Fact(Skip = "Recursive constraints need special handling")]
    public void RecursiveConstraint_Works()
    {
        var source = """
            interface TreeNode<T extends TreeNode<T>> {
                children: T[];
            }

            class MyNode implements TreeNode<MyNode> {
                children: MyNode[] = [];
            }

            let root = new MyNode();
            root.children.push(new MyNode());
            console.log(root.children.length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n", result);
    }

    #endregion

    #region Constraint with Methods

    [Fact]
    public void ConstraintWithMethod_CanCallMethod()
    {
        var source = """
            interface Stringifiable {
                stringify(): string;
            }

            function convert<T extends Stringifiable>(value: T): string {
                return value.stringify();
            }

            let obj: Stringifiable = {
                stringify: () => "converted"
            };

            console.log(convert(obj));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("converted\n", result);
    }

    #endregion
}
