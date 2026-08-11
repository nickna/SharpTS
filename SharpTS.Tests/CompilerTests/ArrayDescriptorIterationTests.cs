using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ArrayDescriptorIterationTests
{
    [Fact]
    public void Array_index_accessor_is_observed_by_get_and_reduce()
    {
        const string source = """
            var values = [, 1, 2];
            Object.defineProperty(values, "0", {
              get: function() { return 7; },
              configurable: true
            });
            console.log(values[0]);
            console.log(values.reduce(function(previous, current) {
              return previous + current;
            }));
            """;

        Assert.Equal("7\n10\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Reduce_right_uses_undefined_from_own_setter_only_descriptor()
    {
        const string source = """
            Object.prototype[2] = 2;
            var values = { 0: 0, 1: 1, length: 3 };
            Object.defineProperty(values, "2", {
              set: function(value) {},
              configurable: true
            });
            console.log(typeof values[2]);
            values = Array.prototype.reduceRight.call(values, function(previous, current, index) {
              if (index === 1) console.log(typeof previous);
              return previous;
            });
            """;

        Assert.Equal("undefined\nundefined\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Defining_nonwritable_array_index_initializes_its_backing_slot()
    {
        const string source = """
            var values = [];
            Object.defineProperty(values, "0", { value: 12 });
            values[0] = 99;
            console.log(values[0]);
            console.log(values.length);
            """;

        Assert.Equal("12\n1\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Generic_array_index_descriptor_creates_undefined_over_inherited_value()
    {
        const string source = """
            Object.defineProperty(Array.prototype, "0", {
              value: 11,
              configurable: true
            });
            var values = [];
            Object.defineProperty(values, "0", { configurable: false });
            console.log(typeof values[0]);
            console.log(values.length);
            delete Array.prototype[0];
            """;

        Assert.Equal("undefined\n1\n", TestHarness.RunCompiled(source));
    }
}
