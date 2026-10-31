using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class OptionalSpreadPresenceRuntimeTests
{
    [Theory, ModeData]
    public void OptionalOverridesCopyOnlyPresentPropertiesAndEvaluateGettersOnce(ExecutionMode mode)
    {
        const string source = """
            interface Maybe { value?:number; }
            let reads=0;
            const absent:Maybe={};
            const present:Maybe={get value(){reads++;return 2;}};
            const before:{value:number}={value:1,...absent};
            const after:{value:number}={value:1,...present};
            console.log(before.value,after.value,reads);
            function combine(use:boolean):number{
                const source:{value:number}|undefined=use?{value:4}:undefined;
                const result:{value:number}={value:3,...source};
                return result.value;
            }
            console.log(combine(false),combine(true));
            """;
        Assert.Equal("1 2 1\n3 4\n", TestHarness.Run(source, mode));
    }
}
