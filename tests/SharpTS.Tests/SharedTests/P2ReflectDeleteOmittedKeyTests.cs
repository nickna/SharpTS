using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ReflectDeleteOmittedKeyTests
{
    // The compiled Reflect wrapper independently supplies missing arguments.
    [Theory, InterpretedOnlyData]
    public void DeletePropertyConvertsMissingKeyToUndefined(ExecutionMode mode)
    {
        const string source = """
            const omitted: any = { undefined: 1 }, explicit: any = { undefined: 1 };
            console.log((Reflect.deleteProperty as any)(omitted), Object.hasOwn(omitted, "undefined"));
            console.log(Reflect.deleteProperty(explicit, undefined as any), Object.hasOwn(explicit, "undefined"));
            console.log(Reflect.deleteProperty.length);
            try { (Reflect.deleteProperty as any)(); }
            catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("true false\ntrue false\n2\ntrue\n", TestHarness.Run(source, mode));
    }
}
