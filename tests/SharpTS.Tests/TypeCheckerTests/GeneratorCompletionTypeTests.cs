using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GeneratorCompletionTypeTests
{
    private static void Check(string source)
        => new TypeChecker(strictNullChecks: true).Check(new Parser(new Lexer(source).ScanTokens()).Parse().Statements);

    [Theory, ModeData]
    public void AnnotatedGeneratorReturnsItsCompletionType(ExecutionMode mode)
        => Assert.Equal("1 done\n", TestHarness.Run("""
            function* make(): Generator<number, string, unknown> { yield 1; return 'done'; }
            const iterator: any = make();
            console.log(iterator.next().value, iterator.next().value);
            """, mode));

    [Fact]
    public void CompletionTypesDefaultToAnyAndSubstituteGenericParameters()
        => Check("""
            function* defaulted(): Generator<number> { yield 1; return 'done'; }
            function* generic<T>(value: T): Generator<number, T, unknown> { yield 1; return value; }
            async function* asyncCompletion(): AsyncGenerator<number, string, unknown> { yield 1; return 'done'; }
            async function* adoptedCompletion(): AsyncGenerator<number, string, unknown> { yield 1; return Promise.resolve('done'); }
            """);

    [Theory]
    [InlineData("function* bad(): Generator<number, string, unknown> { yield 1; return 2; }")]
    [InlineData("async function* bad(): AsyncGenerator<number, string, unknown> { yield 1; return 2; }")]
    [InlineData("function* bad(): Generator<number, void, unknown> { yield 1; return 'bad'; }")]
    public void IncompatibleCompletionValuesRemainInvalid(string source)
        => Assert.Equal("TS2322", Assert.ThrowsAny<TypeCheckException>(() => Check(source)).Diagnostic.TsCode);
}
