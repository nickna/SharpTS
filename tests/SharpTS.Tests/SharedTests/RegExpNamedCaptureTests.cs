using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for RegExp named capture groups. Runs against both interpreter and compiler.
/// </summary>
public class RegExpNamedCaptureTests
{
    [Theory, ModeData]
    public void Exec_NamedGroups_PopulatesGroupsObject(ExecutionMode mode)
    {
        var source = """
            let regex = /(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})/;
            let match = regex.exec("2024-03-15");
            console.log(match.groups.year);
            console.log(match.groups.month);
            console.log(match.groups.day);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2024\n03\n15\n", output);
    }

    [Theory, ModeData]
    public void Exec_NamedGroups_IndexAndInput(ExecutionMode mode)
    {
        var source = """
            let regex = /(?<word>\w+)/;
            let match = regex.exec("hello world");
            console.log(match[0]);
            console.log(match.index);
            console.log(match.groups.word);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n0\nhello\n", output);
    }

    [Theory, ModeData]
    public void Exec_NoNamedGroups_GroupsIsNull(ExecutionMode mode)
    {
        var source = """
            let regex = /(\d+)-(\d+)/;
            let match = regex.exec("123-456");
            console.log(match.groups);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory, ModeData]
    public void Match_NamedGroups_NonGlobal(ExecutionMode mode)
    {
        var source = """
            let str = "2024-03-15";
            let match: any = str.match(/(?<year>\d{4})-(?<month>\d{2})/);
            console.log(match.groups.year);
            console.log(match.groups.month);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2024\n03\n", output);
    }

    [Theory, ModeData]
    public void MatchAll_NamedGroups(ExecutionMode mode)
    {
        var source = """
            let str = "2024-03 and 2025-12";
            let matches = [...str.matchAll(/(?<year>\d{4})-(?<month>\d{2})/g)];
            console.log(matches.length);
            console.log(matches[0].groups.year);
            console.log(matches[0].groups.month);
            console.log(matches[1].groups.year);
            console.log(matches[1].groups.month);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n2024\n03\n2025\n12\n", output);
    }

    [Theory, ModeData]
    public void Exec_NoMatch_ReturnsNull(ExecutionMode mode)
    {
        var source = """
            let regex = /(?<name>\d+)/;
            let match = regex.exec("no digits here");
            console.log(match === null);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Exec_MixedNamedAndUnnamed(ExecutionMode mode)
    {
        var source = """
            let regex = /(\d+)-(?<name>\w+)/;
            let match = regex.exec("42-hello");
            console.log(match[0]);
            console.log(match[1]);
            console.log(match[2]);
            console.log(match.groups.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42-hello\n42\nhello\nhello\n", output);
    }
}
