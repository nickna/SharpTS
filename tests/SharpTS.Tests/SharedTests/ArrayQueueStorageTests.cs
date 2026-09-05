using SharpTS.Runtime;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests;

// Storage-level coverage: these guards must reject unsupported shapes before
// the interpreter bypasses any observable indexed property operation.
public class ArrayQueueStorageTests
{
    [Fact]
    public void DenseQueueGuardRejectsRestrictedAndHoleyArrays()
    {
        foreach (Action<SharpTSArray> change in new Action<SharpTSArray>[]
        {
            array => array.Seal(), array => array.Freeze(), array => array.PreventExtensions(),
            array => array.DeleteAt(0), array => array.SetLength(3),
            array => array.Set(4, 1d), array => array.Add(ArrayHole.Instance),
            array => _ = array.Elements
        })
        {
            var array = new SharpTSArray(new object?[] { 1d, 2d });
            Assert.True(array.CanUseDenseQueueFastPath(2));
            change(array);
            Assert.False(array.CanUseDenseQueueFastPath(array.LongLength));
        }
    }

    [Fact]
    public void AdoptedBackingCannotBypassHoleGuard()
    {
        var backing = new Deque<object?>(new object?[] { 1d });
        var array = new SharpTSArray(backing);
        backing[0] = ArrayHole.Instance;
        Assert.False(array.CanUseDenseQueueFastPath(1));
    }
}
