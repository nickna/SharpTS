using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PromiseSubclassExecutorValidationTests
{
    [Theory, ModeData]
    public void InvalidExecutorsThrowBeforeReturningSubclassInstance(ExecutionMode mode)
    {
        const string source = """
            class C extends Promise<any> {
                constructor(executor: any) { super(executor); }
            }
            const invalid: any[] = [7, null, undefined, {}, "executor"];
            for (const executor of invalid) {
                try { new C(executor); console.log("accepted"); }
                catch (error: any) { console.log(error instanceof TypeError); }
            }
            new C((resolve: any) => resolve(42)).then(value => console.log(value));
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n42\n", TestHarness.Run(source, mode));
    }
}
