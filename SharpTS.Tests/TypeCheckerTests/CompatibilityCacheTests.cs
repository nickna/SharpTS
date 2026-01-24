using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests that verify IsCompatible memoization cache correctness.
/// These tests use complex union/intersection types that would trigger
/// many recursive IsCompatible calls, exercising the caching mechanism.
/// </summary>
public class CompatibilityCacheTests
{
    [Fact]
    public void UnionToUnion_AssignmentWorksCorrectly()
    {
        // Union-to-union comparison triggers O(n*m) IsCompatible calls
        // The cache should prevent redundant work while maintaining correctness
        var source = """
            type A = string | number | boolean;
            type B = number | boolean | string;

            let x: A = "hello";
            let y: B = 42;

            // These assignments should work (same types, different order)
            let a: A = y;
            let b: B = x;

            console.log("union-to-union works");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("union-to-union works\n", result);
    }

    [Fact]
    public void NestedUnions_FlattenAndCompareCorrectly()
    {
        // Nested unions should be flattened and compared correctly
        var source = """
            type Inner = string | number;
            type Outer = Inner | boolean;

            let x: Outer = "test";
            let y: Outer = 123;
            let z: Outer = true;

            console.log(typeof x);
            console.log(typeof y);
            console.log(typeof z);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("string\nnumber\nboolean\n", result);
    }

    [Fact]
    public void UnionWithNull_HandlesNullabilityCorrectly()
    {
        // Unions with null should cache correctly
        var source = """
            type Nullable = string | null;

            let a: Nullable = "hello";
            let b: Nullable = null;

            console.log(a ?? "default");
            console.log(b ?? "default");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\ndefault\n", result);
    }

    [Fact]
    public void RepeatedTypeChecks_ProduceConsistentResults()
    {
        // Multiple assignments of same types should all succeed
        // Tests that cached results are correct
        var source = """
            type NumOrStr = number | string;

            let a: NumOrStr = 1;
            let b: NumOrStr = "two";
            let c: NumOrStr = 3;
            let d: NumOrStr = "four";
            let e: NumOrStr = 5;

            console.log(a);
            console.log(b);
            console.log(c);
            console.log(d);
            console.log(e);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\ntwo\n3\nfour\n5\n", result);
    }

    [Fact]
    public void GenericWithUnion_ResolvesCorrectly()
    {
        // Generic types with union constraints exercise complex compatibility paths
        var source = """
            function process<T extends string | number>(value: T): T {
                return value;
            }

            let a = process(42);
            let b = process("hello");

            console.log(a);
            console.log(b);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\nhello\n", result);
    }

    [Fact]
    public void SelfReferentialInterface_DoesNotInfiniteLoop()
    {
        // Self-referential types could cause infinite loops without proper handling
        var source = """
            interface TreeNode {
                value: number;
                children: TreeNode[];
            }

            let node: TreeNode = {
                value: 1,
                children: [
                    { value: 2, children: [] },
                    { value: 3, children: [] }
                ]
            };

            console.log(node.value);
            console.log(node.children.length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", result);
    }

    [Fact]
    public void FunctionTypeCompatibility_CachesParameterChecks()
    {
        // Function type compatibility involves checking all parameter types
        var source = """
            type Handler = (a: string, b: number) => boolean;

            let handler: Handler = (x: string, y: number) => {
                return x.length > y;
            };

            console.log(handler("hello", 3));
            console.log(handler("hi", 5));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("true\nfalse\n", result);
    }

    [Fact]
    public void ArrayOfUnions_ChecksElementTypeCorrectly()
    {
        // Array element type checking against unions
        var source = """
            type Value = string | number;
            let arr: Value[] = [1, "two", 3, "four"];

            for (let v of arr) {
                console.log(v);
            }
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\ntwo\n3\nfour\n", result);
    }

    [Fact]
    public void IntersectionTypes_CacheAllMemberChecks()
    {
        // Intersection types require checking all members of all intersected types
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
    public void UnionOfObjects_StructuralChecksCached()
    {
        // Union of object types requires structural compatibility checks
        var source = """
            interface Cat {
                meow: () => void;
            }

            interface Dog {
                bark: () => void;
            }

            type Pet = Cat | Dog;

            let cat: Pet = {
                meow: () => console.log("meow")
            };

            let dog: Pet = {
                bark: () => console.log("woof")
            };

            if ("meow" in cat) {
                cat.meow();
            }
            if ("bark" in dog) {
                dog.bark();
            }
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("meow\nwoof\n", result);
    }

    [Theory]
    [InlineData("string | number", "string")]
    [InlineData("string | number", "number")]
    [InlineData("string | number | boolean", "boolean")]
    public void UnionAssignments_CompileAndRun(string unionType, string assignedType)
    {
        // Test various union assignments compile and run correctly
        var source = $$"""
            type U = {{unionType}};
            let x: U = {{(assignedType == "string" ? "\"test\"" : assignedType == "number" ? "42" : "true")}};
            console.log(typeof x);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal($"{assignedType}\n", result);
    }

    // Unit tests for TypeInfoEqualityComparer behavior with List-containing TypeInfo types

    [Fact]
    public void TypeInfoEqualityComparer_HandlesUnionTypesWithEquivalentMembers()
    {
        // Two Union instances with same member types but different List instances
        var union1 = new TypeInfo.Union([
            new TypeInfo.String(),
            new TypeInfo.Primitive(TokenType.TYPE_NUMBER)
        ]);
        var union2 = new TypeInfo.Union([
            new TypeInfo.String(),
            new TypeInfo.Primitive(TokenType.TYPE_NUMBER)
        ]);

        // Record equality would fail (different List instances)
        // But TypeInfoEqualityComparer should treat them as equal
        Assert.True(TypeInfoEqualityComparer.Instance.Equals(union1, union2));
        Assert.Equal(
            TypeInfoEqualityComparer.Instance.GetHashCode(union1),
            TypeInfoEqualityComparer.Instance.GetHashCode(union2)
        );
    }

    [Fact]
    public void TypeInfoEqualityComparer_HandlesFunctionTypesWithEquivalentParams()
    {
        var func1 = new TypeInfo.Function(
            [new TypeInfo.String(), new TypeInfo.Primitive(TokenType.TYPE_NUMBER)],
            new TypeInfo.Void()
        );
        var func2 = new TypeInfo.Function(
            [new TypeInfo.String(), new TypeInfo.Primitive(TokenType.TYPE_NUMBER)],
            new TypeInfo.Void()
        );

        Assert.True(TypeInfoEqualityComparer.Instance.Equals(func1, func2));
        Assert.Equal(
            TypeInfoEqualityComparer.Instance.GetHashCode(func1),
            TypeInfoEqualityComparer.Instance.GetHashCode(func2)
        );
    }

    [Fact]
    public void TypeInfoEqualityComparer_DistinguishesDifferentUnionTypes()
    {
        var union1 = new TypeInfo.Union([new TypeInfo.String()]);
        var union2 = new TypeInfo.Union([new TypeInfo.Primitive(TokenType.TYPE_NUMBER)]);

        Assert.False(TypeInfoEqualityComparer.Instance.Equals(union1, union2));
    }

    [Fact]
    public void TypeInfoEqualityComparer_HandlesIntersectionTypes()
    {
        var intersection1 = new TypeInfo.Intersection([
            new TypeInfo.String(),
            new TypeInfo.Primitive(TokenType.TYPE_NUMBER)
        ]);
        var intersection2 = new TypeInfo.Intersection([
            new TypeInfo.String(),
            new TypeInfo.Primitive(TokenType.TYPE_NUMBER)
        ]);

        Assert.True(TypeInfoEqualityComparer.Instance.Equals(intersection1, intersection2));
        Assert.Equal(
            TypeInfoEqualityComparer.Instance.GetHashCode(intersection1),
            TypeInfoEqualityComparer.Instance.GetHashCode(intersection2)
        );
    }

    [Fact]
    public void TypeInfoEqualityComparer_HandlesTupleTypes()
    {
        var tuple1 = new TypeInfo.Tuple([
            new TypeInfo.TupleElement(new TypeInfo.String(), TupleElementKind.Required),
            new TypeInfo.TupleElement(new TypeInfo.Primitive(TokenType.TYPE_NUMBER), TupleElementKind.Required)
        ], RequiredCount: 2);
        var tuple2 = new TypeInfo.Tuple([
            new TypeInfo.TupleElement(new TypeInfo.String(), TupleElementKind.Required),
            new TypeInfo.TupleElement(new TypeInfo.Primitive(TokenType.TYPE_NUMBER), TupleElementKind.Required)
        ], RequiredCount: 2);

        Assert.True(TypeInfoEqualityComparer.Instance.Equals(tuple1, tuple2));
        Assert.Equal(
            TypeInfoEqualityComparer.Instance.GetHashCode(tuple1),
            TypeInfoEqualityComparer.Instance.GetHashCode(tuple2)
        );
    }
}
