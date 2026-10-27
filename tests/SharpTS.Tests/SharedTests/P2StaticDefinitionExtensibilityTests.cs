using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2StaticDefinitionExtensibilityTests
{
    [Theory, ModeData]
    public void StaticFieldsThrowOnNonextensibleConstructorAfterInitializerEffects(ExecutionMode mode)
    {
        const string source = """
            let saved: any, effects = "";
            try {
                class Owner {
                    static { saved = this; Object.preventExtensions(this); }
                    static value = (effects += "rhs", 1);
                    static later = (effects += ",later", 2);
                }
                console.log("accepted");
            }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(effects, Object.hasOwn(saved, "value"), Object.hasOwn(saved, "later"));
            """;
        Assert.Equal("true\nrhs false false\n", TestHarness.Run(source, mode));
    }
}
