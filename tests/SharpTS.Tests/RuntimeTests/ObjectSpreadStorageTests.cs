using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

public class ObjectSpreadStorageTests
{
    [Fact]
    public void PlainDataCopy_DoesNotAllocateTemporaryStorage()
    {
        var source = new SharpTSObject(new() { ["a"] = 1d, ["b"] = true, ["c"] = "three" });
        var target = new Dictionary<string, object?>(4);
        Assert.True(source.TryCopyPlainSpreadFields(target));
        bool success = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) success &= source.TryCopyPlainSpreadFields(target);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, allocated);
        Assert.Equal("three", target["c"]);
    }

    [Fact]
    public void NumericKeys_RejectFastCopyBeforeWritingAnything()
    {
        var source = new SharpTSObject(new() { ["a"] = 1d, ["2"] = 2d });
        var target = new Dictionary<string, object?> { ["original"] = 10d };
        Assert.False(source.TryCopyPlainSpreadFields(target));
        Assert.Single(target);
        Assert.Equal(10d, target["original"]);
    }
}
