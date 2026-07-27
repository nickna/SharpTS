using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the newer RegExp flag accessor properties on instances
/// (sticky/dotAll/hasIndices/unicode/unicodeSets — issue #212).
/// Runs against both interpreter and compiler.
/// </summary>
public class RegExpFlagPropertyTests
{
    [Theory, ModeData]
    public void FlagProperties_SetFlags_ReadTrue(ExecutionMode mode)
    {
        var source = """
            console.log(/a/y.sticky);
            console.log(/a/s.dotAll);
            console.log(/a/d.hasIndices);
            console.log(/a/u.unicode);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void FlagProperties_UnsetFlags_ReadFalse(ExecutionMode mode)
    {
        var source = """
            console.log(/a/.sticky);
            console.log(/a/.dotAll);
            console.log(/a/.hasIndices);
            console.log(/a/.unicode);
            console.log(/a/.unicodeSets);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\nfalse\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void FlagsString_IncludesHasIndicesAndSticky(ExecutionMode mode)
    {
        var source = """
            console.log(/a/d.flags);
            console.log(/a/y.flags);
            console.log(/a/s.flags);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("d\ny\ns\n", output);
    }
}
