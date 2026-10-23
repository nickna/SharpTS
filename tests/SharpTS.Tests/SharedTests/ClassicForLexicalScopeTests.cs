using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassicForLexicalScopeTests
{
    public const string IssueSource = """
        const named = () => 7;
        for (let named = 0; named < 1; named++) {}
        const read = () => named;
        console.log(typeof read(), read() === named);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["module-callable-after-same-name-let-issue", IssueSource, "function true\n"];
        yield return ["ordinary-named-expression-private-binding-after-loop", """
            const target: any = function named(): void {
                const identity = named;
                for (let named = 0; named < 1; named++) {}
                const read = () => named;
                console.log(typeof read(), read() === identity);
            };
            target();
            """, "function true\n"];
        yield return ["multiple-let-and-const-head-bindings-stay-local", """
            const first = () => 7, second = () => 8;
            for (let first = 0, second = 2; first < 1; first++) { console.log(first, second); }
            const readFirst = () => first, readSecond = () => second;
            console.log(typeof readFirst(), readFirst() === first, typeof readSecond(), readSecond() === second);
            for (const first = 'local', second = false; false;) {}
            const readConstFirst = () => first, readConstSecond = () => second;
            console.log(typeof readConstFirst(), readConstFirst() === first,
                typeof readConstSecond(), readConstSecond() === second);
            """, "0 2\nfunction true function true\nfunction true function true\n"];
        yield return ["var-head-visibility-and-assignment-head-preservation", """
            function run() {
                let value = 10;
                for (var count = 0, limit = 2; count < limit; count++) { value++; }
                console.log(count, limit, value);
                for (value = 1; value < 3; value++) {}
                console.log(value);
                for (let index = 0; index < 1; index++) { var result = 7; }
                const read = () => result;
                console.log(read());
            }
            run();
            """, "2 2 12\n3\n7\n"];
        yield return ["outer-union-narrowing-and-unrelated-initializer-assignment", """
            function run(flag: boolean) {
                let value: string | number = flag ? 'outer' : 10;
                let side: string | number = 'before';
                if (typeof value === 'string') {
                    for (let value = 0, unused = (side = 42); value < 1; value++) {}
                    console.log(value.toUpperCase(), side.toFixed(0));
                }
            }
            run(true);
            """, "OUTER 42\n"];
        yield return ["property-narrowing-restoration-keeps-unrelated-property-writes", """
            function run(holder: { value: string | number }, other: { value: string | number }) {
                if (typeof holder.value === 'string' && typeof other.value === 'string') {
                    for (let holder = 0, unused = (other.value = 42); holder < 1; holder++) {}
                    console.log(holder.value.toUpperCase(), other.value.toFixed(0));
                }
            }
            run({ value: 'property' }, { value: 'before' });
            """ + "\r\n", "PROPERTY 42\n"];
        yield return ["nested-same-name-lexical-loops-restore-each-owning-binding", """
            const named = () => 7;
            let turns = 0;
            for (const named = 'outer'; turns < 1; turns++) {
                for (let named = 0; named < 1; named++) {}
                const read = () => named;
                console.log(read().toUpperCase());
            }
            const read = () => named;
            console.log(typeof read(), read() === named);
            """, "OUTER\nfunction true\n"];
        yield return ["typed-header-initializer-arrow-uses-the-header-binding", """
            let value = 'outer';
            for (let value: () => void = () => { value = () => {}; }; false;) {}
            console.log(value);
            """, "outer\n"];
    }

    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["let-header-is-unavailable-after-loop", """
            for (let hidden = 0; hidden < 1; hidden++) {}
            try { hidden; } catch (error) { console.log(error instanceof ReferenceError); }
            """, "TS2304", 2];
        yield return ["const-header-is-unavailable-after-loop", """
            for (const hidden = 1; false;) {}
            try { hidden; } catch (error) { console.log(error instanceof ReferenceError); }
            """, "TS2304", 2];
        yield return ["outer-callable-type-is-not-replaced-by-header-number", """
            const named = () => 7;
            for (let named = 0; named < 1; named++) {}
            const wrong: number = named;
            """, "TS2322", 3];
        yield return ["body-var-keeps-its-numeric-inferred-return-type", """
            function run() {
                for (let index = 0; index < 1; index++) { var result = 7; }
                const read = () => result;
                const wrong: string = read();
            }
            """, "TS2322", 4];
        yield return ["private-function-binding-type-is-not-replaced-by-header-number", """
            const target: any = function named(): void {
                for (let named = 0; named < 1; named++) {}
                const read = () => named;
                const wrong: number = read();
            };
            """, "TS2322", 4];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void LexicalHeadersDoNotLeakNamesOrErasePreciseOuterTypes(
        string name, string source, string code, int line)
    {
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        var result = new TypeChecker().CheckWithRecovery(statements, document);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == code && diagnostic.Line == line);
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    public static IEnumerable<object[]> SavedCases() => Cases();

    [Theory, MemberData(nameof(ModeCases))]
    public void ClassicForLexicalBindingsPreserveOuterTypesAfterLoopExit(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
