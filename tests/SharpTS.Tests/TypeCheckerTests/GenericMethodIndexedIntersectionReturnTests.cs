using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericMethodIndexedIntersectionReturnTests
{
    [Theory]
    [InlineData("string", false)]
    [InlineData("number", true)]
    public void OuterAndMethodInstantiationRetainIndexedIntersectionReturn(string expected, bool rejected)
    {
        var source = $$"""
            declare class Component<Data, Computed> {
                get<K extends keyof (Data & Computed)>(key: K): (Data & Computed)[K];
            }
            declare const component: Component<{ hello: string }, unknown>;
            const value: {{expected}} = component.get('hello');
            """;
        void Check() => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        if (rejected) Assert.ThrowsAny<TypeCheckException>(Check);
        else Check();
    }
}
