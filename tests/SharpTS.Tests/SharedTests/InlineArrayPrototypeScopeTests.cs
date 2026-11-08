using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InlineArrayPrototypeScopeTests
{
    [Theory]
    [InlineData("[10, 20]", "[1, 2]")]
    [InlineData("arguments", "arguments")]
    public void NestedAbruptCallRestoresReceiverAndThisArgument(string innerReceiver, string outerReceiver)
    {
        var source = $$"""
            const failure = {};
            const callbackThis = {};
            function inner() {
                "use strict";
                Array.prototype.filter.call({{innerReceiver}}, function () { throw failure; }, {});
            }
            function outer() {
                "use strict";
                const original = {{outerReceiver}};
                const result = Array.prototype.map.call(original, function (value, index, receiver) {
                    if (index === 0) {
                        try { inner(10, 20); } catch (error) { console.log(error === failure); }
                        try { Array.prototype.filter.call([30], undefined, {}); }
                        catch (error) { console.log(error instanceof TypeError); }
                    }
                    return [value, receiver === original, this === callbackThis].join(":");
                }, callbackThis);
                console.log(result.join(","));
            }
            outer(1, 2);
            """;

        Assert.Equal("true\ntrue\n1:true:true,2:true:true\n",
            TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }
}
