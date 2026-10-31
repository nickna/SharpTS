using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ArgumentsDefaultPrototypeTests
{
    [Theory, ModeData]
    public void ArgumentsUseObjectPrototypeAndRespectExplicitReparenting(ExecutionMode mode)
    {
        const string source = """
            function check(value: any) {
                console.log(Object.getPrototypeOf(value) === Object.prototype,
                    Object.getPrototypeOf(value) === Array.prototype, value instanceof Array, value instanceof Object, Array.isArray(value));
            }
            function inspect(first: any, second: any) {
                const args: any = arguments;
                check(args);
                Object.setPrototypeOf(args, null); check(args);
                Object.setPrototypeOf(args, Array.prototype); check(args);
            }
            inspect(1, 2);
            """;
        Assert.Equal("true false false true false\nfalse false false false false\nfalse true true true false\n", TestHarness.Run(source, mode));
    }
}
