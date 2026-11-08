using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EnumIndexReadTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void QuotedAndIdentifierMembersCanBeReadByTheirStringKeys(ExecutionMode mode)
    {
        const string source = """
            enum Status { Ready = 3, "not-ready" = 4 }
            const enum Tag { "ready-now" = "yes" }
            const ready: Status = Status["Ready"];
            const pending: Status = Status["not-ready"];
            const tag: Tag = Tag["ready-now"];
            console.log(ready, pending, tag, Status[4]);
            """;
        Assert.Equal("3 4 yes not-ready\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("E.B")]
    [InlineData("E[\"B\"]")]
    public void AStringMemberOfAMixedEnumDoesNotAssignToNumber(string member)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            const enum E { A = 1, B = "b" }
            const text: string = {{member}};
            const value: number = {{member}};
            """);
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(statements));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
        Assert.Equal(3, error.Diagnostic.Location?.Line);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void AnEnumStringKeyCanReadAnInheritedProperty(ExecutionMode mode)
    {
        const string source = """
            enum E { A = 1 }
            console.log(typeof E['hasOwnProperty'], E['missing']);
            """;
        Assert.Equal("function undefined\n", TestHarness.RunUnchecked(source, mode));
    }
}
