using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Negative tests that verify the TypeChecker properly detects and reports type errors.
/// These tests verify that invalid code is rejected at type-check time.
/// </summary>
public class TypeErrorTests
{
    #region Basic Type Mismatch Errors

    [Fact]
    public void StringAssignedToNumber_Fails()
    {
        var source = """
            let x: number = "hello";
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void NumberAssignedToString_Fails()
    {
        var source = """
            let x: string = 42;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void BooleanAssignedToNumber_Fails()
    {
        var source = """
            let x: number = true;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ObjectAssignedToPrimitive_Fails()
    {
        var source = """
            let x: number = { value: 42 };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ArrayAssignedToNumber_Fails()
    {
        var source = """
            let x: number = [1, 2, 3];
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Object Property Errors

    [Fact]
    public void MissingRequiredProperty_Fails()
    {
        var source = """
            interface Person {
                name: string;
                age: number;
            }

            let p: Person = { name: "Alice" };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void WrongPropertyType_Fails()
    {
        var source = """
            interface Person {
                name: string;
                age: number;
            }

            let p: Person = { name: "Alice", age: "thirty" };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void AccessingNonExistentProperty_Fails()
    {
        var source = """
            interface Person {
                name: string;
            }

            let p: Person = { name: "Alice" };
            console.log(p.age);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Function Errors

    [Fact]
    public void WrongArgumentType_Fails()
    {
        var source = """
            function greet(name: string): void {
                console.log("Hello, " + name);
            }

            greet(42);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void TooFewArguments_Fails()
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }

            add(1);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void TooManyArguments_Fails()
    {
        var source = """
            function greet(name: string): void {
                console.log("Hello, " + name);
            }

            greet("Alice", "Bob");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void WrongReturnType_Fails()
    {
        var source = """
            function getNumber(): number {
                return "hello";
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void MissingReturn_InNonVoidFunction_Fails()
    {
        var source = """
            function getNumber(): number {
                console.log("no return");
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Array Errors

    [Fact]
    public void WrongElementType_Fails()
    {
        var source = """
            let arr: number[] = [1, 2, "three"];
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void PushWrongType_Fails()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr.push("four");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void AssignWrongArrayType_Fails()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let strs: string[] = nums;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Class Errors

    [Fact]
    public void UnrelatedClassAssignment_Fails()
    {
        var source = """
            class Cat {
                meow(): void {}
            }
            class Dog {
                bark(): void {}
            }

            let cat: Cat = new Dog();
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void EmptySuperclassToEmptySubclass_IsStructurallyCompatible()
    {
        var source = """
            class Animal {}
            class Dog extends Animal {}

            let animal: Animal = new Animal();
            let dog: Dog = animal;
            """;

        var exception = Record.Exception(() => TestHarness.RunInterpreted(source));
        Assert.Null(exception);
    }

    [Fact]
    public void AbstractClassInstantiation_Fails()
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
    public void MissingAbstractMethodImplementation_Fails()
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
    public void OverrideWithoutParent_Fails()
    {
        var source = """
            class Animal {
                eat(): void {}
            }

            class Dog extends Animal {
                override bark(): void {}
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Interface Implementation Errors

    [Fact]
    public void MissingInterfaceMethod_Fails()
    {
        var source = """
            interface Printable {
                print(): void;
            }

            class Document implements Printable {
                // Missing print() method
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void WrongInterfaceMethodSignature_Fails()
    {
        var source = """
            interface Printable {
                print(message: string): void;
            }

            class Document implements Printable {
                print(): void {
                    console.log("printed");
                }
            }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Generic Constraint Errors

    [Fact]
    public void GenericConstraintViolation_Fails()
    {
        var source = """
            function double<T extends number>(value: T): number {
                return value * 2;
            }

            double("hello");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

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

    [Fact]
    public void RecordConstraintMissingField_Fails()
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

    #region Null and Undefined Errors

    [Fact]
    public void NullAssignedToNonNullable_Fails()
    {
        var source = """
            let x: string = null;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void UndefinedAssignedToNonOptional_Fails()
    {
        var source = """
            let x: number = undefined;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Union Type Errors

    [Fact]
    public void ValueNotInUnion_Fails()
    {
        var source = """
            let x: string | number = true;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void UnionToNarrowerType_Fails()
    {
        var source = """
            let x: string | number = "hello";
            let y: string = x;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region This Context Errors

    [Fact]
    public void ThisOutsideClass_TypesAsAny()
    {
        // Per JS spec, `this` at module/function scope is valid (globalThis
        // in sloppy mode, undefined in strict). Type-checker returns Any so
        // CommonJS constructor-function patterns type-check.
        var source = """
            function test(): void {
                const x: any = this;
                console.log(typeof x);
            }
            test();
            """;
        // Should NOT throw a type error now.
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ThisInStaticMethod_TypeChecks()
    {
        // Per JS spec, `this` inside a static method refers to the class constructor,
        // so `this.staticField` is valid. Required for patterns like semver's
        // `static get ANY()` that use `new this(...)`.
        var source = """
            class Counter {
                static count: number = 0;
                static increment(): void {
                    this.count++;
                }
            }
            Counter.increment();
            """;
        TestHarness.RunInterpreted(source);
    }

    #endregion

    #region Operator Errors

    [Fact]
    public void ArithmeticOnStrings_Fails()
    {
        var source = """
            let result = "hello" - "world";
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void DivisionOnStrings_Fails()
    {
        var source = """
            let result = "hello" / 2;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Tuple Errors

    [Fact]
    public void TupleWrongElementTypes_Fails()
    {
        var source = """
            let tuple: [string, number] = [42, "hello"];
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void TupleWrongLength_Fails()
    {
        var source = """
            let tuple: [string, number] = ["hello"];
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Overload Errors

    [Fact]
    public void NoMatchingOverload_Fails()
    {
        var source = """
            function process(value: number): string;
            function process(value: string): number;
            function process(value: number | string): string | number {
                return String(value);
            }

            process(true);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Optional Parameter Errors

    [Fact]
    public void RequiredAfterOptional_Fails()
    {
        var source = """
            function test(a?: number, b: number): void {
                console.log(a, b);
            }
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        // Should error about required param after optional
    }

    #endregion

    #region Keyof Errors

    [Fact]
    public void InvalidKeyof_Fails()
    {
        var source = """
            function getProperty<T, K extends keyof T>(obj: T, key: K): T[K] {
                return obj[key];
            }

            let person = { name: "Alice", age: 30 };
            getProperty(person, "invalid");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Array index range (ECMA-262)

    // ECMA-262 array indices are integers in [0, 2^32 - 2]. Numeric literals
    // outside that range (e.g. 4294967295, -1) are regular property
    // assignments per spec, not array-element writes — so the element-type
    // check must not fire for them. Regression for issue #77.

    [Fact]
    public void StringAssignToOutOfRangeUint32Index_OnNumberArray_Allowed()
    {
        var source = """
            var a: number[] = [0, 1, 2];
            a[4294967295] = "spec-legal";
            """;

        // Type-checker must not reject; runtime may still reject (separate
        // sparse-array layer of work).
        var ex = Record.Exception(() => TestHarness.RunInterpreted(source));
        Assert.False(
            ex is not null && ex.Message.Contains("Type Error"),
            $"unexpected type error: {ex?.Message}");
    }

    [Fact]
    public void StringAssignToNegativeIndex_OnNumberArray_Allowed()
    {
        var source = """
            var a: number[] = [0, 1, 2];
            a[-1] = "not an array element";
            """;

        var ex = Record.Exception(() => TestHarness.RunInterpreted(source));
        Assert.False(
            ex is not null && ex.Message.Contains("Type Error"),
            $"unexpected type error: {ex?.Message}");
    }

    [Fact]
    public void StringAssignToInRangeIndex_OnNumberArray_StillFails()
    {
        var source = """
            var a: number[] = [0, 1, 2];
            a[5] = "still wrong";
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
        Assert.Contains("array of", ex.Message);
    }

    [Fact]
    public void StringAssignToMaxValidIndex_OnNumberArray_StillFails()
    {
        var source = """
            var a: number[] = [0, 1, 2];
            a[4294967294] = "still wrong";
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
        Assert.Contains("array of", ex.Message);
    }

    #endregion
}
