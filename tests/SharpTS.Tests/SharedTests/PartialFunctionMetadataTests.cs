using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PartialFunctionMetadataTests
{
    [Theory, ModeData]
    public void PartialMetadataDefinitionsPreserveOmittedAttributes(ExecutionMode mode)
    {
        const string source = """
            function declared() {}
            const expression = function() {};
            const arrow = () => {};
            const bound = declared.bind(null);
            const functions: any[] = [declared, expression, arrow, bound];
            for (const fn of functions) {
                for (const key of ["name", "length"]) {
                    Object.defineProperty(fn, key, { value: 3.66 });
                    const first: any = Object.getOwnPropertyDescriptor(fn, key);
                    console.log(first.value, first.writable, first.enumerable, first.configurable);
                    Object.defineProperty(fn, key, { value: 8 });
                    console.log(Object.getOwnPropertyDescriptor(fn, key)!.value);
                    Object.defineProperty(fn, key, { configurable: false });
                    try { Object.defineProperty(fn, key, { value: 9 }); }
                    catch (error: any) { console.log(error instanceof TypeError); }
                }
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("3.66 false false true\n8\ntrue\n", 8)), TestHarness.Run(source, mode));
    }
}
