using System.Reflection;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ArgumentsIteratorLengthRangeTests
{
    [Fact]
    public void ArgumentsIteratorAdvancesPastInt32Boundary()
    {
        var assembly = Assembly.Load(TestHarness.CompileUncheckedAndVerify("""
            function inspect() { return arguments[Symbol.iterator](); }
            inspect();
            """));
        var arguments = Activator.CreateInstance(assembly.GetType("$Arguments", throwOnError: true)!)!;
        assembly.GetType("$Runtime", throwOnError: true)!
            .GetMethod("SetProperty")!.Invoke(null, [arguments, "length", 2147483649.0]);
        var iteratorType = assembly.GetType("$ArrayIterator", throwOnError: true)!;
        using var iterator = (IEnumerator<object>)Activator.CreateInstance(iteratorType, arguments, 0)!;
        // Seed the index immediately before the boundary to avoid billions of advances.
        iteratorType.GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(iterator, 2147483645.0);

        foreach (double expected in new[] { 2147483646.0, 2147483647.0, 2147483648.0 })
        {
            Assert.True(iterator.MoveNext());
            Assert.Equal(expected, Assert.IsType<double>(iterator.Current));
        }
        Assert.False(iterator.MoveNext());
    }
}
