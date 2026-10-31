using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PrimitiveDescriptorBagTests
{
    [Theory, ModeData]
    public void StringDescriptorBagsAreBoxedBeforeDescriptorConversion(ExecutionMode mode)
    {
        const string source = """
            try { Object.create(null, 'abc'); console.log('accepted'); }
            catch (error) { console.log(error instanceof TypeError); }
            try { Object.defineProperties({}, 'abc'); console.log('accepted'); }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.keys(Object.create(null, '')).length,
                Object.keys(Object.defineProperties({}, '')).length);
            """;
        Assert.Equal("true\ntrue\n0 0\n", TestHarness.Run(source, mode));
    }
}
