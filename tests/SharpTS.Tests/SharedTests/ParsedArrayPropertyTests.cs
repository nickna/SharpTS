using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ParsedArrayPropertyTests
{
    [Theory, ModeData]
    public void ComputedDeletionRespectsTheArrayLengthDescriptor(ExecutionMode mode)
    {
        const string source = """
            const array = JSON.parse('[]');
            console.log(delete array['length'], array.length);
            function strictDelete(value) {
                'use strict';
                try { console.log('returned', delete value['length']); }
                catch (error) { console.log('caught', error instanceof TypeError); }
            }
            strictDelete(array);
            """;
        Assert.Equal("false 0\ncaught true\n", TestHarness.RunUnchecked(source, mode));
    }
}
