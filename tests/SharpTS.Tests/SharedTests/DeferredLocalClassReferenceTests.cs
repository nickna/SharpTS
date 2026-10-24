using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class DeferredLocalClassReferenceTests
{
    [Theory, ModeData]
    public void DeferredFunctionReferencesLaterLocalClass(ExecutionMode mode)
    {
        const string source = """
            function make() {
                function before() { return C; }
                class C {}
                return before;
            }
            const read = make();
            console.log(read() === read());
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("function make() { C; class C {} }")]
    [InlineData("function make() { function before() { return C.missing; } class C {} }")]
    public void ForwardClassRetainsOrderAndMemberDiagnostics(string source)
        => Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
}
