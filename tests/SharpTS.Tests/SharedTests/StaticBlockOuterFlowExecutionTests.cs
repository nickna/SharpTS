using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StaticBlockOuterFlowExecutionTests
{
    [Theory, ModeData]
    public void StaticBlockFlowMatchesDeclarationOrderAndLexicalOwnership(ExecutionMode mode)
    {
        const string source = """
            let value: string | number = 'before';
            class C {
                static { { value = 1; } }
                static { const checked: number = value; console.log(checked); }
                static { let value = 'local'; console.log(value); }
                static { value = 'after'; }
            }
            const checked: string = value;
            console.log(checked);
            """;
        Assert.Equal("1\nlocal\nafter\n", TestHarness.Run(source, mode));
    }
}
