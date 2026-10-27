using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class SwitchDefaultDiscriminantTests
{
    private static void Check(string source) => new TypeChecker(strictNullChecks: true)
        .Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    [Theory]
    [InlineData("case 'a': break; case 'c': break; default: const correct: number = value.b;")]
    [InlineData("default: const correct: number = value.b; break; case 'a': break; case 'c': break;")]
    [InlineData("case 'a': case 'c': break; default: const correct: number = value.b;")]
    public void DirectDefaultEntryExcludesAllTestedDiscriminants(string clauses)
        => Check($$"""
            type State = {kind:'a', a:string} | {kind:'b', b:number} | {kind:'c', a:string};
            function read(value: State) {
                switch (value.kind) { {{clauses}} }
            }
            """);

    [Fact]
    public void FallthroughRetainsThePreviousDiscriminant()
        => Assert.Equal("TS2339", Assert.ThrowsAny<TypeCheckException>(() => Check("""
            type State = {kind:'a', a:string} | {kind:'b', b:number};
            function read(value: State) {
                switch (value.kind) { case 'a': default: const wrong: number = value.b; }
            }
            """)).Diagnostic.TsCode);

    [Fact]
    public void CaseWritesInvalidateDefaultDiscriminantProof()
        => Assert.Equal("TS2339", Assert.ThrowsAny<TypeCheckException>(() => Check("""
            type State = {kind:'a', a:string} | {kind:'b', b:number};
            function read(value: State, replacement: State) {
                switch (value.kind) {
                    case (value = replacement, 'a'): break;
                    default: const wrong: number = value.b;
                }
            }
            """)).Diagnostic.TsCode);
}
