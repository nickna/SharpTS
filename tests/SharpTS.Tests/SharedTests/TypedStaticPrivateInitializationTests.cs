using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedStaticPrivateInitializationTests
{
    [Theory, ModeData]
    public void TypedConstructorReadChecksFieldInstallation(ExecutionMode mode)
    {
        const string source = """
            class C {
                static first = C.read(C);
                static #m: number = 42;
                static read(value: typeof C): any {
                    try { return value.#m; }
                    catch (error) { return error instanceof TypeError; }
                }
            }
            console.log(C.first, C.read(C));
            """;
        Assert.Equal("true 42\n", TestHarness.Run(source, mode));
    }
}
