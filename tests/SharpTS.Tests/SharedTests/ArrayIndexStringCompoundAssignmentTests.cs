using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayIndexStringCompoundAssignmentTests
{
    public const string IssueSource = """
        const values = ["left"];
        values[0] += "right";
        console.log(values[0]);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["inferred-string-array-plus-equals-issue", IssueSource, "leftright\n"];
        yield return ["typed-and-any-arrays-with-statically-string-rhs", """
            const typed: string[] = ['left'], dynamic: any[] = ['left'];
            const suffix: string = 'right';
            const combined: string = (typed[0] += suffix);
            console.log(combined, typed[0], dynamic[0] += suffix, dynamic[0]);
            """, "leftright leftright leftright leftright\n"];
        yield return ["string-array-with-numeric-primitive-rhs", """
            const values: string[] = ['value:'];
            const combined: string = (values[0] += 42);
            console.log(combined, values[0]);
            """, "value:42 value:42\n"];
        yield return ["any-rhs-and-variable-named-property-positive-controls", """
            const values: any[] = ['left'];
            const suffix: any = 'right';
            let text = 'left';
            const holder = { text: 'left' };
            console.log(values[0] += suffix, text += 'right', holder.text += 'right');
            console.log(values[0], text, holder.text);
            """, "leftright leftright leftright\nleftright leftright leftright\n"];
        yield return ["primitive-string-operands-evaluate-receiver-index-rhs-once", """
            const values: string[] = ['left'], events: string[] = [];
            let receivers = 0, indexes = 0, rights = 0;
            function receiver(): string[] { events.push('receiver'); receivers++; return values; }
            function index(): number { events.push('index'); indexes++; return 0; }
            function right(): string { events.push('rhs'); rights++; return 'right'; }
            const combined: string = (receiver()[index()] += right());
            console.log(combined, values[0], receivers, indexes, rights, events.join(','));
            """, "leftright leftright 1 1 1 receiver,index,rhs\n"];
    }

    public static IEnumerable<object[]> AcceptedCheckerCases()
    {
        yield return ["numeric-never-rhs-preserves-number-result-without-execution", """
            function fail(): never { throw new Error('unreachable'); }
            function check(values: number[]) {
                const result: number = (values[0] += fail());
                return result;
            }
            """];
        yield return ["literal-slots-widen-expression-results-to-primitives", """
            function check(strings: 'left'[], numbers: 1[]) {
                const text: string = (strings[0] += 'right');
                const numeric: number = (numbers[0] += 2);
                return [text, numeric];
            }
            """];
        yield return ["union-array-plus-string-result-is-string", """
            function check(values: (string | number)[]) {
                const text: string = (values[0] += 'right');
                return text;
            }
            """];
        yield return ["any-array-and-any-rhs-compound-result-remains-any", """
            function check(values: any[], suffix: any) {
                const text: string = (values[0] += suffix);
                const numeric: number = (values[0] += suffix);
                return [text, numeric];
            }
            """];
        yield return ["any-constrained-generic-slot-with-any-rhs-remains-any", """
            function check<T extends any>(values: T[], suffix: any) {
                const text: string = (values[0] += suffix);
                const numeric: number = (values[0] += suffix);
                return [text, numeric];
            }
            """];
    }

    [Theory, MemberData(nameof(AcceptedCheckerCases))]
    public void AdditionResultsPreservePreciseCheckerTypesWithoutExecution(string name, string source)
    {
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        var result = new TypeChecker().CheckWithRecovery(statements, document);

        Assert.Empty(result.Diagnostics);
    }

    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["string-result-cannot-write-number-array", """
            const values: number[] = [1];
            values[0] += 'right';
            """, "TS2322", 2];
        // Preserve the existing non-+= TS2365 diagnostic; strict TypeScript codes are in the oracle record.
        yield return ["string-left-is-not-numeric-subtraction", """
            const values: string[] = ['left'];
            values[0] -= 1;
            """, "TS2365", 2];
        // Preserve the existing non-+= TS2365 diagnostic; strict TypeScript codes are in the oracle record.
        yield return ["string-right-is-not-numeric-multiplication", """
            const values: number[] = [1];
            values[0] *= 'right';
            """, "TS2365", 2];
        yield return ["boolean-plus-number-remains-invalid", """
            const values: boolean[] = [true];
            values[0] += 1;
            """, "TS2365", 2];
        yield return ["readonly-string-array-still-rejects-element-write", """
            const values: readonly string[] = ['left'];
            values[0] += 'right';
            """, "TS2542", 2];
        yield return ["string-literal-slot-result-is-not-the-old-literal", """
            function check(values: 'left'[]) {
                const wrong: 'left' = (values[0] += 'right');
            }
            """, "TS2322", 2];
        yield return ["number-literal-slot-result-is-not-the-old-literal", """
            function check(values: 1[]) {
                const wrong: 1 = (values[0] += 2);
            }
            """, "TS2322", 2];
        yield return ["any-array-plus-string-result-cannot-initialize-number", """
            function check(values: any[]) {
                const wrong: number = (values[0] += 'right');
            }
            """, "TS2322", 2];
        yield return ["string-array-plus-any-result-cannot-initialize-number", """
            function check(values: string[], suffix: any) {
                const wrong: number = (values[0] += suffix);
            }
            """, "TS2322", 2];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void InvalidArithmeticAndIncompatibleElementWritesRemainRejected(
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

    [Theory, MemberData(nameof(ModeCases))]
    public void NumericArrayIndexPlusEqualsAcceptsStringConcatenation(
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
