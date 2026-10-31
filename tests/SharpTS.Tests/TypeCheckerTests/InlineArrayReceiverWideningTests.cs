using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class InlineArrayReceiverWideningTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["inline-with", "console.log([1].with(0,2).join(','));", "2\n"];
        yield return ["inline-push", "console.log([1].push(2));", "2\n"];
        yield return ["const-binding", "const value=[1];value.push(2);console.log(value.join(','));", "1,2\n"];
        yield return ["inline-string-with", "console.log(['a'].with(0,'b').join(','));", "b\n"];
        yield return ["inline-boolean-with", "console.log([true].with(0,false).join(','));", "false\n"];
        yield return ["bracket-push", "console.log([1]['push'](2));", "2\n"];
        yield return ["parenthesized-with", "console.log(([1]).with(0,2).join(','));", "2\n"];
        yield return ["satisfies-with", "console.log(([1] satisfies number[]).with(0,2).join(','));", "2\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void FreshMutableReceiversAcceptOtherValuesOfTheElementType(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("([1] as const).with(0,2);")]
    [InlineData("([1] as const).push(2);")]
    [InlineData("([1] as 1[]).push(2);")]
    [InlineData("const value:1[]=[1];value.push(2);")]
    [InlineData("[1].push('wrong');")]
    [InlineData("([1] satisfies (1|2)[]).with(0,2);")]
    public void ExplicitLiteralAndReadonlyDomainsRemainRestricted(string source)
        => Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
}
