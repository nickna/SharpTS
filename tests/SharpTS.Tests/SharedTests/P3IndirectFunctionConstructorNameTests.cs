using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3IndirectFunctionConstructorNameTests
{
    [Theory, ModeData]
    public void AliasedFunctionConstructorReportsJavaScriptName(ExecutionMode mode)
    {
        const string source = """
            const constructor:any = Error.constructor;
            console.log(constructor === Function, constructor.name, constructor['name'], Reflect.get(constructor, 'name'));
            console.log(Function.name, Object.getPrototypeOf(function(){}).constructor.name);
            """;
        Assert.Equal("true Function Function Function\nFunction Function\n", TestHarness.Run(source, mode));
    }
}
