using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ComputedStringMethodTests
{
    [Theory, ModeData]
    public void StaticAndInstanceLiteralMethodsKeepTheirOwnSignaturesAndBodies(ExecutionMode mode)
    {
        const string source = """
            class C {
                [""](): number { return 1; }
                static [""](): string { return "static"; }
                ["read"](): number { return 2; }
                static ["read"](): string { return "method"; }
                [7](): number { return 3; }
                static [7](): string { return "numeric"; }
            }
            const instance = new C();
            const instanceValue: number = instance[""]();
            const staticValue: string = C[""]();
            console.log(instanceValue, staticValue, instance.read(), C.read(), instance[7](), C["7"]());
            const Expression = class {
                [""](): number { return 4; }
                static [""](): string { return "expression"; }
            };
            console.log(new Expression()[""](), Expression[""]());
            """;
        Assert.Equal("1 static 2 method 3 numeric\n4 expression\n", TestHarness.Run(source, mode));
    }
}
