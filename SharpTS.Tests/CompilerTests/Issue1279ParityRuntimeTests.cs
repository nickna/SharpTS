using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class Issue1279ParityRuntimeTests
{
    [Fact]
    public void Strict_array_index_writes_observe_accessors_and_in_reports_them()
    {
        const string source = """
            var values: any = [, 2];
            Object.defineProperty(values, "0", {
              get: function() { return this.saved; },
              set: function(value) { this.saved = value; }
            });
            Array.prototype.sort.call(values);
            console.log(values.saved);
            console.log(values[0]);
            console.log("0" in values);
            """;

        Assert.Equal("2\n2\ntrue\n", TestHarness.RunCompiled(source));
    }
}
