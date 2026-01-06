using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class MapSetTests
{
    // ========== Map Constructor Tests ==========

    [Fact]
    public void Map_EmptyConstructor_CreatesEmptyMap()
    {
        var source = @"
            let m = new Map<string, number>();
            console.log(m.size);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Map_FromEntries_CreatesMapWithValues()
    {
        var source = @"
            let m = new Map<string, number>([['a', 1], ['b', 2], ['c', 3]]);
            console.log(m.size);
            console.log(m.get('a'));
            console.log(m.get('b'));
            console.log(m.get('c'));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    // ========== Map Basic Operations ==========

    [Fact]
    public void Map_SetAndGet_WorksCorrectly()
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('key1', 100);
            m.set('key2', 200);
            console.log(m.get('key1'));
            console.log(m.get('key2'));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n200\n", output);
    }

    [Fact]
    public void Map_Has_ChecksKeyExistence()
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('exists', 42);
            console.log(m.has('exists'));
            console.log(m.has('missing'));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Map_Delete_RemovesKey()
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('key', 123);
            console.log(m.has('key'));
            console.log(m.delete('key'));
            console.log(m.has('key'));
            console.log(m.delete('key'));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Map_Clear_RemovesAllEntries()
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('a', 1);
            m.set('b', 2);
            console.log(m.size);
            m.clear();
            console.log(m.size);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n0\n", output);
    }

    [Fact]
    public void Map_Set_ReturnsMapForChaining()
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('a', 1).set('b', 2).set('c', 3);
            console.log(m.size);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    // ========== Map Iteration ==========

    [Fact]
    public void Map_ForOf_IteratesEntries()
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('x', 10);
            m.set('y', 20);
            for (let entry of m) {
                console.log(entry[0] + '=' + entry[1]);
            }
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("x=10", output);
        Assert.Contains("y=20", output);
    }

    [Fact]
    public void Map_Keys_ReturnsKeyIterator()
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('first', 1);
            m.set('second', 2);
            for (let key of m.keys()) {
                console.log(key);
            }
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("first", output);
        Assert.Contains("second", output);
    }

    [Fact]
    public void Map_Values_ReturnsValueIterator()
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('a', 100);
            m.set('b', 200);
            for (let value of m.values()) {
                console.log(value);
            }
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("100", output);
        Assert.Contains("200", output);
    }

    [Fact]
    public void Map_Entries_ReturnsEntryIterator()
    {
        var source = @"
            let m = new Map<string, number>();
            m.set('p', 5);
            m.set('q', 10);
            for (let entry of m.entries()) {
                console.log(entry[0] + '->' + entry[1]);
            }
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("p->5", output);
        Assert.Contains("q->10", output);
    }

    // ========== Set Constructor Tests ==========

    [Fact]
    public void Set_EmptyConstructor_CreatesEmptySet()
    {
        var source = @"
            let s = new Set<number>();
            console.log(s.size);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Set_FromArray_CreatesSetWithValues()
    {
        var source = @"
            let s = new Set<number>([1, 2, 3, 2, 1]);
            console.log(s.size);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    // ========== Set Basic Operations ==========

    [Fact]
    public void Set_Add_InsertsValues()
    {
        var source = @"
            let s = new Set<string>();
            s.add('apple');
            s.add('banana');
            console.log(s.size);
            console.log(s.has('apple'));
            console.log(s.has('cherry'));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\ntrue\nfalse\n", output);
    }

    [Fact]
    public void Set_Add_IgnoresDuplicates()
    {
        var source = @"
            let s = new Set<number>();
            s.add(1);
            s.add(2);
            s.add(1);
            s.add(2);
            console.log(s.size);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Set_Delete_RemovesValue()
    {
        var source = @"
            let s = new Set<string>();
            s.add('remove-me');
            console.log(s.has('remove-me'));
            console.log(s.delete('remove-me'));
            console.log(s.has('remove-me'));
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Fact]
    public void Set_Clear_RemovesAllValues()
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n0\n", output);
    }

    [Fact]
    public void Set_Add_ReturnsSetForChaining()
    {
        var source = @"
            let s = new Set<number>();
            s.add(1).add(2).add(3);
            console.log(s.size);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    // ========== Set Iteration ==========

    [Fact]
    public void Set_ForOf_IteratesValues()
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
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("a", output);
        Assert.Contains("b", output);
        Assert.Contains("c", output);
    }

    [Fact]
    public void Set_Values_ReturnsValueIterator()
    {
        var source = @"
            let s = new Set<number>();
            s.add(5);
            s.add(10);
            for (let v of s.values()) {
                console.log(v);
            }
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("5", output);
        Assert.Contains("10", output);
    }

    [Fact]
    public void Set_Keys_IsSameAsValues()
    {
        var source = @"
            let s = new Set<number>();
            s.add(1);
            s.add(2);
            for (let k of s.keys()) {
                console.log(k);
            }
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("1", output);
        Assert.Contains("2", output);
    }

    [Fact]
    public void Set_Entries_ReturnsPairs()
    {
        var source = @"
            let s = new Set<string>();
            s.add('x');
            s.add('y');
            for (let entry of s.entries()) {
                console.log(entry[0] + '=' + entry[1]);
            }
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Contains("x=x", output);
        Assert.Contains("y=y", output);
    }

    // ========== Type Inference Tests ==========

    [Fact]
    public void Map_WithoutTypeArgs_DefaultsToAny()
    {
        var source = @"
            let m = new Map();
            m.set('string', 123);
            m.set(456, 'number');
            console.log(m.size);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Set_WithoutTypeArgs_DefaultsToAny()
    {
        var source = @"
            let s = new Set();
            s.add(1);
            s.add('two');
            s.add(true);
            console.log(s.size);
        ";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }
}
