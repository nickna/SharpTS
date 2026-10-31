using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class FunctionInterfaceHeritageTests
{
    private static void Check(string source) => new TypeChecker(strictNullChecks: true)
        .Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    [Fact]
    public void PlainAndGenericInterfacesInheritFunctionMembers()
        => Check("""
            interface Callable extends Function { tag: string; }
            interface GenericCallable<T> extends Function { value: T; }
            function read(value: Callable, generic: GenericCallable<number>) {
                const name: string = value.name;
                const length: number = generic.length;
                const tag: string = value.tag;
                const payload: number = generic.value;
                const broad: Function = value;
                value.call(null, 1);
                if (typeof value === 'function') { const kept: Callable = value; }
                if (typeof value === 'object') { const impossible: never = value; }
            }
            """);

    [Fact]
    public void InheritedMembersRetainTheirDeclaredTypes()
        => Assert.Equal("TS2322", Assert.ThrowsAny<TypeCheckException>(() => Check("""
            interface Callable extends Function { tag: string; }
            function read(value: Callable) { const wrong: string = value.length; }
            """)).Diagnostic.TsCode);

    [Fact]
    public void FunctionHeritageDoesNotInventAnInvocationSignature()
        => Assert.Equal("TS2349", Assert.ThrowsAny<TypeCheckException>(() => Check("""
            interface Callable extends Function { tag: string; }
            function invoke(value: Callable) { value(); }
            """)).Diagnostic.TsCode);
}
