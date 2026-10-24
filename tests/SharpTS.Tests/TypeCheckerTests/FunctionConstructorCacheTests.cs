using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class FunctionConstructorCacheTests
{
    [Theory]
    [InlineData("number", "1", false)]
    [InlineData("string", "'bad'", true)]
    public void SameNamedConstructorsRetainIdentityInFunctionCache(string type, string value, bool rejected)
    {
        var source = $$"""
            {
                const Only = class Same { static marker: number = 1; };
                const f: (value: typeof Only) => void = (value: typeof Only) => {};
            }
            {
                const Target = class Same { static marker: number = 1; };
                const Source = class Same { static marker: {{type}} = {{value}}; };
                const f: (value: typeof Target) => void = (value: typeof Source) => {};
            }
            """;
        void Check() => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        if (rejected) Assert.ThrowsAny<TypeCheckException>(Check);
        else Check();
    }
}
