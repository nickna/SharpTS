using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectDescriptorPrimitiveKeyConversionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["permitted-primitives", "for(const target of [0,'x',true,1n,Symbol('s')]){let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return 'x';}};console.log(typeof target,Object.getOwnPropertyDescriptor(target,key)===undefined,calls);}", "number true 1\nstring true 1\nboolean true 1\nbigint true 1\nsymbol true 1\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ObjectBoxingAndKeyConversionRetainTheirOrder(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
