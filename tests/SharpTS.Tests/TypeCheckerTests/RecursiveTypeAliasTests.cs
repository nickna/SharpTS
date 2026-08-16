using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for recursive type aliases in TypeChecker.
/// Recursive type aliases enable patterns like linked lists, trees, and JSON types.
/// </summary>
public class RecursiveTypeAliasTests
{
    #region Legal Patterns - Non-Generic

    [Fact]
    public void LinkedList_RecursiveThroughObjectProperty_Works()
    {
        var source = """
            type Node = { value: number; next: Node | null };

            const node: Node = { value: 1, next: { value: 2, next: null } };
            console.log(node.value);
            console.log(node.next!.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", result);
    }

    [Fact]
    public void RecursiveAlias_WithNullableProperty_Works()
    {
        var source = """
            type TreeNode = {
                data: string;
                left: TreeNode | null;
                right: TreeNode | null;
            };

            const tree: TreeNode = {
                data: "root",
                left: { data: "left", left: null, right: null },
                right: null
            };
            console.log(tree.data);
            console.log(tree.left!.data);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("root\nleft\n", result);
    }

    [Fact]
    public void RecursiveAlias_WithArray_Works()
    {
        var source = """
            type MenuItem = {
                label: string;
                children: MenuItem[];
            };

            const menu: MenuItem = {
                label: "File",
                children: [
                    { label: "New", children: [] },
                    { label: "Open", children: [] }
                ]
            };
            console.log(menu.label);
            console.log(menu.children.length);
            console.log(menu.children[0].label);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("File\n2\nNew\n", result);
    }

    [Fact]
    public void JsonType_UnionWithArrayAndObject_Works()
    {
        var source = """
            type Json = string | number | boolean | null | Json[] | { [key: string]: Json };

            const jsonValue: Json = {
                name: "test",
                count: 42,
                active: true,
                items: [1, 2, "three"]
            };
            console.log("JSON type works");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("JSON type works\n", result);
    }

    [Fact]
    public void RecursiveAlias_PropertyAccess_Works()
    {
        var source = """
            type Node = { value: number; next: Node | null };

            function getSecondValue(node: Node): number | null {
                if (node.next === null) {
                    return null;
                }
                return node.next.value;
            }

            const list: Node = { value: 1, next: { value: 2, next: null } };
            console.log(getSecondValue(list));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", result);
    }

    [Fact]
    public void RecursiveAlias_AssignmentCompatibility_Works()
    {
        var source = """
            type Node = { value: number; next: Node | null };

            const node1: Node = { value: 1, next: null };
            const node2: Node = { value: 2, next: node1 };
            console.log(node2.value);
            console.log(node2.next!.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n1\n", result);
    }

    #endregion

    #region Legal Patterns - Generic

    [Fact]
    public void BinaryTree_GenericRecursive_Works()
    {
        var source = """
            type Tree<T> = { value: T; children: Tree<T>[] };

            const tree: Tree<string> = {
                value: "root",
                children: [
                    { value: "child1", children: [] },
                    { value: "child2", children: [] }
                ]
            };
            console.log(tree.value);
            console.log(tree.children.length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("root\n2\n", result);
    }

    [Fact]
    public void RecursiveGenericAlias_TypeSubstitution_Works()
    {
        var source = """
            type Container<T> = { item: T; nested: Container<T> | null };

            const numContainer: Container<number> = {
                item: 42,
                nested: { item: 100, nested: null }
            };
            console.log(numContainer.item);
            console.log(numContainer.nested!.item);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n100\n", result);
    }

    [Fact]
    public void RecursiveAlias_InFunctionReturn_Works()
    {
        var source = """
            type Node<T> = { value: T; next: Node<T> | null };

            function createNode<T>(value: T): Node<T> {
                return { value: value, next: null };
            }

            const node = createNode("hello");
            console.log(node.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void RecursiveAlias_InFunctionParameter_Works()
    {
        var source = """
            type Node = { value: number; next: Node | null };

            function traverse(node: Node): void {
                console.log(node.value);
                if (node.next !== null) {
                    traverse(node.next);
                }
            }

            const list: Node = { value: 1, next: { value: 2, next: { value: 3, next: null } } };
            traverse(list);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", result);
    }

    #endregion

    #region Illegal Patterns - Should Fail

    [Fact]
    public void DirectSelfReference_Fails()
    {
        var source = """
            type Bad = Bad;
            const x: Bad = 1;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("circularly references itself", ex.Message);
    }

    [Fact]
    public void MutualDirectReference_Fails()
    {
        var source = """
            type A = B;
            type B = A;
            const x: A = 1;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("circularly references itself", ex.Message);
    }

    [Fact]
    public void UnionOfOnlySelfReferences_Fails()
    {
        var source = """
            type Bad = Bad | Bad;
            const x: Bad = 1;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("circularly references itself", ex.Message);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void DeeplyNestedRecursion_DoesNotStackOverflow()
    {
        var source = """
            type DeepNode = { value: number; child: DeepNode | null };

            function buildDeepList(depth: number): DeepNode {
                if (depth <= 0) {
                    return { value: 0, child: null };
                }
                return { value: depth, child: buildDeepList(depth - 1) };
            }

            const deep = buildDeepList(50);
            console.log(deep.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("50\n", result);
    }

    [Fact]
    public void RecursiveAlias_WithOptionalProperty_Works()
    {
        var source = """
            type Node = { value: number; next?: Node };

            const node: Node = { value: 1 };
            console.log(node.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n", result);
    }

    [Fact]
    public void RecursiveAlias_WithMultipleRecursiveProperties_Works()
    {
        var source = """
            type DoublyLinked = {
                value: number;
                prev: DoublyLinked | null;
                next: DoublyLinked | null;
            };

            const node: DoublyLinked = {
                value: 1,
                prev: null,
                next: { value: 2, prev: null, next: null }
            };
            console.log(node.value);
            console.log(node.next!.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", result);
    }

    #endregion
}
