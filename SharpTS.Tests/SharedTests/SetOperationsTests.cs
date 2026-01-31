using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for ES2025 Set operations: union, intersection, difference, symmetricDifference,
/// isSubsetOf, isSupersetOf, isDisjointFrom. Runs against both interpreter and compiler.
/// </summary>
public class SetOperationsTests
{
    #region Union Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Union_CombinesTwoSets(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Union_WithEmptySet(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>();
            let result = a.union(b);
            console.log(result.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Union_DoesNotMutateOriginal(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([3, 4]);
            let result = a.union(b);
            console.log(a.size);
            console.log(b.size);
            console.log(result.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n2\n4\n", output);
    }

    #endregion

    #region Intersection Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Intersection_ReturnsCommonElements(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\ntrue\ntrue\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Intersection_NoCommonElements(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([3, 4]);
            let result = a.intersection(b);
            console.log(result.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Intersection_WithEmptySet(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>();
            let result = a.intersection(b);
            console.log(result.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    #endregion

    #region Difference Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Difference_ReturnsElementsOnlyInFirst(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\ntrue\ntrue\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Difference_WithEmptySet(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>();
            let result = a.difference(b);
            console.log(result.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Difference_FromEmptySet(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>();
            let b = new Set<number>([1, 2, 3]);
            let result = a.difference(b);
            console.log(result.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    #endregion

    #region Symmetric Difference Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_SymmetricDifference_ReturnsElementsInEitherButNotBoth(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\ntrue\ntrue\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_SymmetricDifference_IdenticalSets(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>([1, 2, 3]);
            let result = a.symmetricDifference(b);
            console.log(result.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_SymmetricDifference_DisjointSets(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([3, 4]);
            let result = a.symmetricDifference(b);
            console.log(result.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    #endregion

    #region IsSubsetOf Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsSubsetOf_ReturnsTrueForSubset(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([1, 2, 3, 4]);
            console.log(a.isSubsetOf(b));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsSubsetOf_ReturnsFalseForNonSubset(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 5]);
            let b = new Set<number>([1, 2, 3, 4]);
            console.log(a.isSubsetOf(b));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsSubsetOf_EmptySetIsSubsetOfAll(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>();
            let b = new Set<number>([1, 2, 3]);
            console.log(a.isSubsetOf(b));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsSubsetOf_SetIsSubsetOfItself(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            console.log(a.isSubsetOf(a));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region IsSupersetOf Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsSupersetOf_ReturnsTrueForSuperset(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 3, 4]);
            let b = new Set<number>([1, 2]);
            console.log(a.isSupersetOf(b));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsSupersetOf_ReturnsFalseForNonSuperset(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>([1, 2, 5]);
            console.log(a.isSupersetOf(b));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsSupersetOf_AllAreSupersetOfEmpty(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>();
            console.log(a.isSupersetOf(b));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region IsDisjointFrom Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsDisjointFrom_ReturnsTrueForDisjointSets(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2]);
            let b = new Set<number>([3, 4]);
            console.log(a.isDisjointFrom(b));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsDisjointFrom_ReturnsFalseForOverlappingSets(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>([1, 2, 3]);
            let b = new Set<number>([3, 4, 5]);
            console.log(a.isDisjointFrom(b));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_IsDisjointFrom_EmptySetIsDisjointFromAll(ExecutionMode mode)
    {
        var source = @"
            let a = new Set<number>();
            let b = new Set<number>([1, 2, 3]);
            console.log(a.isDisjointFrom(b));
            console.log(b.isDisjointFrom(a));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region String Set Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Operations_WithStrings(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n2\n1\ntrue\n", output);
    }

    #endregion

    #region Chaining Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Operations_CanBeChained(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\ntrue\ntrue\n", output);
    }

    #endregion
}
