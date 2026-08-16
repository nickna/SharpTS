using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for type compatibility rules in TypeChecker.Compatibility.cs.
/// Covers primitive types, literal types, special types, arrays, tuples, unions, and intersections.
/// </summary>
public class TypeCompatibilityTests
{
    #region Primitive Types

    [Theory]
    [InlineData("number", "42")]
    [InlineData("string", "\"hello\"")]
    [InlineData("boolean", "true")]
    public void PrimitiveType_AcceptsSameType(string type, string value)
    {
        var source = $$"""
            let x: {{type}} = {{value}};
            console.log(typeof x);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal($"{type}\n", result);
    }

    [Fact]
    public void NumberToString_IsIncompatible()
    {
        var source = """
            let x: string = 42;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void StringToNumber_IsIncompatible()
    {
        var source = """
            let x: number = "hello";
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void BooleanToNumber_IsIncompatible()
    {
        var source = """
            let x: number = true;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Literal Types

    [Fact]
    public void StringLiteral_WidensToString()
    {
        var source = """
            let x: string = "specific";
            console.log(x);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("specific\n", result);
    }

    [Fact]
    public void NumberLiteral_WidensToNumber()
    {
        var source = """
            let x: number = 42;
            console.log(x);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void ConstDeclaration_PreservesLiteralType()
    {
        // const declarations should preserve literal types
        var source = """
            const x = "hello";
            const y = 42;
            console.log(x);
            console.log(y);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n42\n", result);
    }

    #endregion

    #region Special Types (Any, Never, Unknown)

    [Fact]
    public void AnyType_AcceptsAnyValue()
    {
        var source = """
            let x: any = 42;
            x = "hello";
            x = true;
            x = null;
            console.log("any accepts all");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("any accepts all\n", result);
    }

    [Fact]
    public void AnyType_AssignableToAnyType()
    {
        var source = """
            let x: any = "test";
            let y: number = x;
            let z: string = x;
            console.log("any assignable to all");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("any assignable to all\n", result);
    }

    [Fact]
    public void NeverType_AssignableToAnyType()
    {
        // never (bottom type) should be assignable to anything
        // Note: We throw a plain object since Error class doesn't exist in SharpTS
        var source = """
            function fail(): never {
                throw "fail";
            }

            // This should type-check (never is assignable to number)
            function test(): number {
                return fail();
            }

            console.log("never is bottom type");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("never is bottom type\n", result);
    }

    [Fact]
    public void UnknownType_AcceptsAnyValue()
    {
        var source = """
            let x: unknown = 42;
            x = "hello";
            x = true;
            console.log("unknown accepts all");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("unknown accepts all\n", result);
    }

    #endregion

    #region Null and Undefined

    [Fact]
    public void NullableUnion_AcceptsNull()
    {
        var source = """
            let x: string | null = null;
            console.log(x ?? "default");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("default\n", result);
    }

    [Fact]
    public void NullableUnion_AcceptsValue()
    {
        var source = """
            let x: string | null = "hello";
            console.log(x ?? "default");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void UndefinedUnion_AcceptsUndefined()
    {
        var source = """
            let x: number | undefined = undefined;
            console.log(x ?? 0);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", result);
    }

    [Fact]
    public void NullNotAssignableToNonNullable()
    {
        var source = """
            let x: string = null;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Array Compatibility

    [Fact]
    public void ArrayOfSameType_Compatible()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            console.log(arr.length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", result);
    }

    [Fact]
    public void ArrayWithMixedElements_CompatibleWithUnionArray()
    {
        var source = """
            let arr: (number | string)[] = [1, "two", 3];
            console.log(arr.length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", result);
    }

    [Fact]
    public void NumberArray_NotAssignableToStringArray()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let strs: string[] = nums;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void EmptyArray_CompatibleWithAnyArrayType()
    {
        var source = """
            let nums: number[] = [];
            let strs: string[] = [];
            console.log(nums.length);
            console.log(strs.length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n0\n", result);
    }

    #endregion

    #region Tuple Compatibility

    [Fact]
    public void TupleWithCorrectTypes_Compatible()
    {
        var source = """
            let tuple: [string, number] = ["hello", 42];
            console.log(tuple[0]);
            console.log(tuple[1]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n42\n", result);
    }

    [Fact]
    public void TupleWithWrongTypes_Incompatible()
    {
        var source = """
            let tuple: [string, number] = [42, "hello"];
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void TupleToArray_Compatible()
    {
        // A tuple should be assignable to an array of the union of its element types
        var source = """
            let tuple: [string, number] = ["hello", 42];
            let arr: (string | number)[] = tuple;
            console.log(arr.length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", result);
    }

    [Fact]
    public void TupleWithOptionalElements_HandlesUndefined()
    {
        var source = """
            let tuple: [string, number?] = ["hello"];
            console.log(tuple[0]);
            console.log(tuple.length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n1\n", result);
    }

    #endregion

    #region Union Types

    [Fact]
    public void UnionType_AcceptsAnyMember()
    {
        var source = """
            type NumOrStr = number | string;
            let a: NumOrStr = 42;
            let b: NumOrStr = "hello";
            console.log(typeof a);
            console.log(typeof b);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("number\nstring\n", result);
    }

    [Fact]
    public void NestedUnion_Flattens()
    {
        var source = """
            type Inner = number | string;
            type Outer = Inner | boolean;

            let a: Outer = 1;
            let b: Outer = "two";
            let c: Outer = true;
            console.log(typeof a);
            console.log(typeof b);
            console.log(typeof c);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("number\nstring\nboolean\n", result);
    }

    [Fact]
    public void UnionToUnion_SameMembersDifferentOrder()
    {
        var source = """
            type A = string | number | boolean;
            type B = boolean | string | number;

            let x: A = "hello";
            let y: B = x;
            console.log("union order independent");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("union order independent\n", result);
    }

    [Fact]
    public void UnionSubset_AssignableToSuperset()
    {
        var source = """
            let x: number = 42;
            let y: number | string = x;
            console.log(y);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void UnionSuperset_NotAssignableToSubset()
    {
        // A union with more members cannot be assigned to a narrower type
        var source = """
            let x: number | string = 42;
            let y: number = x;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Intersection Types

    [Fact]
    public void IntersectionType_RequiresAllMembers()
    {
        var source = """
            interface Named {
                name: string;
            }
            interface Aged {
                age: number;
            }

            type Person = Named & Aged;

            let p: Person = { name: "Alice", age: 30 };
            console.log(p.name);
            console.log(p.age);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n30\n", result);
    }

    [Fact]
    public void IntersectionType_MissingMember_Fails()
    {
        var source = """
            interface Named {
                name: string;
            }
            interface Aged {
                age: number;
            }

            type Person = Named & Aged;

            let p: Person = { name: "Alice" };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void IntersectionOfPrimitives_NeverType()
    {
        // Intersection of incompatible types should result in never
        var source = """
            function test(x: string & number): never {
                return x;
            }
            console.log("intersection of primitives");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("intersection of primitives\n", result);
    }

    #endregion

    #region Record/Object Structural Typing

    [Fact]
    public void ObjectLiteral_MatchesRecordType()
    {
        var source = """
            type Point = { x: number; y: number };
            let p: Point = { x: 10, y: 20 };
            console.log(p.x + p.y);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("30\n", result);
    }

    [Fact]
    public void ObjectLiteral_MissingRequiredField_Fails()
    {
        var source = """
            type Point = { x: number; y: number };
            let p: Point = { x: 10 };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ObjectLiteral_ExtraField_Allowed()
    {
        // TypeScript allows excess properties when assigned to a variable first
        var source = """
            type Point = { x: number; y: number };
            let obj = { x: 10, y: 20, z: 30 };
            let p: Point = obj;
            console.log(p.x + p.y);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("30\n", result);
    }

    [Fact]
    public void ObjectLiteral_OptionalField_CanBeOmitted()
    {
        var source = """
            type Config = { name: string; debug?: boolean };
            let c: Config = { name: "app" };
            console.log(c.name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("app\n", result);
    }

    [Fact]
    public void ObjectLiteral_WrongFieldType_Fails()
    {
        var source = """
            type Point = { x: number; y: number };
            let p: Point = { x: "ten", y: 20 };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Interface Structural Typing

    [Fact]
    public void Interface_ObjectLiteralSatisfies()
    {
        var source = """
            interface Printable {
                print(): void;
            }

            let obj: Printable = {
                print: () => console.log("printed")
            };
            obj.print();
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("printed\n", result);
    }

    [Fact]
    public void Interface_ObjectLiteralMissingMethod_Fails()
    {
        var source = """
            interface Printable {
                print(): void;
            }

            let obj: Printable = {
                display: () => console.log("displayed")
            };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void InterfaceToInterface_SubtypeCompatible()
    {
        var source = """
            interface Animal {
                name: string;
            }
            interface Dog extends Animal {
                breed: string;
            }

            let dog: Dog = { name: "Rex", breed: "German Shepherd" };
            let animal: Animal = dog;
            console.log(animal.name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex\n", result);
    }

    #endregion
}
