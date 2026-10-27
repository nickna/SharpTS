using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassConstructorKeyCreationOrderTests
{
    [Theory, ModeData]
    public void Issue2635Regression(ExecutionMode mode)
    {
        const string source = """
            class Owner { static first() {} static get middle() { return 1; } static last() {} }
            const names = Object.getOwnPropertyNames(Owner).filter(key => key !== 'name' && key !== 'length' && key !== 'prototype');
            console.log(names.join(','));
            """;
        Assert.Equal("first,middle,last\n", TestHarness.Run(source, mode));
    }
}
