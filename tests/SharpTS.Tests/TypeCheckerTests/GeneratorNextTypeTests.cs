using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class GeneratorNextTypeTests
{
    private static void Check(string source) => new TypeChecker(strictNullChecks: true)
        .Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    [Fact]
    public void YieldExpressionsUseTheNextInputDomain()
        => Check("""
            function* run(): Generator<string, void, number[]> {
                let sink = 0;
                [sink] = yield 'test';
                const resumed: number[] = yield;
            }
            async function* asyncRun(): AsyncGenerator<string, void, number[]> {
                const resumed: number[] = yield 'test';
            }
            function* generic<T>(): Generator<string, void, T> {
                const resumed: T = yield 'test';
            }
            """);

    [Fact]
    public void YieldedTypeDoesNotBecomeTheResumeType()
        => Assert.Equal("TS2322", Assert.ThrowsAny<TypeCheckException>(() => Check("""
            function* run(): Generator<string, void, number[]> {
                const wrong: string = yield 'test';
            }
            """)).Diagnostic.TsCode);

    [Fact]
    public void DelegationUsesTheDelegateCompletionDomain()
        => Check("""
            function* delegate(): Generator<string, number, unknown> { yield 'test'; return 1; }
            function* run(): Generator<string, void, unknown> {
                const completed: number = yield* delegate();
            }
            """);

    [Theory, ModeData]
    public void DestructuringUsesTheDeclaredNextElementType(ExecutionMode mode)
        => Assert.Equal("test\nmatch7\n", TestHarness.Run("""
            function* run(value: number): Generator<string, void, number[]> {
                let sink = 0;
                switch (value) {
                    default: yield 'default'; break;
                    case ([sink] = yield 'test', sink): yield 'match' + sink;
                }
            }
            const iterator: any = run(7);
            console.log(iterator.next().value);
            console.log(iterator.next([7]).value);
            """, mode));
}
