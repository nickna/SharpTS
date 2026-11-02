using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BoundNullishReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["sloppy-undefined-declaration", "function read(this:any){console.log(this===globalThis)}read.bind(undefined)();", "true\n"];
        yield return ["sloppy-null-declaration", "function read(this:any){console.log(this===globalThis)}read.bind(null)();", "true\n"];
        yield return ["sloppy-undefined-expression", "const read=function(this:any){console.log(this===globalThis)};read.bind(undefined)();", "true\n"];
        yield return ["sloppy-null-expression", "const read=function(this:any){console.log(this===globalThis)};read.bind(null)();", "true\n"];
        yield return ["strict-undefined-declaration", "function read(this:any){'use strict';console.log(this===undefined)}read.bind(undefined)();", "true\n"];
        yield return ["strict-null-declaration", "function read(this:any){'use strict';console.log(this===null)}read.bind(null)();", "true\n"];
        yield return ["strict-undefined-expression", "const read=function(this:any){'use strict';console.log(this===undefined)};read.bind(undefined)();", "true\n"];
        yield return ["strict-null-expression", "const read=function(this:any){'use strict';console.log(this===null)};read.bind(null)();", "true\n"];
        yield return ["first-binding", "function read(this:any){'use strict';console.log(this===null)}read.bind(null).bind({})();", "true\n"];
    }

    public static IEnumerable<object[]> DualCases()
    {
        foreach (var row in Cases())
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [.. row, mode];
    }

    [Theory, MemberData(nameof(DualCases))]
    public void BoundNullishReceiversRetainCalleeStrictness(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
