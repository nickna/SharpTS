using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UnmappedArgumentsIntegrityTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void IndexedDescriptorsHonorSealAndFreeze(ExecutionMode mode)
    {
        const string source = """
            function sealed() {
                "use strict";
                const args = arguments;
                Object.seal(args);
                const index = Object.getOwnPropertyDescriptor(args, "0");
                const length = Object.getOwnPropertyDescriptor(args, "length");
                console.log(index.configurable, index.writable, index.enumerable,
                    length.configurable, length.writable, length.enumerable,
                    Reflect.deleteProperty(args, "0"), Reflect.deleteProperty(args, "length"),
                    Object.hasOwn(args, "0"), Reflect.defineProperty(args, "0", { configurable: true }));
            }
            sealed(1);

            function frozen() {
                "use strict";
                const args = arguments;
                Object.defineProperty(args, "1", { get() { return 2; }, configurable: true });
                Object.freeze(args);
                const index = Object.getOwnPropertyDescriptor(args, "0");
                const accessor = Object.getOwnPropertyDescriptor(args, "1");
                const length = Object.getOwnPropertyDescriptor(args, "length");
                console.log(index.configurable, index.writable, accessor.configurable,
                    length.configurable, length.writable, Reflect.deleteProperty(args, "0"),
                    Reflect.defineProperty(args, "0", { value: 9 }), args[0]);
            }
            frozen(1, 2);
            """;

        Assert.Equal("false true true false true false false false true false\n"
            + "false false false false false false false 1\n", TestHarness.RunUnchecked(source, mode));
    }
}
