using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassInstanceOptionalAssignmentTests
{
    [Theory]
    [InlineData("value: number = 1;", "value?: number;", true)]
    [InlineData("value?: number;", "", false)]
    [InlineData("value?: number;", "value: number = 1;", false)]
    [InlineData("value?: number;", "other: string = '';", true)]
    [InlineData("value?: number;", "value: string = '';", true)]
    public void OrdinaryInstanceRelationRetainsOptionalPresence(string target, string source, bool rejected)
    {
        var text = $"class Target {{ {target} }} class Source {{ {source} }} const value: Target = new Source();";
        void Check() => new TypeChecker().Check(new Parser(new Lexer(text).ScanTokens()).ParseOrThrow());
        if (rejected) Assert.ThrowsAny<TypeCheckException>(Check);
        else Check();
    }
}
