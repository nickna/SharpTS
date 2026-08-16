using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for the TypeScript 'object' type - any non-primitive value.
/// The object type excludes: string, number, boolean, bigint, symbol, null, undefined.
/// The object type includes: arrays, functions, class instances, records, Map/Set, etc.
/// </summary>
public class ObjectTypeTests
{
    #region Basic Assignment - Valid Cases

    [Fact]
    public void ObjectType_AcceptsObjectLiteral()
    {
        var source = """
            let obj: object = { foo: 1 };
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_AcceptsArray()
    {
        var source = """
            let obj: object = [1, 2, 3];
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_AcceptsClassInstance()
    {
        var source = """
            class Point {
                constructor(public x: number, public y: number) {}
            }
            let obj: object = new Point(1, 2);
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_AcceptsFunction()
    {
        var source = """
            let obj: object = () => {};
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_AcceptsArrowFunction()
    {
        var source = """
            let obj: object = (x: number) => x * 2;
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_AcceptsMap()
    {
        var source = """
            let obj: object = new Map<string, number>();
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_AcceptsSet()
    {
        var source = """
            let obj: object = new Set<number>();
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_AcceptsEmptyObject()
    {
        var source = """
            let obj: object = {};
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    #endregion

    #region Primitive Rejection

    [Fact]
    public void ObjectType_RejectsString()
    {
        var source = """
            let obj: object = "hello";
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ObjectType_RejectsNumber()
    {
        var source = """
            let obj: object = 42;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ObjectType_RejectsBoolean()
    {
        var source = """
            let obj: object = true;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ObjectType_RejectsBigInt()
    {
        var source = """
            let obj: object = 123n;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ObjectType_RejectsSymbol()
    {
        var source = """
            let obj: object = Symbol("test");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Null and Undefined Rejection

    [Fact]
    public void ObjectType_RejectsNull()
    {
        var source = """
            let obj: object = null;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ObjectType_RejectsUndefined()
    {
        var source = """
            let obj: object = undefined;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Object Assignability

    [Fact]
    public void ObjectType_AssignableToAny()
    {
        var source = """
            let obj: object = { x: 1 };
            let a: any = obj;
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_AssignableToUnknown()
    {
        var source = """
            let obj: object = { x: 1 };
            let u: unknown = obj;
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_AssignableToObject()
    {
        var source = """
            let obj1: object = { x: 1 };
            let obj2: object = obj1;
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectType_NotAssignableToSpecificType()
    {
        // object cannot be assigned to more specific types without narrowing
        var source = """
            let obj: object = { x: 1 };
            let arr: number[] = obj;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Union Types

    [Fact]
    public void ObjectOrString_AcceptsObject()
    {
        var source = """
            let x: object | string = { foo: 1 };
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectOrString_AcceptsString()
    {
        var source = """
            let x: object | string = "hello";
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectOrNull_AcceptsNull()
    {
        var source = """
            let x: object | null = null;
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void ObjectOrNull_AcceptsObject()
    {
        var source = """
            let x: object | null = { foo: 1 };
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    #endregion

    #region Generic Constraints

    [Fact]
    public void GenericConstraintObject_AcceptsObjectArgument()
    {
        var source = """
            function acceptObject<T extends object>(value: T): T {
                return value;
            }

            let result = acceptObject({ x: 1 });
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void GenericConstraintObject_AcceptsArrayArgument()
    {
        var source = """
            function acceptObject<T extends object>(value: T): T {
                return value;
            }

            let result = acceptObject([1, 2, 3]);
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void GenericConstraintObject_RejectsPrimitive()
    {
        var source = """
            function acceptObject<T extends object>(value: T): T {
                return value;
            }

            let result = acceptObject(42);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Function Parameters

    [Fact]
    public void FunctionParameter_Object_AcceptsObjectLiteral()
    {
        var source = """
            function process(obj: object): void {
                console.log("processed");
            }

            process({ x: 1, y: 2 });
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("processed\n", result);
    }

    [Fact]
    public void FunctionParameter_Object_RejectsPrimitive()
    {
        var source = """
            function process(obj: object): void {
                console.log("processed");
            }

            process("hello");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void FunctionReturn_Object_CanReturnArray()
    {
        var source = """
            function createObject(): object {
                return [1, 2, 3];
            }

            let obj = createObject();
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    #endregion

    #region Type Narrowing with typeof

    [Fact]
    public void TypeofObject_NarrowsFromUnion()
    {
        var source = """
            function test(x: string | object): string {
                if (typeof x === "object") {
                    return "is object";
                }
                return x;
            }

            console.log(test({ foo: 1 }));
            console.log(test("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("is object\nhello\n", result);
    }

    #endregion
}
