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

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
    }

    [Fact]
    public void NumberAssignedToString_Fails()
    {
        var source = """
            let x: string = 42;
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
    }

    [Fact]
    public void BooleanAssignedToNumber_Fails()
    {
        var source = """
            let x: number = true;
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
    }

    [Fact]
    public void ObjectAssignedToPrimitive_Fails()
    {
        var source = """
            let x: number = { value: 42 };
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
    }

    [Fact]
    public void ArrayAssignedToNumber_Fails()
    {
        var source = """
            let x: number = [1, 2, 3];
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
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

        DiagnosticAssertions.SingleError(source, "TS2741", 6);
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

        DiagnosticAssertions.SingleError(source, "TS2322", 6);
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

        DiagnosticAssertions.SingleError(source, "TS2339", 6);
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

        DiagnosticAssertions.SingleError(source, "TS2345", 5);
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

        DiagnosticAssertions.SingleError(source, "TS2554", 5);
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

        DiagnosticAssertions.SingleError(source, "TS2554", 5);
    }

    [Fact]
    public void WrongReturnType_Fails()
    {
        var source = """
            function getNumber(): number {
                return "hello";
            }
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 2);
    }

    [Fact]
    public void MissingReturn_InNonVoidFunction_Fails()
    {
        var source = """
            function getNumber(): number {
                console.log("no return");
            }
            """;

        DiagnosticAssertions.SingleError(source, "TS2366", 1);
    }

    #endregion

    #region Array Errors

    [Fact]
    public void WrongElementType_Fails()
    {
        var source = """
            let arr: number[] = [1, 2, "three"];
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
    }

    [Fact]
    public void PushWrongType_Fails()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr.push("four");
            """;

        DiagnosticAssertions.SingleError(source, "TS2345", 2);
    }

    [Fact]
    public void AssignWrongArrayType_Fails()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let strs: string[] = nums;
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 2);
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

        DiagnosticAssertions.SingleError(source, "TS2741", 8);
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

        DiagnosticAssertions.NoErrors(source);
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

        DiagnosticAssertions.SingleError(source, "TS2511", 5);
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

        DiagnosticAssertions.SingleError(source, "TS2515", 5);
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

        DiagnosticAssertions.SingleError(source, "TS4113", 5);
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

        DiagnosticAssertions.SingleError(source, "TS2420", 5);
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

        DiagnosticAssertions.SingleError(source, "TS2420", 5);
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

        DiagnosticAssertions.SingleError(source, "TS2345", 5);
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

        DiagnosticAssertions.SingleError(source, "TS2344", 9);
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

        DiagnosticAssertions.SingleError(source, "TS2345", 5);
    }

    #endregion

    #region Null and Undefined Errors

    [Fact]
    public void NullAssignedToNonNullable_Fails()
    {
        var source = """
            let x: string = null;
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
    }

    [Fact]
    public void UndefinedAssignedToNonOptional_Fails()
    {
        var source = """
            let x: number = undefined;
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
    }

    #endregion

    #region Union Type Errors

    [Fact]
    public void ValueNotInUnion_Fails()
    {
        var source = """
            let x: string | number = true;
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
    }

    [Fact]
    public void UnionToNarrowerType_Fails()
    {
        var source = """
            function narrow(x: string | number): void {
                let y: string = x;
            }
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 2);
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

        DiagnosticAssertions.NoErrors(source);
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

        DiagnosticAssertions.NoErrors(source);
    }

    #endregion

    #region Operator Errors

    [Fact]
    public void ArithmeticOnStrings_Fails()
    {
        var source = """
            let result = "hello" - "world";
            """;

        DiagnosticAssertions.Errors(source, ("TS2363", 1), ("TS2362", 1));
    }

    [Fact]
    public void DivisionOnStrings_Fails()
    {
        var source = """
            let result = "hello" / 2;
            """;

        DiagnosticAssertions.SingleError(source, "TS2362", 1);
    }

    #endregion

    #region Tuple Errors

    [Fact]
    public void TupleWrongElementTypes_Fails()
    {
        var source = """
            let tuple: [string, number] = [42, "hello"];
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 1);
    }

    [Fact]
    public void TupleWrongLength_Fails()
    {
        var source = """
            let tuple: [string, number] = ["hello"];
            """;

        DiagnosticAssertions.SingleError(source, "TS2741", 1);
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

        DiagnosticAssertions.SingleError(source, "TS2769", 7);
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

        DiagnosticAssertions.SingleError(source, "TS1016", 1);
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

        DiagnosticAssertions.SingleError(source, "TS2345", 6);
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

        DiagnosticAssertions.NoErrors(source);
    }

    [Fact]
    public void StringAssignToNegativeIndex_OnNumberArray_Allowed()
    {
        var source = """
            var a: number[] = [0, 1, 2];
            a[-1] = "not an array element";
            """;

        DiagnosticAssertions.NoErrors(source);
    }

    [Fact]
    public void StringAssignToInRangeIndex_OnNumberArray_StillFails()
    {
        var source = """
            var a: number[] = [0, 1, 2];
            a[5] = "still wrong";
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 2);
    }

    [Fact]
    public void StringAssignToMaxValidIndex_OnNumberArray_StillFails()
    {
        var source = """
            var a: number[] = [0, 1, 2];
            a[4294967294] = "still wrong";
            """;

        DiagnosticAssertions.SingleError(source, "TS2322", 2);
    }

    #endregion
}
