using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class HandlerForwardReferenceTests
{
    [Theory, ModeData]
    public void HandlerClosuresReferenceTheirLaterLexicalBinding(ExecutionMode mode)
    {
        const string source = """
            const value = "outer";
            try {
                const read = () => value;
                let value: number = 1;
                console.log(read());
            } catch (error) {}
            try { throw 0; } catch (error: unknown) {
                const read = () => value;
                const value: number = 2;
                console.log(read());
            } finally {
                const read = () => value;
                let value: number = 3;
                console.log(read());
            }
            try { throw 0; } catch {
                const read = () => value;
                const value: number = 4;
                console.log(read());
            }
            console.log(value);
            """;
        Assert.Equal("1\n2\n3\n4\nouter\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AccessorHandlerVarsKeepTheirCallableScope(ExecutionMode mode)
    {
        const string source = """
            const C = class {
                get value(): number {
                    try { var value = 1; let local = 2; value += local; } finally {}
                    return value;
                }
            };
            console.log(new C().value);
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("class C { get value(): string { try { var value = 1; } finally {} return value; } }")]
    [InlineData("try { let hidden = 1; } catch (error) {} console.log(hidden);")]
    [InlineData("try {} finally { let hidden = 1; } console.log(hidden);")]
    [InlineData("try { throw 0; } catch (error: unknown) { const n: number = error; }")]
    public void HandlerBindingsRetainScopeAndCatchType(string source)
        => Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
}
