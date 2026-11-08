using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UnmappedArgumentsIntegrityTests
{
    [Fact]
    public void CompiledUnmappedArgumentsOwnTheProtectedCalleeAccessor()
    {
        const string source = """
            function inspect(args) {
                "use strict";
                const descriptor = Object.getOwnPropertyDescriptor(args, "callee");
                console.log(typeof descriptor.get, descriptor.get === descriptor.set,
                    descriptor.enumerable, descriptor.configurable);
                Object.seal(args);
                try { delete args["callee"]; }
                catch (error) { console.log(error instanceof TypeError); }
                try { console.log(args["callee"]); }
                catch (error) { console.log(error instanceof TypeError); }
                try { args["callee"] = 1; }
                catch (error) { console.log(error instanceof TypeError); }
            }

            function strict() { "use strict"; inspect(arguments); }
            function rest(...values) { inspect(arguments); }
            function derived(parent) {
                class Derived extends parent {
                    constructor() { super(); inspect(arguments); }
                }
                return Derived;
            }
            strict();
            rest();
            const Derived = derived(class {});
            new Derived();
            """;

        const string expected = "function true false false\ntrue\ntrue\ntrue\n";
        Assert.Equal(expected + expected + expected, TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void StrictDeletionOfMissingIndicesSucceedsAfterSealOrFreeze(ExecutionMode mode)
    {
        const string source = """
            function remove(args, key) {
                "use strict";
                try { return delete args[key]; }
                catch (error) { return error instanceof TypeError ? "TypeError" : "unexpected"; }
            }

            function check(integrity, first, second) {
                "use strict";
                const args = arguments;
                delete args[1];
                Object.setPrototypeOf(args, { 1: "inherited" });
                Object.defineProperty(args, "10", { value: "stored", configurable: true });
                Object.defineProperty(args, "named", { value: "stored", configurable: true });
                integrity(args);
                console.log(remove(args, 7), remove(args, 1), remove(args, "01"), args[1]);
                console.log(remove(args, 0), remove(args, 10), remove(args, "named"), remove(args, "length"));
            }

            check(Object.seal, 1, 2);
            check(Object.freeze, 1, 2);
            """;

        const string expected = "true true true inherited\nTypeError TypeError TypeError TypeError\n";
        Assert.Equal(expected + expected, TestHarness.RunUnchecked(source, mode));
    }

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
