using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ArrowParameterAssignmentTests
{
    [Theory, ModeData]
    public void GuardedParameterAssignmentUsesDeclaredUnion(ExecutionMode mode)
    {
        const string source = """
            const read = (value: string | number) => {
                if (typeof value === "string") { value = 1; }
                return value;
            };
            console.log(read("text"));
            const outer = (value: string | number) => {
                const inner = (value: string | boolean) => {
                    if (typeof value === "string") value = true;
                    return value;
                };
                if (typeof value === "string") value = 2;
                console.log(inner("text"));
                return value;
            };
            console.log(outer("text"));
            """;
        Assert.Equal("1\ntrue\n2\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("value = true;")]
    [InlineData("value = 1; const invalid: string = value;")]
    public void GuardedParameterAssignmentStillChecksWritesAndSubsequentReads(string body)
    {
        string source = "const read = (value: string | number) => { " +
            "if (typeof value === \"string\") { " + body + " } };";
        Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
    }
}

