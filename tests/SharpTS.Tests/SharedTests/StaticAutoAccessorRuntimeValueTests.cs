using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StaticAutoAccessorRuntimeValueTests
{
    [Theory, ModeData]
    public void ConstructorAliasesUseActualAutoAccessorStorage(ExecutionMode mode)
    {
        const string source = """
            class C {
                static accessor count: number = 1;
                static accessor label: string = 'one';
            }
            const alias: any = C;
            console.log(alias.count, alias.label);
            alias.count = 3;
            console.log(C.count, alias.count);
            const descriptor: any = Object.getOwnPropertyDescriptor(C, 'count');
            try { descriptor.get.call({}); } catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("1 one\n3 3\ntrue\n", TestHarness.Run(source, mode));
    }
}
