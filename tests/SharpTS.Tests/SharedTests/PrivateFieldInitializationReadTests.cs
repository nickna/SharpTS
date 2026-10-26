using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrivateFieldInitializationReadTests
{
    [Theory, ModeData]
    public void LaterPrivateFieldIsNotReadableUntilInstalled(ExecutionMode mode)
    {
        const string source = """
            class C {
                #first = this.read();
                #second = 1;
                read(): any { return this.#second; }
            }
            class Generic<T> {
                #first = this.read();
                #second = 1;
                read(): any { return this.#second; }
            }
            const Expression = class {
                #first = this.read();
                #second = 1;
                read(): any { return this.#second; }
            };
            try { new C(); } catch (error: any) { console.log(error instanceof TypeError); }
            try { new Generic<number>(); } catch (error: any) { console.log(error instanceof TypeError); }
            try { new Expression(); } catch (error: any) { console.log(error instanceof TypeError); }
            class Ready {
                #first: any;
                #second = this.read();
                read(): any { return this.#first; }
            }
            console.log(new Ready().read() === undefined);
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }
}
