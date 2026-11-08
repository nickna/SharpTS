using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FrozenArrayDescriptorTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void IndexedAndLengthDescriptorsReflectIntegrityLevel(ExecutionMode mode)
    {
        const string source = """
            const array = [1, , 3];
            array[4294967294] = 4;
            Object.defineProperty(array, "2", {
                get() { throw new Error("getter called during inspection"); },
                enumerable: true,
                configurable: true
            });
            Object.freeze(array);
            const index = Object.getOwnPropertyDescriptor(array, "0");
            const length = Object.getOwnPropertyDescriptor(array, "length");
            const sparse = Reflect.getOwnPropertyDescriptor(array, "4294967294");
            const accessor = Reflect.getOwnPropertyDescriptor(array, "2");
            console.log(index.writable, index.configurable, length.writable);
            console.log(sparse.value, sparse.writable, sparse.enumerable, sparse.configurable);
            console.log(typeof accessor.get, accessor.enumerable, accessor.configurable);
            console.log(Object.getOwnPropertyDescriptor(array, "1") === undefined);

            const sealed = Object.seal([5]);
            const sealedIndex = Object.getOwnPropertyDescriptor(sealed, "0");
            const sealedLength = Object.getOwnPropertyDescriptor(sealed, "length");
            console.log(sealedIndex.writable, sealedIndex.configurable, sealedLength.writable);
            """;

        Assert.Equal("false false false\n4 false true false\nfunction true false\ntrue\ntrue false true\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
