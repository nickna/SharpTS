using SharpTS.Tests.Infrastructure;
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
}
