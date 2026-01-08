using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for ES2025 Set operations: union, intersection, difference, symmetricDifference,
/// isSubsetOf, isSupersetOf, isDisjointFrom.
/// </summary>
public class SetOperationsTests
{
    // ========== Union Tests ==========

    [Fact]
    public void Set_Union_CombinesTwoSets()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>([3, 4, 5]);
            let result = a.union(b);
            console.log(result.size);
            console.log(result.has(1));
            console.log(result.has(2));
            console.log(result.has(3));
            console.log(result.has(4));
            console.log(result.has(5));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Set_Union_WithEmptySet()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>();
            let result = a.union(b);
            console.log(result.size);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Set_Union_DoesNotMutateOriginal()
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([3, 4]);
            let result = a.union(b);
            console.log(a.size);
            console.log(b.size);
            console.log(result.size);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n2\n4\n", output);
    }

    // ========== Intersection Tests ==========

    [Fact]
    public void Set_Intersection_ReturnsCommonElements()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3, 4]);
            let b = new Set<number>([3, 4, 5, 6]);
            let result = a.intersection(b);
            console.log(result.size);
            console.log(result.has(3));
            console.log(result.has(4));
            console.log(result.has(1));
            console.log(result.has(5));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\ntrue\ntrue\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Set_Intersection_NoCommonElements()
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([3, 4]);
            let result = a.intersection(b);
            console.log(result.size);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Set_Intersection_WithEmptySet()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>();
            let result = a.intersection(b);
            console.log(result.size);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    // ========== Difference Tests ==========

    [Fact]
    public void Set_Difference_ReturnsElementsOnlyInFirst()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3, 4]);
            let b = new Set<number>([3, 4, 5, 6]);
            let result = a.difference(b);
            console.log(result.size);
            console.log(result.has(1));
            console.log(result.has(2));
            console.log(result.has(3));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\ntrue\ntrue\nfalse\n", output);
    }

    [Fact]
    public void Set_Difference_WithEmptySet()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>();
            let result = a.difference(b);
            console.log(result.size);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Set_Difference_FromEmptySet()
    {
        var source = @"
            let a = new Set<number>();
            let b = new Set<number>([1, 2, 3]);
            let result = a.difference(b);
            console.log(result.size);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    // ========== Symmetric Difference Tests ==========

    [Fact]
    public void Set_SymmetricDifference_ReturnsElementsInEitherButNotBoth()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>([2, 3, 4]);
            let result = a.symmetricDifference(b);
            console.log(result.size);
            console.log(result.has(1));
            console.log(result.has(4));
            console.log(result.has(2));
            console.log(result.has(3));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\ntrue\ntrue\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Set_SymmetricDifference_IdenticalSets()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>([1, 2, 3]);
            let result = a.symmetricDifference(b);
            console.log(result.size);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Set_SymmetricDifference_DisjointSets()
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([3, 4]);
            let result = a.symmetricDifference(b);
            console.log(result.size);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n", output);
    }

    // ========== IsSubsetOf Tests ==========

    [Fact]
    public void Set_IsSubsetOf_ReturnsTrueForSubset()
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([1, 2, 3, 4]);
            console.log(a.isSubsetOf(b));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Set_IsSubsetOf_ReturnsFalseForNonSubset()
    {
        var source = @"
            let a = new Set<number>([1, 2, 5]);
            let b = new Set<number>([1, 2, 3, 4]);
            console.log(a.isSubsetOf(b));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Set_IsSubsetOf_EmptySetIsSubsetOfAll()
    {
        var source = @"
            let a = new Set<number>();
            let b = new Set<number>([1, 2, 3]);
            console.log(a.isSubsetOf(b));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Set_IsSubsetOf_SetIsSubsetOfItself()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            console.log(a.isSubsetOf(a));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    // ========== IsSupersetOf Tests ==========

    [Fact]
    public void Set_IsSupersetOf_ReturnsTrueForSuperset()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3, 4]);
            let b = new Set<number>([1, 2]);
            console.log(a.isSupersetOf(b));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Set_IsSupersetOf_ReturnsFalseForNonSuperset()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>([1, 2, 5]);
            console.log(a.isSupersetOf(b));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Set_IsSupersetOf_AllAreSupersetOfEmpty()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>();
            console.log(a.isSupersetOf(b));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    // ========== IsDisjointFrom Tests ==========

    [Fact]
    public void Set_IsDisjointFrom_ReturnsTrueForDisjointSets()
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([3, 4]);
            console.log(a.isDisjointFrom(b));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Set_IsDisjointFrom_ReturnsFalseForOverlappingSets()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>([3, 4, 5]);
            console.log(a.isDisjointFrom(b));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Set_IsDisjointFrom_EmptySetIsDisjointFromAll()
    {
        var source = @"
            let a = new Set<number>();
            let b = new Set<number>([1, 2, 3]);
            console.log(a.isDisjointFrom(b));
            console.log(b.isDisjointFrom(a));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\n", output);
    }

    // ========== String Set Tests ==========

    [Fact]
    public void Set_Operations_WithStrings()
    {
        var source = @"
            let a = new Set<string>(['apple', 'banana', 'cherry']);
            let b = new Set<string>(['banana', 'cherry', 'date']);

            let union = a.union(b);
            let intersection = a.intersection(b);
            let difference = a.difference(b);

            console.log(union.size);
            console.log(intersection.size);
            console.log(difference.size);
            console.log(difference.has('apple'));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n2\n1\ntrue\n", output);
    }

    // ========== Chaining Tests ==========

    [Fact]
    public void Set_Operations_CanBeChained()
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>([2, 3, 4]);
            let c = new Set<number>([3, 4, 5]);

            // (a union b) intersection c
            let result = a.union(b).intersection(c);
            console.log(result.size);
            console.log(result.has(3));
            console.log(result.has(4));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\ntrue\ntrue\n", output);
    }
}
