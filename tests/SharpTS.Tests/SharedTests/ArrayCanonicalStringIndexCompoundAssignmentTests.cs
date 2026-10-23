using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayCanonicalStringIndexCompoundAssignmentTests
{
    public const string IssueSource = """
        const values = [1];
        const result = values["0"] += 2;
        console.log(result, values[0]);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["canonical-numeric-string-index-issue", IssueSource, "3 3\n"];
        yield return ["literal-string-keys-preserve-primitive-compound-results", """
            const words: string[] = ['left'];
            const key: '0' = '0';
            const joined: string = (words[key] += 'right');
            const numbers: number[] = [10];
            const difference: number = (numbers['0'] -= 3);
            const product: number = (numbers[key] *= 2);
            const bits: number = (numbers['0'] |= 1);
            const dynamic: any[] = ['value:'];
            console.log(joined, words[0], difference, product, bits, numbers[0], dynamic[key] += 2, dynamic[0]);
            const sparse: any[] = [];
            sparse[2147483648] = 4;
            const large = sparse['2147483648'] += 2;
            console.log(large, sparse.length, Object.hasOwn(sparse, '2147483648'), Object.hasOwn(sparse, '0'));
            """, "leftright leftright 7 14 15 15 value:2 value:2\n6 2147483649 true false\n"];
        yield return ["string-key-reference-is-evaluated-once-before-rhs-and-retains-old-value", """
            const values: number[] = [4], other: number[] = [20];
            let selected: number[] = values;
            const state = { receivers: 0, indexes: 0, rights: 0, order: '' };
            function receiver(): number[] { state.receivers++; state.order += 'receiver>'; return selected; }
            function index(): '0' { state.indexes++; state.order += 'index>'; return '0'; }
            function right(): number {
                state.rights++; state.order += 'rhs>'; values[0] = 100; selected = other; return 2;
            }
            const result = receiver()[index()] += right();
            console.log(result, values[0], other[0], state.receivers, state.indexes, state.rights, state.order);
            """, "6 6 20 1 1 1 receiver>index>rhs>\n"];
        yield return ["canonical-string-index-invokes-accessors-and-abrupt-get-prevents-rhs", """
            const values: any[] = [0], marker = {};
            const state = { value: 4, gets: 0, rights: 0, sets: 0, same: true, order: '' };
            Object.defineProperty(values, '0', {
                get() { state.gets++; state.same = state.same && this === values; state.order += 'get>'; return state.value; },
                set(next: number) { state.sets++; state.same = state.same && this === values; state.order += 'set>'; state.value = next; },
                configurable: true
            });
            function right(): number { state.rights++; state.order += 'rhs>'; state.value = 100; return 2; }
            const result = values['0'] += right();
            console.log(result, state.value, state.gets, state.rights, state.sets, state.same, state.order);
            const blocked: any[] = [0];
            Object.defineProperty(blocked, '0', { get() { throw marker; }, configurable: true });
            try { blocked['0'] += right(); console.log('unexpected'); }
            catch (error) { console.log(error === marker, state.rights, state.sets, state.order); }
            """, "6 6 1 1 1 true get>rhs>set>\ntrue 1 1 get>rhs>set>\n"];
    }

    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["readonly-canonical-string-index-still-rejects-write", """
            const values: readonly number[] = [1];
            values['0'] += 2;
            """, "TS2542", 2];
        yield return ["numeric-slot-cannot-store-string-result", """
            const values: number[] = [1];
            values['0'] += 'right';
            """, "TS2322", 2];
        yield return ["boolean-slot-plus-number-is-invalid", """
            const values: boolean[] = [true];
            values['0'] += 1;
            """, "TS2365", 2];
        // Preserve the existing non-+= TS2365 diagnostic; the strict TypeScript oracle uses TS2362.
        yield return ["string-slot-subtraction-remains-invalid", """
            const values: string[] = ['left'];
            values['0'] -= 1;
            """, "TS2365", 2];
        yield return ["readonly-canonical-string-subtraction-still-rejects-write", """
            const values: readonly number[] = [1];
            values['0'] -= 1;
            """, "TS2542", 2];
        // Preserve SharpTS's TS7053 diagnostic; the strict TypeScript oracle uses TS7015.
        yield return ["noncanonical-numeric-looking-string-remains-invalid-index", """
            const values: number[] = [1];
            values['00'] += 2;
            """, "TS7053", 2];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void CanonicalStringIndexPreservesReadonlyAndArithmeticDiagnostics(
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
    public void CanonicalStringIndicesPreserveArrayCompoundReferences(
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
