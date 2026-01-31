using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Map and Set collections. Runs against both interpreter and compiler.
/// </summary>
public class MapSetTests
{
    #region Map Constructor Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_EmptyConstructor_CreatesEmptyMap(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            console.log(m.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_FromEntries_CreatesMapWithValues(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>([['a', 1], ['b', 2], ['c', 3]]);
            console.log(m.size);
            console.log(m.get('a'));
            console.log(m.get('b'));
            console.log(m.get('c'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    #endregion

    #region Map Basic Operations

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_SetAndGet_WorksCorrectly(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('key1', 100);
            m.set('key2', 200);
            console.log(m.get('key1'));
            console.log(m.get('key2'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_Has_ChecksKeyExistence(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('exists', 42);
            console.log(m.has('exists'));
            console.log(m.has('missing'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_Delete_RemovesKey(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('key', 123);
            console.log(m.has('key'));
            console.log(m.delete('key'));
            console.log(m.has('key'));
            console.log(m.delete('key'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_Clear_RemovesAllEntries(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('a', 1);
            m.set('b', 2);
            console.log(m.size);
            m.clear();
            console.log(m.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_Set_ReturnsMapForChaining(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('a', 1).set('b', 2).set('c', 3);
            console.log(m.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    #endregion

    #region Map Iteration

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_ForEach_IteratesAllEntries(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('a', 1);
            m.set('b', 2);
            m.forEach((value, key) => {
                console.log(key + ': ' + value);
            });
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("a: 1", output);
        Assert.Contains("b: 2", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_ForOf_IteratesEntries(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('x', 10);
            m.set('y', 20);
            for (let entry of m) {
                console.log(entry[0] + '=' + entry[1]);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("x=10", output);
        Assert.Contains("y=20", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_Keys_ReturnsKeyIterator(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('first', 1);
            m.set('second', 2);
            for (let key of m.keys()) {
                console.log(key);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("first", output);
        Assert.Contains("second", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_Values_ReturnsValueIterator(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('a', 100);
            m.set('b', 200);
            for (let value of m.values()) {
                console.log(value);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("100", output);
        Assert.Contains("200", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_Entries_ReturnsEntryIterator(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('p', 5);
            m.set('q', 10);
            for (let entry of m.entries()) {
                console.log(entry[0] + '->' + entry[1]);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("p->5", output);
        Assert.Contains("q->10", output);
    }

    #endregion

    #region Map Object Key Reference Equality

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_ObjectKeys_UseReferenceEquality(ExecutionMode mode)
    {
        var source = @"
            let m = new Map();
            let obj1 = { x: 1 };
            let obj2 = { x: 1 };  // Same content, different reference

            m.set(obj1, 'first');
            console.log(m.has(obj1));  // true - same reference
            console.log(m.has(obj2));  // false - different reference
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_StringKeys_UseValueEquality(ExecutionMode mode)
    {
        var source = @"
            let m = new Map<string, number>();
            let key1 = 'hello';
            let key2 = 'hello';

            m.set(key1, 42);
            console.log(m.has(key2));  // true - strings use value equality
            console.log(m.get(key2));  // 42
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n42\n", output);
    }

    #endregion

    #region Set Constructor Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_EmptyConstructor_CreatesEmptySet(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<number>();
            console.log(s.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_FromArray_CreatesSetWithValues(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<number>([1, 2, 3, 2, 1]);  // Duplicates removed
            console.log(s.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    #endregion

    #region Set Basic Operations

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Add_InsertsValues(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<string>();
            s.add('apple');
            s.add('banana');
            console.log(s.size);
            console.log(s.has('apple'));
            console.log(s.has('cherry'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\ntrue\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Add_IgnoresDuplicates(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<number>();
            s.add(1);
            s.add(2);
            s.add(1);  // Duplicate
            s.add(2);  // Duplicate
            console.log(s.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Delete_RemovesValue(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<string>();
            s.add('remove-me');
            console.log(s.has('remove-me'));
            console.log(s.delete('remove-me'));
            console.log(s.has('remove-me'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Clear_RemovesAllValues(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<number>();
            s.add(1);
            s.add(2);
            s.add(3);
            console.log(s.size);
            s.clear();
            console.log(s.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Add_ReturnsSetForChaining(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<number>();
            s.add(1).add(2).add(3);
            console.log(s.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    #endregion

    #region Set Iteration

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_ForEach_IteratesAllValues(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<number>();
            s.add(10);
            s.add(20);
            s.add(30);
            s.forEach((value) => {
                console.log(value);
            });
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("10", output);
        Assert.Contains("20", output);
        Assert.Contains("30", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_ForOf_IteratesValues(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<string>();
            s.add('a');
            s.add('b');
            s.add('c');
            for (let value of s) {
                console.log(value);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("a", output);
        Assert.Contains("b", output);
        Assert.Contains("c", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Values_ReturnsValueIterator(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<number>();
            s.add(5);
            s.add(10);
            for (let v of s.values()) {
                console.log(v);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("5", output);
        Assert.Contains("10", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Keys_IsSameAsValues(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<number>();
            s.add(1);
            s.add(2);
            for (let k of s.keys()) {
                console.log(k);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("1", output);
        Assert.Contains("2", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_Entries_ReturnsPairs(ExecutionMode mode)
    {
        var source = @"
            let s = new Set<string>();
            s.add('x');
            s.add('y');
            for (let entry of s.entries()) {
                console.log(entry[0] + '=' + entry[1]);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("x=x", output);
        Assert.Contains("y=y", output);
    }

    #endregion

    #region Set Object Reference Equality

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_ObjectValues_UseReferenceEquality(ExecutionMode mode)
    {
        var source = @"
            let s = new Set();
            let obj1 = { id: 1 };
            let obj2 = { id: 1 };  // Same content, different reference

            s.add(obj1);
            console.log(s.has(obj1));  // true - same reference
            console.log(s.has(obj2));  // false - different reference
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    #endregion

    #region Type Inference Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Map_WithoutTypeArgs_DefaultsToAny(ExecutionMode mode)
    {
        var source = @"
            let m = new Map();
            m.set('string', 123);
            m.set(456, 'number');
            console.log(m.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Set_WithoutTypeArgs_DefaultsToAny(ExecutionMode mode)
    {
        var source = @"
            let s = new Set();
            s.add(1);
            s.add('two');
            s.add(true);
            console.log(s.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    #endregion
}
